using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Messages;

namespace Dana.Services.Ai;

public record ChatTurn(string Role, string Text);
public record ToolCallInfo(string Name, string InputJson, string Output, bool IsError);
public record ChatReply(string Text, List<ToolCallInfo> ToolCalls, long InputTokens, long OutputTokens, string? StopReason);
public record ChatRequest(string ApiKey, string Model, string System, List<ChatTurn> History, string UserText, IReadOnlyList<ToolDef> Tools, int MaxToolCalls, string? Effort, Action<string>? OnDelta = null);

/// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
public delegate Task<ToolResult> ToolExecutor(string name, JsonElement input);

public interface IChatProvider
{
    string Id { get; }
    Task<ChatReply> CompleteAsync(ChatRequest req, ToolExecutor exec, Action<string>? progress, CancellationToken ct);
    Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct);
}

/// <summary>Claude mediante el SDK oficial de Anthropic. Bucle manual de herramientas: los bloques nativos (incluido el razonamiento) se conservan dentro del turno.</summary>
public class AnthropicProvider : IChatProvider
{
    public string Id => Models.AiProviders.Anthropic;

    public async Task<ChatReply> CompleteAsync(ChatRequest req, ToolExecutor exec, Action<string>? progress, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = req.ApiKey };
        var tools = req.Tools.Select(ToTool).ToList();
        var messages = new List<MessageParam>();
        foreach (var h in req.History)
        {
            if (string.IsNullOrWhiteSpace(h.Text)) continue;
            if (h.Role == "assistant") messages.Add(new AssistantMessageParam { Content = h.Text });
            else messages.Add(new UserMessageParam { Content = h.Text });
        }
        messages.Add(new UserMessageParam { Content = req.UserText });

        var calls = new List<ToolCallInfo>();
        long inTok = 0, outTok = 0;
        var text = new StringBuilder();
        string? stop = null;
        for (var round = 0; round <= req.MaxToolCalls; round++)
        {
            var p = new MessageCreateParams
            {
                Model = req.Model,
                MaxTokens = 8000,
                System = req.System,
                Messages = messages,
                Tools = tools.Count > 0 ? tools.Select(t => (ToolUnion)t).ToList() : null,
                OutputConfig = string.IsNullOrEmpty(req.Effort) ? null : new OutputConfig { Effort = req.Effort switch { "low" => Effort.Low, "medium" => Effort.Medium, "max" => Effort.Max, _ => Effort.High } },
            };

            // streaming: acumulamos los bloques (texto, razonamiento con firma, tool_use) y emitimos los deltas de texto
            var blocks = new List<StreamBlock>();
            await foreach (var ev in client.Messages.CreateStreaming(p, cancellationToken: ct))
            {
                if (ev.TryPickStart(out var start)) inTok += start.Message.Usage.InputTokens;
                else if (ev.TryPickContentBlockStart(out var cbs))
                {
                    var cb = cbs.ContentBlock; var b = new StreamBlock();
                    if (cb.TryPickText(out var tb)) { b.Type = "text"; b.Text.Append(tb.Text); }
                    else if (cb.TryPickToolUse(out var tub)) { b.Type = "tool_use"; b.Id = tub.ID; b.Name = tub.Name; }
                    else if (cb.TryPickThinking(out var thb)) { b.Type = "thinking"; b.Text.Append(thb.Thinking); b.Signature.Append(thb.Signature); }
                    else if (cb.TryPickRedactedThinking(out var rtb)) { b.Type = "redacted"; b.Text.Append(rtb.Data); }
                    else b.Type = "other";
                    blocks.Add(b);
                }
                else if (ev.TryPickContentBlockDelta(out var cbd))
                {
                    var idx = (int)cbd.Index; if (idx >= blocks.Count) continue;
                    var b = blocks[idx]; var d = cbd.Delta;
                    if (d.TryPickText(out var td)) { b.Text.Append(td.Text); req.OnDelta?.Invoke(td.Text); }
                    else if (d.TryPickInputJson(out var ij)) b.Json.Append(ij.PartialJson);
                    else if (d.TryPickThinking(out var thd)) b.Text.Append(thd.Thinking);
                    else if (d.TryPickSignature(out var sd)) b.Signature.Append(sd.Signature);
                }
                else if (ev.TryPickDelta(out var md)) { stop = md.Delta.StopReason?.ToString(); outTok += md.Usage.OutputTokens; }
            }

            List<ContentBlockParam> assistant = [];
            List<ContentBlockParam> results = [];
            foreach (var b in blocks)
            {
                switch (b.Type)
                {
                    case "text": assistant.Add(new TextBlockParam { Text = b.Text.ToString() }); text.Append(b.Text); break;
                    case "thinking": assistant.Add(new ThinkingBlockParam { Thinking = b.Text.ToString(), Signature = b.Signature.ToString() }); break;
                    case "redacted": assistant.Add(new RedactedThinkingBlockParam { Data = b.Text.ToString() }); break;
                    case "tool_use":
                    {
                        var json = b.Json.Length == 0 ? "{}" : b.Json.ToString();
                        Dictionary<string, JsonElement> dict;
                        try { dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new(); } catch { dict = new(); json = "{}"; }
                        assistant.Add(new ToolUseBlockParam { ID = b.Id!, Name = b.Name!, Input = dict });
                        var def = AgentTools.Find(b.Name!);
                        progress?.Invoke(def?.Title ?? b.Name!);
                        var input = JsonSerializer.SerializeToElement(dict);
                        var r = await exec(b.Name!, input);
                        calls.Add(new ToolCallInfo(b.Name!, json, r.Output, r.IsError));
                        results.Add(new ToolResultBlockParam { ToolUseID = b.Id!, Content = r.Output, IsError = r.IsError });
                        break;
                    }
                }
            }
            if (stop == "refusal")
            {
                text.Append("\n\n(El modelo declinó responder a esta solicitud.)");
                break;
            }
            if (results.Count == 0 || stop != "tool_use") break;
            messages.Add(new AssistantMessageParam { Content = assistant });
            messages.Add(new UserMessageParam { Content = results });
            if (round == req.MaxToolCalls) text.Append("\n\n(Se alcanzó el máximo de herramientas por respuesta.)");
        }
        return new(text.ToString().Trim(), calls, inTok, outTok, stop);
    }

    private sealed class StreamBlock { public string Type = ""; public string? Id; public string? Name; public StringBuilder Text = new(); public StringBuilder Json = new(); public StringBuilder Signature = new(); }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = apiKey };
        var page = await client.Models.List(cancellationToken: ct);
        return page.Items.Select(m => m.ID).OrderByDescending(x => x).ToList();
    }

    private static Tool ToTool(ToolDef d)
    {
        var schema = JsonSerializer.SerializeToElement(d.Schema);
        var props = new Dictionary<string, JsonElement>();
        if (schema.TryGetProperty("properties", out var ps) && ps.ValueKind == JsonValueKind.Object)
            foreach (var p in ps.EnumerateObject()) props[p.Name] = p.Value.Clone();
        var required = schema.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.Array ? rq.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        return new Tool { Name = d.Name, Description = d.Description, InputSchema = new() { Properties = props, Required = required } };
    }
}

