using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dana.Services.Invoicing;

/// <summary>Un código CABYS (Catálogo de Bienes y Servicios de Hacienda, Costa Rica) con su descripción
/// y la tarifa de IVA asociada.</summary>
public record CabysItem(string Codigo, string Descripcion, decimal Impuesto, string? Categoria);

/// <summary>Consulta el catálogo CABYS directamente contra la API pública de Hacienda
/// (https://api.hacienda.go.cr/fe/cabys). Permite buscar por texto o por código. Los resultados se
/// cachean en memoria para no repetir llamadas. No requiere credenciales.</summary>
public class CabysService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<CabysService> _log;

    // Caché simple en memoria: la consulta (texto o código) → resultados. El catálogo CABYS no cambia a diario.
    private readonly ConcurrentDictionary<string, List<CabysItem>> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CabysService(IHttpClientFactory http, ILogger<CabysService> log)
    {
        _http = http;
        _log = log;
    }

    /// <summary>Busca códigos CABYS por descripción (mínimo 3 caracteres) o por código numérico.
    /// Devuelve hasta <paramref name="top"/> resultados; lista vacía si no hay coincidencias o falla la red.</summary>
    public async Task<List<CabysItem>> SearchAsync(string? term, int top = 25, CancellationToken ct = default)
    {
        term = term?.Trim() ?? "";
        if (term.Length < 3) return new();

        var byCode = term.All(char.IsDigit);
        var cacheKey = $"{(byCode ? "c" : "q")}:{term.ToLowerInvariant()}:{top}";
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var url = byCode
            ? $"https://api.hacienda.go.cr/fe/cabys?codigo={Uri.EscapeDataString(term)}"
            : $"https://api.hacienda.go.cr/fe/cabys?q={Uri.EscapeDataString(term)}&top={top}";

        try
        {
            var client = _http.CreateClient("integrations");
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return new();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var items = await ParseAsync(stream, ct);
            _cache[cacheKey] = items;
            return items;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo consultar CABYS en Hacienda ({Url})", url);
            return new();
        }
    }

    /// <summary>Devuelve el detalle de un único código CABYS (13 dígitos), o null si no existe.</summary>
    public async Task<CabysItem?> GetByCodeAsync(string? code, CancellationToken ct = default)
    {
        code = code?.Trim() ?? "";
        if (code.Length == 0) return null;
        var items = await SearchAsync(code, 1, ct);
        return items.FirstOrDefault(i => i.Codigo == code) ?? items.FirstOrDefault();
    }

    // La API devuelve dos formas: la búsqueda por texto => { total, cantidad, cabys: [ … ] };
    // la consulta por código => un arreglo plano [ … ]. Cada elemento trae codigo, descripcion,
    // impuesto y un arreglo "categorias".
    private static async Task<List<CabysItem>> ParseAsync(Stream stream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        JsonElement arr;
        if (root.ValueKind == JsonValueKind.Array) arr = root;
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("cabys", out var cabys)) arr = cabys;
        else return new();

        var result = new List<CabysItem>();
        foreach (var el in arr.EnumerateArray())
        {
            var codigo = el.TryGetProperty("codigo", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(codigo)) continue;
            var descripcion = el.TryGetProperty("descripcion", out var d) ? d.GetString() ?? "" : "";
            decimal impuesto = el.TryGetProperty("impuesto", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetDecimal() : 0m;

            string? categoria = null;
            if (el.TryGetProperty("categorias", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0)
            {
                // La primera categoría es la de mayor nivel: da contexto útil en la lista.
                categoria = cats[0].GetString();
            }

            result.Add(new CabysItem(codigo, descripcion, impuesto, categoria));
        }
        return result;
    }
}
