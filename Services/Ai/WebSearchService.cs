using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using Dana.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Dana.Services.Ai;

public record WebResult(string Title, string Url, string Snippet);

/// <summary>Búsqueda web para los agentes mediante APIs oficiales: Brave Search (clave), OpenAI Responses (web_search) o Anthropic (web_search). Y lectura de páginas públicas.</summary>
public partial class WebSearchService
{
    private readonly IHttpClientFactory _http;
    private readonly SettingsService _settings;
    private readonly Func<string?, string?> _unprotect;

    public WebSearchService(IHttpClientFactory http, SettingsService settings, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dp)
    {
        _http = http; _settings = settings;
        var protector = dp.CreateProtector("Dana.AiKeys");
        _unprotect = v => { if (string.IsNullOrEmpty(v)) return null; try { return protector.Unprotect(v); } catch { return null; } };
    }

    public string Provider(AiSettings ai) =>
        !string.IsNullOrEmpty(_unprotect(ai.BraveKeyProtected)) ? "brave" :
        !string.IsNullOrEmpty(_unprotect(ai.OpenAiKeyProtected)) ? "openai" :
        !string.IsNullOrEmpty(_unprotect(ai.AnthropicKeyProtected)) ? "anthropic" : "";

    public async Task<List<WebResult>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var ai = (await _settings.GetAsync()).Ai;
        return Provider(ai) switch
        {
            "brave" => await BraveAsync(_unprotect(ai.BraveKeyProtected)!, query, limit, ct),
            "openai" => await OpenAiAsync(_unprotect(ai.OpenAiKeyProtected)!, ai.OpenAiModel, query, limit, ct),
            "anthropic" => await AnthropicAsync(_unprotect(ai.AnthropicKeyProtected)!, ai.AnthropicModel, query, limit, ct),
            _ => throw new InvalidOperationException("No hay un proveedor de búsqueda web configurado (clave de Brave, OpenAI o Anthropic)."),
        };
    }

    private async Task<List<WebResult>> BraveAsync(string key, string query, int limit, CancellationToken ct)
    {
        var c = _http.CreateClient("websearch");
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={limit}");
        req.Headers.Add("X-Subscription-Token", key); req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await c.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Brave {(int)resp.StatusCode}: {json[..Math.Min(200, json.Length)]}");
        var results = JsonNode.Parse(json)?["web"]?["results"] as JsonArray;
        return (results ?? []).Select(r => new WebResult(r?["title"]?.GetValue<string>() ?? "", r?["url"]?.GetValue<string>() ?? "", StripTags(r?["description"]?.GetValue<string>() ?? ""))).Where(r => r.Url.Length > 0).Take(limit).ToList();
    }

    private const string JsonInstruction = "Busca en la web y devuelve ÚNICAMENTE un arreglo JSON (sin texto adicional ni markdown) con los {0} mejores resultados, cada uno con las claves \"title\", \"url\" y \"snippet\" (resumen en español de 1-2 frases con los datos útiles: qué es, ubicación, contacto si aparece).";

    private async Task<List<WebResult>> OpenAiAsync(string key, string model, string query, int limit, CancellationToken ct)
    {
        var c = _http.CreateClient("websearch");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        async Task<(HttpResponseMessage, string)> Call(string toolType, string m)
        {
            var body = new JsonObject
            {
                ["model"] = m,
                ["tools"] = new JsonArray(new JsonObject { ["type"] = toolType }),
                ["instructions"] = string.Format(JsonInstruction, limit),
                ["input"] = query,
            };
            var r = await c.PostAsync("https://api.openai.com/v1/responses", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
            return (r, await r.Content.ReadAsStringAsync(ct));
        }
        var (resp, json) = await Call("web_search", model);
        if (!resp.IsSuccessStatusCode) (resp, json) = await Call("web_search_preview", model);
        if (!resp.IsSuccessStatusCode && model != "gpt-4.1-mini") (resp, json) = await Call("web_search_preview", "gpt-4.1-mini");
        if (!resp.IsSuccessStatusCode)
        {
            string msg = json; try { msg = JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>() ?? json; } catch { }
            throw new InvalidOperationException($"OpenAI búsqueda {(int)resp.StatusCode}: {msg}");
        }
        var root = JsonNode.Parse(json)!;
        var text = new StringBuilder(); var cites = new List<WebResult>();
        foreach (var item in root["output"] as JsonArray ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var part in item["content"] as JsonArray ?? [])
            {
                if (part?["type"]?.GetValue<string>() != "output_text") continue;
                text.Append(part["text"]?.GetValue<string>());
                foreach (var a in part["annotations"] as JsonArray ?? [])
                    if (a?["type"]?.GetValue<string>() == "url_citation") cites.Add(new WebResult(a["title"]?.GetValue<string>() ?? "", a["url"]?.GetValue<string>() ?? "", ""));
            }
        }
        var parsed = ParseJsonResults(text.ToString());
        if (parsed.Count == 0) parsed = cites.GroupBy(x => x.Url).Select(g => g.First()).ToList();
        return parsed.Take(limit).ToList();
    }

    private async Task<List<WebResult>> AnthropicAsync(string key, string model, string query, int limit, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = key };
        var resp = await client.Messages.Create(new MessageCreateParams
        {
            Model = model, MaxTokens = 4000,
            System = string.Format(JsonInstruction, limit),
            Tools = [new ToolUnion(new WebSearchTool20260209 { MaxUses = 3 })],
            Messages = [new UserMessageParam { Content = query }],
        }, cancellationToken: ct);
        var text = string.Concat(resp.Content.Select(b => b.TryPickText(out TextBlock? t) ? t.Text : ""));
        return ParseJsonResults(text).Take(limit).ToList();
    }

    private static List<WebResult> ParseJsonResults(string text)
    {
        var m = JsonArrayRx().Match(text);
        if (!m.Success) return [];
        try
        {
            return (JsonNode.Parse(m.Value) as JsonArray ?? []).Select(r => new WebResult(r?["title"]?.GetValue<string>() ?? "", r?["url"]?.GetValue<string>() ?? "", r?["snippet"]?.GetValue<string>() ?? "")).Where(r => r.Url.Length > 0).ToList();
        }
        catch { return []; }
    }

    /// <summary>Descarga una página pública y devuelve su texto (sin etiquetas), recortado.</summary>
    public async Task<string> ReadAsync(string url, int maxChars = 6000, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("URL no válida");
        if (uri.IsLoopback || uri.Host.EndsWith(".local") || uri.Host.StartsWith("10.") || uri.Host.StartsWith("192.168.") || uri.Host.StartsWith("172.")) throw new ArgumentException("URL no permitida");
        var c = _http.CreateClient("websearch");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DanaCRM/1.0 (+https://github.com/dana-crm)");
        using var resp = await c.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} al leer {uri.Host}");
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!type.Contains("html") && !type.Contains("text") && !type.Contains("json")) throw new InvalidOperationException("La página no es texto (" + type + ")");
        var bytes = new byte[512 * 1024];
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var read = 0; int n;
        while (read < bytes.Length && (n = await stream.ReadAsync(bytes.AsMemory(read, bytes.Length - read), ct)) > 0) read += n;
        var html = Encoding.UTF8.GetString(bytes, 0, read);
        var title = TitleRx().Match(html).Groups[1].Value.Trim();
        html = ScriptRx().Replace(html, " ");
        var text = System.Net.WebUtility.HtmlDecode(StripTags(html));
        text = SpaceRx().Replace(text, " ").Trim();
        return (title.Length > 0 ? "Título: " + title + "\n\n" : "") + (text.Length > maxChars ? text[..maxChars] + "…" : text);
    }

    private static string StripTags(string s) => TagRx().Replace(s, " ");
    [GeneratedRegex(@"\[\s*\{[\s\S]*\}\s*\]")] private static partial Regex JsonArrayRx();
    [GeneratedRegex(@"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase)] private static partial Regex TitleRx();
    [GeneratedRegex(@"<(script|style|noscript|svg|head)[\s\S]*?</\1>", RegexOptions.IgnoreCase)] private static partial Regex ScriptRx();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRx();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex SpaceRx();
}
