namespace Dana.Models;

/// <summary>
/// Conexiones opcionales a los servicios externos de Savegre. Cada bloque se activa por separado;
/// sin URL (o desactivado) la integración no hace nada. Las claves se guardan cifradas (Data Protection).
/// </summary>
public class IntegrationSettings
{
    /// <summary>Modo de pruebas: la facturación electrónica y los enlaces de pago se simulan
    /// (no se envía nada a Hacienda ni a Tilopay). Ideal para probar el flujo completo.</summary>
    public bool Sandbox { get; set; } = true;

    public SendaSettings Senda { get; set; } = new();
    public WapiSettings Wapi { get; set; } = new();
    public WanSettings Wan { get; set; } = new();
    public TilopaySettings Tilopay { get; set; } = new();
}

/// <summary>Tilopay: pasarela de pagos (Costa Rica). Enlaces de pago para cotizaciones/facturas.</summary>
public class TilopaySettings
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; } = "https://app.tilopay.com/api/v1";
    /// <summary>Credenciales de la API de Tilopay (apiuser / password / key), cifradas.</summary>
    public string? ApiUserProtected { get; set; }
    public string? ApiPasswordProtected { get; set; }
    public string? ApiKeyProtected { get; set; }
}

/// <summary>Senda / Factu: API de facturación electrónica del Ministerio de Hacienda (Costa Rica).</summary>
public class SendaSettings
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Clave de API (o token de servicio) con la que Dana se autentica ante Senda.</summary>
    public string? ApiKeyProtected { get; set; }

    // --- Datos fiscales del emisor (Hacienda exige enviarlos en cada comprobante) ---
    /// <summary>Cédula del emisor ya registrado en Senda (con su certificado .p12 y sesión de Hacienda).</summary>
    public string? CedulaEmisor { get; set; }
    /// <summary>Código de actividad económica del emisor ante Hacienda (p. ej. 8549.0).</summary>
    public string? CodigoActividad { get; set; }
    /// <summary>Nombre/razón social del emisor. Si se deja vacío se usa el nombre de la empresa (AppSettings).</summary>
    public string? EmisorNombre { get; set; }
    /// <summary>Tipo de identificación del emisor: 01 física, 02 jurídica, 03 DIMEX, 04 NITE.</summary>
    public string EmisorTipoId { get; set; } = "02";
    /// <summary>Correo del emisor (obligatorio para Hacienda).</summary>
    public string? EmisorEmail { get; set; }
    /// <summary>Ubicación del emisor (códigos de Hacienda): provincia (1 díg.), cantón y distrito (2 díg.).</summary>
    public string? Provincia { get; set; }
    public string? Canton { get; set; }
    public string? Distrito { get; set; }
    public string? OtrasSenas { get; set; }
}

/// <summary>Wapi (Confluence): middleware multi-tenant para la WhatsApp Cloud API.</summary>
public class WapiSettings
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Clave de API del tenant (header X-Api-Key).</summary>
    public string? ApiKeyProtected { get; set; }
    /// <summary>Id de la cuenta de WhatsApp dentro de Wapi (accountId) desde la que se envía.</summary>
    public string? AccountId { get; set; }
}

/// <summary>Wan (Mapache): bot telefónico con IA (motor SIP + ElevenLabs).</summary>
public class WanSettings
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Token de servicio (JWT) con el que Dana se autentica ante Wan.</summary>
    public string? TokenProtected { get; set; }
}
