using System.Globalization;
using System.Text.Json;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public record InboundResult(bool Ok, int Status, string? Error, Lead? Lead);

/// <summary>Recibe solicitudes externas (API pública / webhooks) y las convierte en prospectos según el canal.</summary>
public class InboundLeadService
{
    private readonly IRepository<ApiChannel> _channels;
    private readonly LeadService _leads;
    private readonly UserService _users;
    private readonly NotificationService _notifications;
    private readonly ILogger<InboundLeadService> _logger;

    public InboundLeadService(IRepository<ApiChannel> channels, LeadService leads, UserService users, NotificationService notifications, ILogger<InboundLeadService> logger)
    {
        _channels = channels;
        _leads = leads;
        _users = users;
        _notifications = notifications;
        _logger = logger;
    }

    public Task<List<ApiChannel>> GetAllAsync() => _channels.GetAllAsync();
    public Task<ApiChannel?> GetAsync(string? id) => _channels.GetByIdAsync(id);
    public async Task SaveAsync(ApiChannel c) { if (c.Id is null) await _channels.InsertAsync(c); else await _channels.UpdateAsync(c); }
    public Task DeleteAsync(string id) => _channels.PurgeAsync(id);
    public Task<ApiChannel?> FindByKeyAsync(string key) => _channels.Collection.Find(c => c.Key == key && c.DeletedAt == null).FirstOrDefaultAsync();

    /// <summary>Convierte un JSON (objeto plano o anidado un nivel) en un diccionario campo→texto.</summary>
    public static Dictionary<string, string> Flatten(JsonElement json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement el, string prefix)
        {
            if (el.ValueKind != JsonValueKind.Object) return;
            foreach (var p in el.EnumerateObject())
            {
                var key = prefix == "" ? p.Name : prefix + "." + p.Name;
                switch (p.Value.ValueKind)
                {
                    case JsonValueKind.String: dict[key] = p.Value.GetString() ?? ""; if (prefix != "") dict.TryAdd(p.Name, p.Value.GetString() ?? ""); break;
                    case JsonValueKind.Number: dict[key] = p.Value.GetRawText(); if (prefix != "") dict.TryAdd(p.Name, p.Value.GetRawText()); break;
                    case JsonValueKind.True or JsonValueKind.False: dict[key] = p.Value.GetRawText(); break;
                    case JsonValueKind.Object: if (prefix == "") Walk(p.Value, p.Name); break;
                }
            }
        }
        Walk(json, "");
        return dict;
    }

    public async Task<InboundResult> ProcessAsync(string? key, Dictionary<string, string> payload, string? origin, string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(key)) return new(false, 401, "Falta la clave de API (cabecera X-Api-Key, parámetro ?key= o campo apiKey).", null);
        var channel = await FindByKeyAsync(key.Trim());
        if (channel is null) return new(false, 401, "Clave de API no válida.", null);
        if (!channel.IsEnabled) return new(false, 403, "El canal está deshabilitado.", null);
        if (channel.AllowedOrigins.Count > 0)
        {
            var o = (origin ?? "").TrimEnd('/');
            if (!channel.AllowedOrigins.Any(a => string.Equals(a.TrimEnd('/'), o, StringComparison.OrdinalIgnoreCase)))
            {
                channel.LastError = $"Origen no permitido: {origin}"; await _channels.UpdateAsync(channel);
                return new(false, 403, "Origen no permitido para este canal.", null);
            }
        }

        string? Pick(string field)
        {
            if (!channel.FieldMap.TryGetValue(field, out var names)) return null;
            foreach (var n in names.Split(',').Select(x => x.Trim()).Where(x => x != ""))
                if (payload.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }

        var name = Pick("name"); var lastName = Pick("lastName");
        if (lastName is null && name is not null && name.Contains(' ')) { var parts = name.Split(' ', 2); name = parts[0]; lastName = parts[1]; }
        var email = Pick("email"); var phone = Pick("phone"); var company = Pick("company"); var message = Pick("message");
        var title = Pick("title"); var source = Pick("source");
        decimal.TryParse(Pick("opportunity") ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out var opportunity);

        if (name is null && email is null && phone is null && title is null)
        {
            channel.LastError = "Solicitud sin nombre, correo, teléfono ni título"; channel.LastPayload = Truncate(rawPayload); await _channels.UpdateAsync(channel);
            return new(false, 400, "Se requiere al menos nombre, correo, teléfono o título.", null);
        }

        // Campos no mapeados → se añaden a los comentarios para no perder información
        var mapped = channel.FieldMap.Values.SelectMany(v => v.Split(',')).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = payload.Where(kv => !mapped.Contains(kv.Key) && !kv.Key.Contains('.') && !string.Equals(kv.Key, "apiKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{kv.Key}: {kv.Value}").ToList();
        var comments = string.Join("\n", new[] { message }.Concat(extras).Where(s => !string.IsNullOrWhiteSpace(s)));

        var assignTo = channel.AssignTo;
        if (assignTo == "@roundrobin")
        {
            var users = (await _users.GetAllAsync()).Where(u => u.IsActive).OrderBy(u => u.Name).Select(u => u.Name).ToList();
            assignTo = users.Count == 0 ? null : users[channel.ReceivedCount % users.Count];
        }

        var lead = new Lead
        {
            Title = Render(string.IsNullOrWhiteSpace(title) ? channel.TitleTemplate : title, name, lastName, company, email),
            Name = name, LastName = lastName, CompanyName = company, Phone = phone, Email = email,
            Source = string.IsNullOrWhiteSpace(source) ? channel.Source : source,
            Status = string.IsNullOrEmpty(channel.Status) ? LeadStatuses.New : channel.Status,
            Opportunity = opportunity, Comments = string.IsNullOrWhiteSpace(comments) ? null : comments,
            AssignedTo = string.IsNullOrWhiteSpace(assignTo) ? null : assignTo,
        };
        if (string.IsNullOrWhiteSpace(lead.Title)) lead.Title = $"Solicitud web {DateTime.Now:dd/MM HH:mm}";

        await _leads.SaveAsync(lead, $"API · {channel.Name}");

        var recipients = channel.NotifyUsers.ToList();
        if (channel.NotifyAssignee && lead.AssignedTo is not null) recipients.Add(lead.AssignedTo);
        foreach (var r in recipients.Distinct())
            await _notifications.NotifyAsync(r, NotificationKinds.System, $"nuevo prospecto desde {channel.Name}: {lead.Title}", $"leads/{lead.Id}", "API");

        channel.ReceivedCount++; channel.LastReceivedAt = DateTime.UtcNow; channel.LastPayload = Truncate(rawPayload); channel.LastError = null;
        await _channels.UpdateAsync(channel);
        _logger.LogInformation("Prospecto {Id} recibido por el canal {Channel}", lead.Id, channel.Name);
        return new(true, 201, null, lead);
    }

    private static string Render(string template, string? name, string? lastName, string? company, string? email) => template
        .Replace("{{nombre}}", name ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{apellido}}", lastName ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{compañía}}", company ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{compania}}", company ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{correo}}", email ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{{fecha}}", DateTime.Now.ToString("dd/MM/yyyy"), StringComparison.OrdinalIgnoreCase)
        .Trim().Replace("  ", " ");

    private static string? Truncate(string? s) => s is null ? null : s.Length > 2000 ? s[..2000] + "…" : s;
}
