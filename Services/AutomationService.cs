using System.Net.Http.Json;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>Motor de automatizaciones. Resuelve los demás servicios de forma diferida para evitar dependencias circulares.</summary>
public class AutomationService
{
    public const string Actor = "Automatización";
    private static readonly AsyncLocal<int> Depth = new();

    private readonly IRepository<Automation> _automations;
    private readonly IServiceProvider _sp;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(IRepository<Automation> automations, IServiceProvider sp, ILogger<AutomationService> logger)
    {
        _automations = automations;
        _sp = sp;
        _logger = logger;
    }

    public async Task<List<Automation>> GetAllAsync() => (await _automations.GetAllAsync()).OrderBy(a => a.BoardKey).ThenBy(a => a.Name).ToList();
    public Task<Automation?> GetAsync(string? id) => _automations.GetByIdAsync(id);
    public async Task SaveAsync(Automation a) { if (a.Id is null) await _automations.InsertAsync(a); else await _automations.UpdateAsync(a); }
    public Task DeleteAsync(string id) => _automations.DeleteAsync(id);

    /// <summary>Evalúa y ejecuta las automatizaciones que coincidan con el evento. Nunca lanza excepciones hacia el llamador.</summary>
    public async Task TriggerAsync(AutomationEvent ev)
    {
        if (Depth.Value >= 3) return; // evita bucles (una automatización que dispara otra que dispara la primera…)
        List<Automation> candidates;
        try
        {
            candidates = await _automations.FindAsync(a => a.IsEnabled && a.BoardKey == ev.BoardKey);
        }
        catch (Exception ex) { _logger.LogError(ex, "No se pudieron cargar las automatizaciones"); return; }

        foreach (var a in candidates.Where(a => Matches(a.Trigger, ev)))
        {
            Depth.Value++;
            try
            {
                foreach (var action in a.Actions) await ExecuteAsync(a, action, ev);
                a.RunCount++; a.LastRunAt = DateTime.UtcNow; a.LastError = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ejecutando la automatización {Name}", a.Name);
                a.LastError = ex.Message; a.LastRunAt = DateTime.UtcNow;
            }
            finally { Depth.Value--; }
            try { await _automations.UpdateAsync(a); } catch { }
        }
    }

    private static bool Matches(AutomationStep trigger, AutomationEvent ev)
    {
        if (trigger.Type != ev.Type) return false;
        var data = ev.Data ?? new();
        return trigger.Type switch
        {
            TriggerTypes.StageChanged => trigger.Get("stageId") == "" || trigger.Get("stageId") == data.GetValueOrDefault("stageId"),
            TriggerTypes.StatusChanged => trigger.Get("status") == "" || trigger.Get("status") == data.GetValueOrDefault("status"),
            TriggerTypes.QuoteAccepted => trigger.Get("estado") == "" || trigger.Get("estado") == data.GetValueOrDefault("status"),
            TriggerTypes.Inactive => int.TryParse(data.GetValueOrDefault("days"), out var di) && di >= (int.TryParse(trigger.Get("days"), out var th) ? th : 7),
            _ => true
        };
    }

