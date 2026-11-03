using Dana.Data;
using Dana.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Dana.Services;

public class EmailOptions
{
    public const string Section = "Email:Smtp";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string? FromName { get; set; }
    public bool UseStartTls { get; set; } = true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

public record EmailContext(string? Name, string? LastName, string? Company, string? Email, string? Title, string User);

public class EmailService
{
    private readonly EmailOptions _options;
    private readonly IRepository<EmailTemplate> _templates;
    private readonly IRepository<User> _users;
    private readonly SecretProtector _secrets;
    private readonly ActivityService _activities;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailOptions options, IRepository<EmailTemplate> templates, IRepository<User> users, SecretProtector secrets, ActivityService activities, ILogger<EmailService> logger)
    {
        _options = options;
        _templates = templates;
        _users = users;
        _secrets = secrets;
        _activities = activities;
        _logger = logger;
    }

    /// <summary>SMTP global (empresa). Lo usan los envíos de sistema (recordatorios, agentes, automatizaciones).</summary>
    public bool IsConfigured => _options.IsConfigured;
    public string? From => _options.From;

    /// <summary>Datos efectivos de envío ya resueltos (personal o global).</summary>
    private record Smtp(string Host, int Port, string? User, string? Password, string FromEmail, string FromName, bool UseStartTls);

    /// <summary>¿El empleado tiene su propio correo conectado para enviar?</summary>
    public async Task<bool> CanUserSendAsync(string? userName)
        => (await GetUserSettingsAsync(userName))?.IsConfigured == true;

    public async Task<UserEmailSettings?> GetUserSettingsAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;
        var u = (await _users.FindAsync(x => x.Name == userName)).FirstOrDefault();
        return u?.EmailSettings;
    }

    private Smtp? Resolve(UserEmailSettings? personal, bool allowGlobalFallback)
    {
        if (personal is { IsConfigured: true })
        {
            var pwd = _secrets.Unprotect(personal.PasswordEnc);
            if (!string.IsNullOrEmpty(pwd))
                return new Smtp(personal.Host!, personal.Port, personal.Username, pwd, personal.FromEmail!, personal.FromName ?? personal.FromEmail!, personal.UseStartTls);
        }
        if (allowGlobalFallback && _options.IsConfigured)
            return new Smtp(_options.Host!, _options.Port, _options.User, _options.Password, _options.From!, _options.FromName ?? _options.From!, _options.UseStartTls);
        return null;
    }

    /// <summary>Prueba conexión + autenticación SMTP sin enviar nada.</summary>
    public async Task<(bool Ok, string? Error)> TestSmtpAsync(string host, int port, string? user, string? password, bool useStartTls)
    {
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, useStartTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(user)) await client.AuthenticateAsync(user, password);
            await client.DisconnectAsync(true);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public Task<List<EmailTemplate>> GetTemplatesAsync() => _templates.GetAllAsync();
    public Task<EmailTemplate?> GetTemplateAsync(string? id) => _templates.GetByIdAsync(id);
    public async Task SaveTemplateAsync(EmailTemplate t) { if (t.Id is null) await _templates.InsertAsync(t); else await _templates.UpdateAsync(t); }
    public Task DeleteTemplateAsync(string id) => _templates.DeleteAsync(id);

    public static string Render(string text, EmailContext ctx) => text
        .Replace("{{nombre}}", ctx.Name ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{apellido}}", ctx.LastName ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{compañía}}", ctx.Company ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{compania}}", ctx.Company ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{correo}}", ctx.Email ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{titulo}}", ctx.Title ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{usuario}}", ctx.User, StringComparison.OrdinalIgnoreCase);

    /// <summary>Envía por SMTP (si está configurado) y registra el correo en la línea de tiempo del elemento.</summary>
    public record EmailAttachment(string Name, byte[] Bytes, string ContentType);

    public Task<(bool Sent, string? Error)> SendAsync(string to, string subject, string body, string entityType, string entityId, string entityTitle, string author) =>
        SendAsync(to, subject, body, entityType, entityId, entityTitle, author, null);

    public Task<(bool Sent, string? Error)> SendAsync(string to, string subject, string body, string entityType, string entityId, string entityTitle, string author, IReadOnlyList<EmailAttachment>? attachments) =>
        SendAsync(to, subject, body, entityType, entityId, entityTitle, author, attachments, allowGlobalFallback: true);

    /// <summary>Envía usando el SMTP personal del remitente; si no lo tiene y <paramref name="allowGlobalFallback"/>, usa el SMTP global. Siempre registra en la línea de tiempo.</summary>
    public async Task<(bool Sent, string? Error)> SendAsync(string to, string subject, string body, string entityType, string entityId, string entityTitle, string author, IReadOnlyList<EmailAttachment>? attachments, bool allowGlobalFallback)
    {
        string? error = null;
        var sent = false;
        var smtp = Resolve(await GetUserSettingsAsync(author), allowGlobalFallback);
        if (smtp is not null)
        {
            try
            {
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(smtp.FromName, smtp.FromEmail));
                msg.To.Add(MailboxAddress.Parse(to));
                msg.Subject = subject;
                if (attachments is { Count: > 0 })
                {
                    var mp = new Multipart("mixed") { new TextPart("plain") { Text = body } };
                    foreach (var a in attachments) mp.Add(new MimePart(a.ContentType) { Content = new MimeContent(new MemoryStream(a.Bytes)), ContentDisposition = new ContentDisposition(ContentDisposition.Attachment), ContentTransferEncoding = ContentEncoding.Base64, FileName = a.Name });
                    msg.Body = mp;
                }
                else msg.Body = new TextPart("plain") { Text = body };
                using var client = new SmtpClient();
                await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseStartTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.Auto);
                if (!string.IsNullOrEmpty(smtp.User)) await client.AuthenticateAsync(smtp.User, smtp.Password);
                await client.SendAsync(msg);
                await client.DisconnectAsync(true);
                sent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a {To}", to);
                error = ex.Message;
            }
        }

        await _activities.LogAsync(entityType, entityId, entityTitle, ActivityKinds.Email,
            $"✉ {(sent ? "Envió" : "Registró")} correo a {to} · Asunto: {subject}{(attachments is { Count: > 0 } ? " · Adjuntos: " + string.Join(", ", attachments.Select(a => a.Name)) : "")}\n\n{body}", author);
        return (sent, error);
    }
}
