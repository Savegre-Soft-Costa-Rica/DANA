using System.Diagnostics;
using Dana.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Dana.Services.Integrations;

/// <summary>Resultado de una prueba de conexión contra un servicio externo.</summary>
public record IntegrationTestResult(bool Ok, string Message);

/// <summary>
/// Punto único para las integraciones opcionales con los servicios de Savegre (Senda, Wapi, Wan).
/// Lee la configuración (cifrada) de <see cref="AppSettings.Integrations"/> y ofrece pruebas de conexión.
/// Los flujos funcionales (emitir facturas, enviar WhatsApp, lanzar llamadas) se añaden sobre esta base.
/// </summary>
public class IntegrationService
{
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly IDataProtector _protector;

    public IntegrationService(SettingsService settings, IHttpClientFactory http, IDataProtectionProvider dp)
    {
        _settings = settings;
        _http = http;
        _protector = dp.CreateProtector("Dana.Integrations");
    }

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _protector.Unprotect(protectedValue); } catch { return null; }
    }

    /// <summary>Configuración vigente (desde la caché de <see cref="SettingsService"/>).</summary>
    public IntegrationSettings Current => _settings.Current.Integrations;

    /// <summary>Modo de pruebas global: factura electrónica y pagos se simulan.</summary>
    public bool Sandbox => Current.Sandbox;

    public bool IsSendaConfigured => Enabled(Current.Senda.Enabled, Current.Senda.BaseUrl);
    public bool IsWapiConfigured => Enabled(Current.Wapi.Enabled, Current.Wapi.BaseUrl);
    public bool IsWanConfigured => Enabled(Current.Wan.Enabled, Current.Wan.BaseUrl);
    public bool IsTilopayConfigured => Enabled(Current.Tilopay.Enabled, Current.Tilopay.BaseUrl);

    public string? SendaApiKey => Unprotect(Current.Senda.ApiKeyProtected);
    public string? WapiApiKey => Unprotect(Current.Wapi.ApiKeyProtected);
    public string? WanToken => Unprotect(Current.Wan.TokenProtected);
    public string? TilopayUser => Unprotect(Current.Tilopay.ApiUserProtected);
    public string? TilopayPassword => Unprotect(Current.Tilopay.ApiPasswordProtected);
    public string? TilopayKey => Unprotect(Current.Tilopay.ApiKeyProtected);

    private static bool Enabled(bool enabled, string? url) => enabled && !string.IsNullOrWhiteSpace(url);

    /// <summary>
    /// Prueba de conectividad contra el endpoint de salud (/health, anónimo en los tres servicios).
    /// Recibe la URL explícita para poder probar lo que está en pantalla sin haber guardado aún.
    /// </summary>
    public async Task<IntegrationTestResult> PingHealthAsync(string? baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new(false, "Falta la URL del servicio.");
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/health", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new(false, "La URL no es válida (debe empezar por http:// o https://).");

        try
        {
            var client = _http.CreateClient("integrations");
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var sw = Stopwatch.StartNew();
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            return res.IsSuccessStatusCode
                ? new(true, $"✔ Conectado ({(int)res.StatusCode}) en {sw.ElapsedMilliseconds} ms.")
                : new(false, $"✖ El servicio respondió {(int)res.StatusCode} {res.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            return new(false, "✖ No se pudo conectar: " + ex.Message);
        }
    }
}