    private async Task ExecuteAsync(Automation a, AutomationStep action, AutomationEvent ev)
    {
        var item = ev.Item;
        var entityType = CrmEntities.TypeOf(item);
        var title = CrmEntities.TitleOf(item);
        var activities = _sp.GetRequiredService<ActivityService>();
        var notifications = _sp.GetRequiredService<NotificationService>();

        async Task LogAsync(string text) => await activities.LogAsync(entityType, item.Id!, title, ActivityKinds.System, $"⚡ {a.Name}: {text}", Actor);

        switch (action.Type)
        {
            case ActionTypes.Notify:
            {
                var user = await ResolveUserAsync(action.Get("user", "@assignee"), item, a);
                if (string.IsNullOrEmpty(user)) break;
                var text = await RenderAsync(action.Get("text", "{{titulo}}"), item);
                await notifications.NotifyAsync(user, NotificationKinds.System, text, CrmEntities.LinkOf(item), Actor);
                await LogAsync($"notificó a {user}");
                break;
            }
            case ActionTypes.Assign:
            {
                var user = await ResolveUserAsync(action.Get("user", "@roundrobin"), item, a);
                if (string.IsNullOrEmpty(user) || user == CrmEntities.AssigneeOf(item)) break;
                CrmEntities.SetAssignee(item, user);
                await CrmEntities.PersistAsync(_sp, item);
                await notifications.NotifyAsync(user, NotificationKinds.Assigned, $"te asignó \"{title}\"", CrmEntities.LinkOf(item), Actor);
                await LogAsync($"asignó a {user}");
                break;
            }
            case ActionTypes.SetStage when item is Deal deal:
            {
                var stageId = action.Get("stageId");
                if (stageId == "" || stageId == deal.StageId) break;
                var updated = await _sp.GetRequiredService<DealService>().MoveToStageAsync(deal.Id!, stageId, Actor);
                if (updated is not null) { deal.StageId = updated.StageId; deal.ClosedAt = updated.ClosedAt; deal.History = updated.History; }
                break;
            }
            case ActionTypes.SetStatus when item is Lead lead:
            {
                var status = action.Get("status");
                if (status == "" || status == lead.Status) break;
                lead.Status = status;
                await _sp.GetRequiredService<LeadService>().SaveAsync(lead, Actor);
                await LogAsync($"cambió el estado a {LeadStatuses.Get(status).Name}");
                break;
            }
            case ActionTypes.CreateTask:
            {
                var user = await ResolveUserAsync(action.Get("user", "@assignee"), item, a);
                var days = int.TryParse(action.Get("days", "1"), out var d) ? d : 1;
                var task = new TaskItem
                {
                    Title = await RenderAsync(action.Get("title", "Seguimiento: {{titulo}}"), item),
                    Kind = action.Get("kind", TaskKinds.Task),
                    DueAt = DateTime.UtcNow.Date.AddDays(days).AddHours(9),
                    AssignedTo = user,
                    EntityType = entityType, EntityId = item.Id, EntityTitle = title
                };
                await _sp.GetRequiredService<TaskService>().SaveAsync(task, Actor);
                break;
            }
            case ActionTypes.SendEmail:
            {
                var email = _sp.GetRequiredService<EmailService>();
                var template = await email.GetTemplateAsync(action.Get("templateId"));
                var to = await EmailForAsync(item);
                if (template is null || string.IsNullOrWhiteSpace(to)) { await LogAsync("no se envió correo (sin plantilla o sin dirección)"); break; }
                var ctx = await ContextForAsync(item);
                var (sent, error) = await email.SendAsync(to, EmailService.Render(template.Subject, ctx), EmailService.Render(template.Body, ctx), entityType, item.Id!, title, Actor);
                if (error is not null) throw new InvalidOperationException(error);
                await LogAsync(sent ? $"envió correo \"{template.Name}\" a {to}" : $"registró correo \"{template.Name}\" para {to} (SMTP no configurado)");
                break;
            }
            case ActionTypes.Webhook:
            {
                var url = action.Get("url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) break;
                var client = _sp.GetRequiredService<IHttpClientFactory>().CreateClient("automations");
                var payload = new { automation = a.Name, @event = ev.Type, board = ev.BoardKey, data = ev.Data, item };
                var res = await client.PostAsJsonAsync(uri, payload);
                await LogAsync($"webhook {uri.Host} → HTTP {(int)res.StatusCode}");
                break;
            }
            case ActionTypes.AskAgent:
            {
                var agents = _sp.GetRequiredService<Dana.Services.Ai.AgentService>();
                var agent = await agents.GetAsync(action.Get("agentId")) ?? throw new InvalidOperationException("El agente no existe");
                var prompt = await RenderAsync(action.Get("prompt", "Revisa {{titulo}} y dime qué hacer."), item);
                prompt += $"\n\nRegistro de referencia: tipo={entityType}, id={item.Id}, título=\"{title}\" (puedes consultarlo con la herramienta 'obtener').";
                var spec = action.Get("user", "@assignee");
                var user = spec == "@all" ? null : await ResolveUserAsync(spec, item, a);
                var reply = await agents.AskAsync(agent, prompt, $"automatización · {a.Name}", user is null ? [] : [user], CrmEntities.LinkOf(item));
                if (reply.Role == "error") throw new InvalidOperationException(reply.Text);
                if (action.Get("note", "1") == "1") await activities.LogAsync(entityType, item.Id!, title, ActivityKinds.Note, reply.Text, $"🤖 {agent.Name}");
                await LogAsync($"consultó al agente {agent.Name}");
                break;
            }
            case ActionTypes.Archive:
                item.IsArchived = true;
                await CrmEntities.PersistAsync(_sp, item);
                await LogAsync("archivó el elemento");
                break;
        }
    }

    private async Task<string?> ResolveUserAsync(string spec, Entity item, Automation a)
    {
        if (spec == "@assignee") return CrmEntities.AssigneeOf(item);
        if (spec == "@roundrobin")
        {
            var users = (await _sp.GetRequiredService<UserService>().GetAllAsync()).Where(u => u.IsActive).OrderBy(u => u.Name).Select(u => u.Name).ToList();
            return users.Count == 0 ? null : users[a.RunCount % users.Count];
        }
        return spec;
    }

    private async Task<string?> EmailForAsync(Entity item)
    {
        if (item is Deal d)
        {
            if (d.ContactId is not null) return (await _sp.GetRequiredService<IRepository<Contact>>().GetByIdAsync(d.ContactId))?.Email;
            if (d.CompanyId is not null) return (await _sp.GetRequiredService<IRepository<Company>>().GetByIdAsync(d.CompanyId))?.Email;
            return null;
        }
        return CrmEntities.EmailOf(item);
    }

    private async Task<EmailContext> ContextForAsync(Entity item)
    {
        switch (item)
        {
            case Lead l: return new EmailContext(l.Name, l.LastName, l.CompanyName, l.Email, l.Title, Actor);
            case Contact c:
                var co = c.CompanyId is null ? null : await _sp.GetRequiredService<IRepository<Company>>().GetByIdAsync(c.CompanyId);
                return new EmailContext(c.Name, c.LastName, co?.Title, c.Email, c.FullName, Actor);
            case Company k: return new EmailContext(null, null, k.Title, k.Email, k.Title, Actor);
            case Deal d:
                var ct = d.ContactId is null ? null : await _sp.GetRequiredService<IRepository<Contact>>().GetByIdAsync(d.ContactId);
                var cp = d.CompanyId is null ? null : await _sp.GetRequiredService<IRepository<Company>>().GetByIdAsync(d.CompanyId);
                return new EmailContext(ct?.Name, ct?.LastName, cp?.Title, ct?.Email ?? cp?.Email, d.Title, Actor);
            default: return new EmailContext(null, null, null, null, CrmEntities.TitleOf(item), Actor);
        }
    }

    /// <summary>Marcadores en textos de automatización: {{titulo}}, {{responsable}}, {{etapa}}, {{estado}}, {{importe}}.</summary>
    public async Task<string> RenderAsync(string text, Entity item)
    {
        var stage = "";
        if (item is Deal d)
        {
            var p = await _sp.GetRequiredService<PipelineService>().GetAsync(d.PipelineId);
            stage = p?.GetStage(d.StageId)?.Name ?? "";
        }
        return text
            .Replace("{{titulo}}", CrmEntities.TitleOf(item), StringComparison.OrdinalIgnoreCase)
            .Replace("{{responsable}}", CrmEntities.AssigneeOf(item) ?? "sin asignar", StringComparison.OrdinalIgnoreCase)
            .Replace("{{etapa}}", stage, StringComparison.OrdinalIgnoreCase)
            .Replace("{{estado}}", item is Lead l ? LeadStatuses.Get(l.Status).Name : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{importe}}", item is Deal dd ? dd.Amount.ToString("N2") : "", StringComparison.OrdinalIgnoreCase);
    }
}
