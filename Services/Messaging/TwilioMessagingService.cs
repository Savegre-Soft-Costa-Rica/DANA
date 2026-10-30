using System.Text.RegularExpressions;
using Dana.Models;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Security;
using Twilio.Types;

namespace Dana.Services.Messaging;

/// <summary>Envío de SMS y WhatsApp con Twilio, y recepción de sus webhooks de estado.</summary>
public class TwilioMessagingService
{
    public const string StatusPath = "/api/webhooks/twilio/status";

    private static readonly Regex E164 = new(@"^\+\d{8,15}$", RegexOptions.Compiled);

    private readonly TwilioOptions _options;
    private readonly ActivityService _activities;
    private readonly ILogger<TwilioMessagingService> _logger;

    public TwilioMessagingService(TwilioOptions options, ActivityService activities, ILogger<TwilioMessagingService> logger)
    {
        _options = options;
        _activities = activities;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Envía un SMS o un WhatsApp y lo registra en la línea de tiempo del elemento.</summary>
    public async Task<(bool Sent, string? Error)> SendAsync(string channel, string to, string body, string entityType, string entityId, string entityTitle, string author)
    {
        if (!_options.IsConfigured) return (false, "Twilio no está configurado.");

        var isWhatsApp = channel == MessageChannels.WhatsApp;
        var from = isWhatsApp ? _options.WhatsAppFrom : _options.SmsFrom;
        if (string.IsNullOrWhiteSpace(from)) return (false, $"Falta el remitente de {(isWhatsApp ? "WhatsApp" : "SMS")}.");

        var phone = NormalizePhone(to);
        if (phone is null) return (false, "El número debe estar en formato internacional, por ejemplo +50688887777.");
        if (string.IsNullOrWhiteSpace(body)) return (false, "El mensaje está vacío.");

        try
        {
            var client = new TwilioRestClient(_options.AccountSid!, _options.AuthToken!);
            var message = await MessageResource.CreateAsync(new CreateMessageOptions(Address(phone, isWhatsApp))
            {
                From = Address(NormalizePhone(from) ?? from, isWhatsApp),
                Body = body,
                StatusCallback = CallbackUri(entityType, entityId, entityTitle),
            }, client);

            var label = isWhatsApp ? "WhatsApp" : "SMS";
            await _activities.LogAsync(entityType, entityId, entityTitle, isWhatsApp ? ActivityKinds.WhatsApp : ActivityKinds.Sms, $"{label} enviado a {phone}: {body}", author);
            _logger.LogInformation("Twilio {Channel} {Sid} enviado a {Phone}", channel, message.Sid, phone);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al enviar {Channel} con Twilio", channel);
            return (false, ex.Message);
        }
    }

    /// <summary>Webhook de estado de Twilio. Valida la firma y registra los mensajes no entregados.</summary>
    public async Task<IResult> HandleStatusCallbackAsync(HttpContext ctx)
    {
        if (!_options.IsConfigured) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        var parameters = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var signature = ctx.Request.Headers["X-Twilio-Signature"].ToString();

        if (!new RequestValidator(_options.AuthToken!).Validate(CurrentUrl(ctx), parameters, signature))
        {
            _logger.LogWarning("Webhook de Twilio rechazado: firma no válida.");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var status = parameters.GetValueOrDefault("MessageStatus");
        if (status is "failed" or "undelivered")
        {
            var query = ctx.Request.Query;
            var entityType = query["entityType"].ToString();
            var entityId = query["entityId"].ToString();
            if (!string.IsNullOrEmpty(entityType) && !string.IsNullOrEmpty(entityId))
            {
                var to = parameters.GetValueOrDefault("To") ?? "";
                var code = parameters.GetValueOrDefault("ErrorCode");
                await _activities.LogAsync(entityType, entityId, query["title"].ToString(), ActivityKinds.System,
                    $"Mensaje no entregado a {to} (estado: {status}{(string.IsNullOrEmpty(code) ? "" : $", código {code}")}).");
            }
        }

        return Results.Ok();
    }

    /// <summary>Convierte un número a E.164 (quita espacios, guiones y paréntesis). Devuelve null si no es válido.</summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = Regex.Replace(input, @"[\s\-()]", "");
        return E164.IsMatch(cleaned) ? cleaned : null;
    }

    private static PhoneNumber Address(string number, bool whatsApp) => new(whatsApp ? "whatsapp:" + number : number);

    private Uri? CallbackUri(string entityType, string entityId, string entityTitle)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl)) return null;
        var query = $"entityType={Uri.EscapeDataString(entityType)}&entityId={Uri.EscapeDataString(entityId)}&title={Uri.EscapeDataString(entityTitle)}";
        return new Uri($"{_options.PublicBaseUrl.TrimEnd('/')}{StatusPath}?{query}");
    }

    // Twilio firma la URL exacta que recibió; con un proxy inverso se usa la URL pública configurada.
    private string CurrentUrl(HttpContext ctx) =>
        string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.Path}{ctx.Request.QueryString}"
            : $"{_options.PublicBaseUrl.TrimEnd('/')}{ctx.Request.Path}{ctx.Request.QueryString}";
}
