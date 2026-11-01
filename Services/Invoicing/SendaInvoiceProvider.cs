using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dana.Models;
using Dana.Services;
using Dana.Services.Integrations;

namespace Dana.Services.Invoicing;

/// <summary>
/// Adaptador a Senda / Factu (facturación electrónica de Hacienda, Costa Rica). Emite comprobantes
/// (<c>POST /comprobante/factura/enviar</c>) y consulta su estado (<c>GET /comprobante/{clave}</c>) con la
/// clave de API configurada en Configuración → Integraciones. El emisor, su certificado .p12 y la sesión
/// con Hacienda se gestionan dentro de Senda; Dana solo envía los datos del comprobante.
/// </summary>
public class SendaInvoiceProvider : IElectronicInvoiceProvider
{
    private const string NotConfigured = "No hay proveedor de facturación electrónica configurado.";

    private readonly IntegrationService _integrations;
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<SendaInvoiceProvider> _logger;

    public SendaInvoiceProvider(IntegrationService integrations, SettingsService settings, IHttpClientFactory http, ILogger<SendaInvoiceProvider> logger)
    {
        _integrations = integrations;
        _settings = settings;
        _http = http;
        _logger = logger;
    }

    public string Name => "Senda";
    public bool IsConfigured => _integrations.IsSendaConfigured;