/// <summary>ChatGPT mediante la API REST de OpenAI (chat completions con function calling).</summary>
public class OpenAiProvider : IChatProvider
{
    public string Id => Models.AiProviders.OpenAi;
    private readonly IHttpClientFactory _http;
    public OpenAiProvider(IHttpClientFactory http) { _http = http; }

    private HttpClient Client(string key)
    {
        var c = _http.CreateClient("openai");
        c.BaseAddress = new Uri("https://api.openai.com/v1/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return c;
    }

    public async Task<ChatReply> CompleteAsync(ChatRequest req, ToolExecutor exec, Action<string>? progress, CancellationToken ct)
    {
        var client = Client(req.ApiKey);
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = req.System } };
        foreach (var h in req.History)
            if (!string.IsNullOrWhiteSpace(h.Text)) messages.Add(new JsonObject { ["role"] = h.Role == "assistant" ? "assistant" : "user", ["content"] = h.Text });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = req.UserText });
        var tools = new JsonArray(req.Tools.Select(t => (JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(JsonSerializer.Serialize(t.Schema)) }
        }).ToArray());

        var calls = new List<ToolCallInfo>();
        long inTok = 0, outTok = 0;
        var text = new StringBuilder();
        string? finish = null;
        var body = new JsonObject { ["model"] = req.Model, ["messages"] = messages, ["stream"] = true, ["stream_options"] = new JsonObject { ["include_usage"] = true } };
        if (req.Tools.Count > 0) body["tools"] = tools;
        for (var round = 0; round <= req.MaxToolCalls; round++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            using var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                string msg = json;
                try { msg = JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>() ?? json; } catch { }
                throw new InvalidOperationException($"OpenAI {(int)resp.StatusCode}: {msg}");
            }
            var acc = new SseAccumulator();
            using (var stream = await resp.Content.ReadAsStreamAsync(ct))
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    if (!line.StartsWith("data:")) continue;
                    var data = line[5..].Trim();
                    if (data == "[DONE]") break;
                    acc.Feed(data, req.OnDelta);
                }
            }
            inTok += acc.InputTokens; outTok += acc.OutputTokens; finish = acc.FinishReason;
            if (acc.Content.Length > 0) text.Append(acc.Content);
            if (acc.ToolCalls.Count == 0) break;
            messages.Add(acc.ToAssistantMessage());
            foreach (var tc in acc.ToolCalls.Values.OrderBy(t => t.Index))
            {
                JsonElement input;
                try { input = JsonDocument.Parse(tc.Arguments.Length == 0 ? "{}" : tc.Arguments.ToString()).RootElement.Clone(); } catch { input = JsonDocument.Parse("{}").RootElement.Clone(); }
                progress?.Invoke(AgentTools.Find(tc.Name)?.Title ?? tc.Name);
                var r = await exec(tc.Name, input);
                calls.Add(new ToolCallInfo(tc.Name, input.GetRawText(), r.Output, r.IsError));
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = tc.Id, ["content"] = r.Output });
            }
            if (round == req.MaxToolCalls) text.Append("\n\n(Se alcanzó el máximo de herramientas por respuesta.)");
        }
        return new(text.ToString().Trim(), calls, inTok, outTok, finish);
    }

    /// <summary>Acumula los fragmentos SSE de chat completions (texto, tool_calls por índice, uso y finish_reason).</summary>
    public sealed class SseAccumulator
    {
        public sealed class Call { public int Index; public string Id = ""; public string Name = ""; public StringBuilder Arguments = new(); }
        public StringBuilder Content { get; } = new();
        public Dictionary<int, Call> ToolCalls { get; } = new();
        public long InputTokens, OutputTokens;
        public string? FinishReason;

        public void Feed(string data, Action<string>? onDelta)
        {
            JsonNode? root; try { root = JsonNode.Parse(data); } catch { return; }
            if (root?["usage"] is JsonObject u) { InputTokens = u["prompt_tokens"]?.GetValue<long>() ?? InputTokens; OutputTokens = u["completion_tokens"]?.GetValue<long>() ?? OutputTokens; }
            var choice = root?["choices"] is JsonArray ch && ch.Count > 0 ? ch[0] : null;
            if (choice is null) return;
            if (choice["finish_reason"]?.GetValue<string>() is { } fr) FinishReason = fr;
            var delta = choice["delta"];
            if (delta is null) return;
            if (delta["content"]?.GetValue<string>() is { Length: > 0 } c) { Content.Append(c); onDelta?.Invoke(c); }
            foreach (var tc in delta["tool_calls"] as JsonArray ?? [])
            {
                var idx = tc?["index"]?.GetValue<int>() ?? 0;
                if (!ToolCalls.TryGetValue(idx, out var call)) ToolCalls[idx] = call = new Call { Index = idx };
                if (tc?["id"]?.GetValue<string>() is { Length: > 0 } id) call.Id = id;
                if (tc?["function"]?["name"]?.GetValue<string>() is { Length: > 0 } n) call.Name = n;
                if (tc?["function"]?["arguments"]?.GetValue<string>() is { } a) call.Arguments.Append(a);
            }
        }

        public JsonObject ToAssistantMessage()
        {
            var m = new JsonObject { ["role"] = "assistant", ["content"] = Content.Length == 0 ? null : Content.ToString() };
            if (ToolCalls.Count > 0) m["tool_calls"] = new JsonArray(ToolCalls.Values.OrderBy(t => t.Index).Select(t => (JsonNode)new JsonObject { ["id"] = t.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["arguments"] = t.Arguments.ToString() } }).ToArray());
            return m;
        }
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct)
    {
        using var resp = await Client(apiKey).GetAsync("models", ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI {(int)resp.StatusCode}: {json}");
        var data = JsonNode.Parse(json)?["data"] as JsonArray;
        return (data ?? []).Select(d => d?["id"]?.GetValue<string>() ?? "").Where(id => id.StartsWith("gpt") || id.StartsWith("o")).Where(id => !id.Contains("realtime") && !id.Contains("audio") && !id.Contains("tts") && !id.Contains("transcribe") && !id.Contains("image") && !id.Contains("embedding")).OrderByDescending(x => x).ToList();
    }
}
