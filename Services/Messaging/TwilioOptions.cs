namespace Dana.Services.Messaging;

public class TwilioOptions
{
    public const string Section = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }

    /// <summary>Número remitente para SMS, en formato E.164 (p. ej. +50688887777).</summary>
    public string? SmsFrom { get; set; }

    /// <summary>Remitente de WhatsApp (p. ej. +14155238886 en el sandbox, o el número aprobado por Meta).</summary>
    public string? WhatsAppFrom { get; set; }

    /// <summary>URL pública de Dana (https://crm.ejemplo.com). Se usa para los webhooks de estado y su validación de firma.</summary>
    public string? PublicBaseUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}

public static class MessageChannels
{
    public const string Sms = "sms";
    public const string WhatsApp = "whatsapp";
}