    public async Task<InvoiceSubmitResult> SubmitAsync(ElectronicInvoice invoice, CancellationToken ct = default)
    {
        var cfg = _integrations.Current.Senda;
        var ready = Ready(cfg, out var error);
        if (!ready) return new(false, null, null, error);

        object payload;
        try
        {
            payload = BuildPayload(invoice, cfg, _settings.Current);
        }
        catch (Exception ex)
        {
            return new(false, null, null, "No se pudieron preparar los datos de la factura: " + ex.Message);
        }

        try
        {
            using var req = BuildRequest(HttpMethod.Post, cfg.BaseUrl!, "/comprobante/factura/enviar", _integrations.SendaApiKey!, payload);
            using var res = await Client().SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return new(false, null, null, DescribeError(res.StatusCode, body));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var clave = Str(root, "clave");
            var consecutivo = Str(root, "consecutivo");
            if (string.IsNullOrEmpty(clave))
                return new(false, null, null, "Senda aceptó la solicitud pero no devolvió la clave del comprobante.");
            return new(true, clave, consecutivo ?? clave, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enviando la factura {Id} a Senda", invoice.Id);
            return new(false, null, null, "No se pudo enviar a Senda: " + ex.Message);
        }
    }

    public async Task<InvoiceStatusResult> GetStatusAsync(ElectronicInvoice invoice, CancellationToken ct = default)
    {
        var cfg = _integrations.Current.Senda;
        if (!_integrations.IsSendaConfigured || string.IsNullOrWhiteSpace(_integrations.SendaApiKey))
            return new(null, NotConfigured);
        if (string.IsNullOrWhiteSpace(invoice.Clave))
            return new(null, "La factura no tiene clave asignada; vuelva a enviarla.");

        try
        {
            using var req = BuildRequest(HttpMethod.Get, cfg.BaseUrl!, "/comprobante/" + invoice.Clave, _integrations.SendaApiKey!, null);
            using var res = await Client().SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return new(null, DescribeError(res.StatusCode, body));

            using var doc = JsonDocument.Parse(body);
            var estado = Str(doc.RootElement, "estado");
            return new(MapEstado(estado), estado == "rechazado" ? "Hacienda rechazó el comprobante." : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando el estado de la factura {Id} en Senda", invoice.Id);
            return new(null, "No se pudo consultar el estado: " + ex.Message);
        }
    }

    // --- Validación de configuración ---

    private bool Ready(SendaSettings cfg, out string error)
    {
        if (!_integrations.IsSendaConfigured) { error = NotConfigured; return false; }
        if (string.IsNullOrWhiteSpace(_integrations.SendaApiKey)) { error = "Falta la clave de API de Senda (Configuración → Integraciones)."; return false; }
        if (string.IsNullOrWhiteSpace(cfg.CedulaEmisor) || string.IsNullOrWhiteSpace(cfg.CodigoActividad))
        { error = "Configure la cédula y el código de actividad del emisor en Integraciones."; return false; }
        if (string.IsNullOrWhiteSpace(cfg.EmisorEmail) || string.IsNullOrWhiteSpace(cfg.Provincia) ||
            string.IsNullOrWhiteSpace(cfg.Canton) || string.IsNullOrWhiteSpace(cfg.Distrito) || string.IsNullOrWhiteSpace(cfg.OtrasSenas))
        { error = "Complete los datos fiscales del emisor (correo y ubicación) en Integraciones."; return false; }
        error = "";
        return true;
    }

    // --- Mapeo de la factura al DTO de Senda ---

    private static object BuildPayload(ElectronicInvoice inv, SendaSettings cfg, AppSettings app)
    {
        var lineas = inv.Lines.Select(l =>
        {
            var linea = new Dictionary<string, object?>
            {
                ["codigoCabys"] = l.Cabys,
                ["cantidad"] = l.Quantity,
                ["unidadMedida"] = string.IsNullOrWhiteSpace(l.Unit) ? "Unid" : l.Unit,
                ["detalle"] = l.Description,
                ["precioUnitario"] = l.UnitPrice,
            };
            if (l.DiscountPercent > 0)
                linea["descuentos"] = new[] { new { monto = Math.Round(l.Quantity * l.UnitPrice * l.DiscountPercent / 100m, 2), naturaleza = "Descuento comercial" } };
            if (l.TaxRate > 0)
                linea["impuestos"] = new[] { new { codigo = "01", codigoTarifa = CodigoTarifa(l.TaxRate), tarifa = l.TaxRate } };
            return (object)linea;
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["cedulaEmisor"] = Digits(cfg.CedulaEmisor),
            ["codigoActividadEmisor"] = cfg.CodigoActividad,
            ["condicionVenta"] = "01", // contado
            ["emisor"] = new
            {
                nombre = string.IsNullOrWhiteSpace(cfg.EmisorNombre) ? app.CompanyName : cfg.EmisorNombre,
                identificacion = new { tipo = string.IsNullOrWhiteSpace(cfg.EmisorTipoId) ? "02" : cfg.EmisorTipoId, numero = Digits(cfg.CedulaEmisor) },
                ubicacion = new { provincia = cfg.Provincia, canton = cfg.Canton, distrito = cfg.Distrito, otrasSenas = cfg.OtrasSenas },
                correoElectronico = cfg.EmisorEmail,
            },
            ["lineas"] = lineas,
        };

        if (!string.IsNullOrWhiteSpace(inv.ReceiverName))
        {
            var receptor = new Dictionary<string, object?>
            {
                ["nombre"] = inv.ReceiverName,
                ["identificacion"] = new { tipo = InferTipo(inv.ReceiverId), numero = Digits(inv.ReceiverId) },
            };
            if (!string.IsNullOrWhiteSpace(inv.ReceiverEmail)) receptor["correoElectronico"] = inv.ReceiverEmail;
            payload["receptor"] = receptor;
        }

        var currency = (inv.Currency ?? "CRC").ToUpperInvariant();
        if (currency != "CRC")
        {
            var tc = TipoCambio(app, currency);
            payload["moneda"] = tc is null ? new { codigo = currency } : (object)new { codigo = currency, tipoCambio = tc.Value };
        }

        return payload;
    }

    /// <summary>Código de tarifa de IVA de Hacienda según el porcentaje. Se envía tal cual al XML (no se deriva en Senda).</summary>
    private static string CodigoTarifa(decimal tarifa) => tarifa switch
    {
        0m => "01",
        0.5m => "05",
        1m => "02",
        2m => "03",
        4m => "04",
        8m => "07",
        13m => "08",
        _ => "08",
    };

    /// <summary>Tipo de identificación del receptor inferido por longitud: 9→física, 10→jurídica, 11-12→DIMEX.</summary>
    private static string InferTipo(string? id) => Digits(id).Length switch
    {
        9 => "01",
        10 => "02",
        11 or 12 => "03",
        _ => "01",
    };

    /// <summary>Colones por 1 unidad de la moneda del comprobante, a partir de los tipos de cambio de AppSettings.</summary>
    private static decimal? TipoCambio(AppSettings app, string currency)
    {
        if (!app.Rates.TryGetValue("CRC", out var crc) || !app.Rates.TryGetValue(currency, out var cur) || cur == 0) return null;
        return Math.Round(crc / cur, 5);
    }

    private static string Digits(string? s) => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\D", "");

    private static string MapEstado(string? estado) => estado switch
    {
        "aceptado" => "accepted",
        "rechazado" => "rejected",
        _ => "submitted", // recibido / procesando / desconocido: sigue en proceso
    };

    // --- HTTP ---

    private HttpClient Client() => _http.CreateClient("integrations");

    private static HttpRequestMessage BuildRequest(HttpMethod method, string baseUrl, string path, string token, object? jsonBody)
    {
        var req = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");
        return req;
    }

    private static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static string DescribeError(HttpStatusCode code, string body)
    {
        var detail = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (Str(root, "error") is { Length: > 0 } e) detail = e;
            else if (Str(root, "mensaje") is { Length: > 0 } m) detail = m;
            if (root.TryGetProperty("errores", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                detail += " · " + string.Join("; ", errs.EnumerateArray().Select(x => Str(x, "mensaje") ?? x.ToString()));
        }
        catch { /* el cuerpo no era JSON: se usa tal cual */ }

        var hint = code switch
        {
            HttpStatusCode.Unauthorized => " (clave de API inválida o la sesión del emisor con Hacienda venció: inicie sesión en Senda).",
            HttpStatusCode.BadGateway => " (fallo de comunicación de Senda con Hacienda; reintente).",
            _ => "",
        };
        if (detail.Length > 400) detail = detail[..400] + "…";
        return $"Senda respondió {(int)code}: {detail}{hint}";
    }
}
