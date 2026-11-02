using System.Collections.Concurrent;
using Dana.Data;
using Dana.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace Dana.Services.Ai;

/// <summary>Agentes de IA: configuración, conversaciones y envío de mensajes al proveedor con herramientas sobre el CRM.</summary>
public class AgentService
{
    private readonly IRepository<AiAgent> _agents;
    private readonly IRepository<AgentConversation> _convs;
    private readonly IRepository<AgentMemory> _memories;
    private readonly SettingsService _settings;
    private readonly AgentTools _tools;
    private readonly Dictionary<string, IChatProvider> _providers;
    private readonly IDataProtector _protector;
    private readonly NotificationService _notifications;
    private readonly UserService _users;
    private readonly ConcurrentDictionary<string, string> _busy = new();
    private readonly IServiceProvider _sp;
    private static readonly AsyncLocal<int> Depth = new();
    public const string SystemUser = "Sistema";
    public const int MaxDepth = 2;

    /// <summary>(agentId, mood) — cambió el estado de ánimo.</summary>
    public event Action<string, int>? MoodChanged;

    /// <summary>(agentId, ocupado) — para mostrar el "pensando…" en la oficina.</summary>
    public event Action<string, bool>? BusyChanged;
    /// <summary>(agentId, texto) — el agente "habla" en la oficina.</summary>
    public event Action<string, string>? Spoke;

    public AgentService(IRepository<AiAgent> agents, IRepository<AgentConversation> convs, SettingsService settings, AgentTools tools, IEnumerable<IChatProvider> providers, IDataProtectionProvider dp, NotificationService notifications, UserService users, IRepository<AgentMemory> memories, IServiceProvider sp)
    {
        _memories = memories; _sp = sp;
        _agents = agents; _convs = convs; _settings = settings; _tools = tools; _notifications = notifications; _users = users;
        _providers = providers.ToDictionary(p => p.Id);
        _protector = dp.CreateProtector("Dana.AiKeys");
    }

    // ---------- claves ----------
    public string Protect(string plain) => _protector.Protect(plain);
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _protector.Unprotect(protectedValue); } catch { return null; }
    }
    public string? KeyFor(AiSettings ai, string provider) => provider == AiProviders.OpenAi ? Unprotect(ai.OpenAiKeyProtected) : Unprotect(ai.AnthropicKeyProtected);
    public bool IsConfigured(AiSettings ai, string provider) => !string.IsNullOrEmpty(KeyFor(ai, provider));
    public bool AnyConfigured(AiSettings ai) => IsConfigured(ai, AiProviders.Anthropic) || IsConfigured(ai, AiProviders.OpenAi);

    public string ResolveProvider(AiAgent a, AiSettings ai)
    {
        var p = string.IsNullOrEmpty(a.Provider) ? ai.DefaultProvider : a.Provider;
        if (!IsConfigured(ai, p)) p = IsConfigured(ai, AiProviders.Anthropic) ? AiProviders.Anthropic : AiProviders.OpenAi;
        return p;
    }
    public string ResolveModel(AiAgent a, AiSettings ai, string provider) =>
        !string.IsNullOrWhiteSpace(a.Model) && (string.IsNullOrEmpty(a.Provider) || a.Provider == provider) ? a.Model! : provider == AiProviders.OpenAi ? ai.OpenAiModel : ai.AnthropicModel;

    public Task<List<string>> ListModelsAsync(string provider, string apiKey, CancellationToken ct = default) => _providers[provider].ListModelsAsync(apiKey, ct);

    public async Task<string> TestAsync(string provider, string apiKey, string model, CancellationToken ct = default)
    {
        var r = await _providers[provider].CompleteAsync(new ChatRequest(apiKey, model, "Responde en una sola frase corta en español.", [], "Saluda y di qué modelo eres.", [], 0, null), (_, _) => Task.FromResult(new ToolResult("")), null, ct);
        return r.Text;
    }

    // ---------- agentes ----------
    private List<AiAgent> _agentsCache = new();
    public async Task<List<AiAgent>> GetAllAsync() => _agentsCache = (await _agents.GetAllAsync()).OrderBy(a => a.Sort).ThenBy(a => a.CreatedAt).ToList();
    public Task<AiAgent?> GetAsync(string? id) => _agents.GetByIdAsync(id);
    public async Task SaveAsync(AiAgent a, string? by)
    {
        a.UpdatedBy = by;
        var others = (await GetAllAsync()).Where(x => x.Id != a.Id).ToList();
        if (others.Any(x => x.X == a.X && x.Y == a.Y)) (a.X, a.Y) = AgentTemplates.FreeSlot(others, a.Id);
        if (a.Id is null) { a.CreatedBy = by; await _agents.InsertAsync(a); } else await _agents.UpdateAsync(a);
        await GetAllAsync();
    }
    public Task DeleteAsync(string id, string? by) => _agents.DeleteAsync(id, by);
    public bool IsBusy(string agentId) => _busy.ContainsKey(agentId);
    public IReadOnlyCollection<string> BusyAgents => _busy.Keys.ToList();

    public async Task EnsureDefaultsAsync()
    {
        if (await _agents.CountAsync() > 0)
        {
            // migración: agentes creados con versiones anteriores reciben plantilla, herramientas nuevas y jefe por defecto
            var all = await GetAllAsync();
            var hasBoss = all.Any(a => a.IsBoss);
            var oldSlots = new HashSet<(int, int)> { (6, 4), (13, 4), (20, 4), (6, 11), (11, 11), (18, 11) };
            foreach (var a in all)
            {
                var changed = false;
                if (oldSlots.Contains((a.X, a.Y)) || all.Any(o => o.Id != a.Id && o.X == a.X && o.Y == a.Y && string.CompareOrdinal(o.Id, a.Id) < 0))
                {
                    (a.X, a.Y) = AgentTemplates.FreeSlot(all.Where(o => !oldSlots.Contains((o.X, o.Y)) || o == a).Where(o => o != a), a.Id);
                    changed = true;
                }
                if (a.Template is null)
                {
                    a.Template = a.Name switch { "Dana" => "general", "Sofía" => "analista", "Leo" => "prospeccion", "Mía" => "redaccion", _ => null };
                    changed = a.Template is not null;
                }
                var t = a.Template is null ? null : AgentTemplates.Find(a.Template);
                if (t is not null)
                    foreach (var tool in t.Tools.Where(x => (x is "consultar_agente" or "buscar_web" or "leer_pagina" or "recordar" or "olvidar" or "enviar_correo" or "enviar_mensaje" or "notificar_usuario" or "campos" || x.StartsWith("documento") || x.StartsWith("adjunto")) && !a.Tools.Contains(x))) { a.Tools.Add(tool); changed = true; }
                if (t is not null && t.AllowWrites && !a.AllowWrites && a.Template == "legal") { a.AllowWrites = true; changed = true; }
                foreach (var tool in new[] { "recordar", "olvidar" }.Where(x => !a.Tools.Contains(x))) { a.Tools.Add(tool); changed = true; }
                if (!hasBoss && a.Template == "general") { a.IsBoss = true; hasBoss = true; changed = true; }
                if (changed) await _agents.UpdateAsync(a);
            }
            return;
        }
        var i = 0;
        foreach (var id in new[] { "general", "analista", "prospeccion", "redaccion", "cotizador" })
        {
            var a = AgentTemplates.Create(AgentTemplates.Find(id)!, i++);
            a.IsBoss = id == "general";
            await _agents.InsertAsync(a);
        }
    }

    public async Task<AiAgent> CreateFromTemplateAsync(string templateId, string? by)
    {
        var t = AgentTemplates.Find(templateId) ?? throw new ArgumentException("Plantilla no encontrada");
        var existing = await GetAllAsync();
        var a = AgentTemplates.Create(t, existing.Count);
        (a.X, a.Y) = AgentTemplates.FreeSlot(existing);
        if (existing.Any(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))) a.Name += " " + (existing.Count(x => x.Template == t.Id) + 1);
        a.CreatedBy = by; a.UpdatedBy = by;
        await _agents.InsertAsync(a);
        return a;
    }

    /// <summary>Marca a un agente como jefe (y desmarca al resto). agentId null = sin jefe.</summary>
    public async Task SetBossAsync(string? agentId, string? by)
    {
        foreach (var a in await GetAllAsync())
        {
            var should = a.Id == agentId;
            if (a.IsBoss == should) continue;
            a.IsBoss = should; a.UpdatedBy = by;
            await _agents.UpdateAsync(a);
        }
        await GetAllAsync();
    }

    // ---------- estado de ánimo ----------
    public async Task AdjustMoodAsync(AiAgent agent, int delta, string reason)
    {
        var fresh = await _agents.GetByIdAsync(agent.Id) ?? agent;
        fresh.Mood = AgentMood.Clamp(fresh.Mood + delta);
        fresh.MoodNote = reason; fresh.MoodUpdatedAt = DateTime.UtcNow;
        agent.Mood = fresh.Mood; agent.MoodNote = fresh.MoodNote; agent.MoodUpdatedAt = fresh.MoodUpdatedAt;
        if (!ReferenceEquals(fresh, agent)) await _agents.UpdateAsync(fresh);
        MoodChanged?.Invoke(agent.Id!, fresh.Mood);
    }

    /// <summary>El ánimo vuelve lentamente hacia 70 con el tiempo (lo llama el worker cada minuto).</summary>
    public async Task DecayMoodsAsync()
    {
        foreach (var a in await GetAllAsync())
        {
            if (a.Mood == 70 || (DateTime.UtcNow - a.MoodUpdatedAt) < TimeSpan.FromHours(1)) continue;
            a.Mood += a.Mood > 70 ? -1 : 1; a.MoodUpdatedAt = DateTime.UtcNow;
            await _agents.UpdateAsync(a);
            MoodChanged?.Invoke(a.Id!, a.Mood);
        }
    }

    public async Task FeedbackAsync(AiAgent agent, AgentConversation conv, AgentMessage msg, int value)
    {
        var prev = msg.Feedback; msg.Feedback = value;
        await _convs.UpdateAsync(conv);
        if (prev == value) return;
        await AdjustMoodAsync(agent, value > 0 ? 6 : -9, value > 0 ? "👍 de " + conv.UserName : "👎 de " + conv.UserName);
    }

    // ---------- comunicación entre agentes ----------
    /// <summary>Un agente consulta a otro. Mantiene un hilo propio entre ambos y limita la profundidad para evitar bucles.</summary>
    public async Task<string> AgentToAgentAsync(AiAgent from, string targetNameOrId, string message, string user)
    {
        if (Depth.Value >= MaxDepth) throw new InvalidOperationException("Profundidad máxima de consultas entre agentes alcanzada; responde con lo que ya sabes.");
        var all = (await GetAllAsync()).Where(a => a.IsActive && a.Id != from.Id).ToList();
        var target = all.FirstOrDefault(a => a.Id == targetNameOrId) ?? all.FirstOrDefault(a => a.Name.Equals(targetNameOrId, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(a => a.Name.Contains(targetNameOrId, StringComparison.OrdinalIgnoreCase) || a.Role.Contains(targetNameOrId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No existe el agente \"{targetNameOrId}\". Agentes disponibles: {string.Join(", ", all.Select(a => a.Name + " (" + a.Role + ")"))}");
        if (!from.IsBoss && from.Collaborators.Count > 0 && !from.Collaborators.Contains(target.Id!) && !target.IsBoss) throw new InvalidOperationException($"{from.Name} no tiene permiso para consultar a {target.Name}.");
        if (IsBusy(target.Id!)) throw new InvalidOperationException($"{target.Name} está ocupado en este momento; inténtalo de nuevo más tarde.");
        var conv = await GetConversationAsync(target.Id!, SystemUser + ":a2a:" + from.Id);
        if (conv.Messages.Count > 30) conv.Messages = conv.Messages.TakeLast(12).ToList();
        Spoke?.Invoke(from.Id!, $"→ {target.Name}: {Trim(message, 90)}");
        Depth.Value++;
        try
        {
            var reply = await SendAsync(target, conv, $"{from.Name} (agente)", $"Mensaje del agente {from.Name} ({from.Role}) en nombre de {user}: {message}");
            if (reply.Role == "error") throw new InvalidOperationException($"{target.Name} no pudo responder: {reply.Text}");
            return $"Respuesta de {target.Name} ({target.Role}):\n{reply.Text}";
        }
        finally { Depth.Value--; }
    }

    // ---------- conversaciones ----------
    public async Task<AgentConversation> GetConversationAsync(string agentId, string user)
    {
        var c = (await _convs.FindAsync(x => x.AgentId == agentId && x.UserName == user)).OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
        return c ?? await _convs.InsertAsync(new AgentConversation { AgentId = agentId, UserName = user, CreatedBy = user });
    }
    public Task<AgentConversation> NewConversationAsync(string agentId, string user) => _convs.InsertAsync(new AgentConversation { AgentId = agentId, UserName = user, CreatedBy = user });
    public async Task<List<AgentConversation>> ListConversationsAsync(string agentId, string user) => (await _convs.FindAsync(x => x.AgentId == agentId && x.UserName == user)).OrderByDescending(x => x.UpdatedAt).ToList();
    public Task<AgentConversation?> GetConversationByIdAsync(string id) => _convs.GetByIdAsync(id);
    public Task DeleteConversationAsync(string id) => _convs.PurgeAsync(id);

    /// <summary>Envía un mensaje del usuario al agente, ejecuta las herramientas necesarias y guarda la conversación.</summary>
    public async Task<AgentMessage> SendAsync(AiAgent agent, AgentConversation conv, string user, string text, Action<string>? progress = null, CancellationToken ct = default, Action<string>? onDelta = null)
    {
        var ai = (await _settings.GetAsync()).Ai;
        if (_agentsCache.Count == 0) await GetAllAsync();
        if (!AnyConfigured(ai)) throw new InvalidOperationException("No hay ninguna clave de API configurada. Un administrador debe añadirla en Configuración → Proveedores de IA.");
        var provider = ResolveProvider(agent, ai);
        var key = KeyFor(ai, provider)!;
        var model = ResolveModel(agent, ai, provider);
        var tools = AgentTools.Catalog.Where(t => agent.Tools.Contains(t.Name)).Where(t => !t.Write || agent.AllowWrites).Where(t => t.Name != "consultar_agente" || (agent.CanCollaborate && Depth.Value < MaxDepth)).ToList();

        conv.Messages.Add(new AgentMessage { Role = "user", Text = text });
        if (conv.Messages.Count(m => m.Role == "user") == 1) conv.Title = text.Length > 60 ? text[..60] + "…" : text;
        var history = conv.Messages.Where(m => m.Role is "user" or "assistant").SkipLast(1).TakeLast(30).Select(m => new ChatTurn(m.Role, m.Text)).ToList();

        _busy[agent.Id!] = user;
        BusyChanged?.Invoke(agent.Id!, true);
        try
        {
            var author = $"{user} · {agent.Name}";
            var reply = await _providers[provider].CompleteAsync(
                new ChatRequest(key, model, BuildSystem(agent, user, await _settings.GetAsync(), tools) + await MemoryPromptAsync(agent, user), history, text, tools, Math.Max(1, ai.MaxToolCalls), string.IsNullOrEmpty(agent.Effort) ? null : agent.Effort, onDelta),
                (name, input) => _tools.ExecuteAsync(name, input, new ToolContext(author, agent.AllowWrites, agent, user, false, conv)), progress, ct);
            foreach (var c in reply.ToolCalls)
                conv.Messages.Add(new AgentMessage { Role = "tool", ToolName = c.Name, ToolInput = c.InputJson, Text = c.IsError ? "⚠ " + c.Output : Trim(c.Output, 400) });
            var msg = new AgentMessage { Role = "assistant", Text = string.IsNullOrWhiteSpace(reply.Text) ? "(sin respuesta)" : reply.Text, InputTokens = (int)reply.InputTokens, OutputTokens = (int)reply.OutputTokens };
            conv.Messages.Add(msg);
            agent.MessageCount++;
            await _agents.UpdateAsync(agent);
            Spoke?.Invoke(agent.Id!, msg.Text);
            var errors = reply.ToolCalls.Count(c => c.IsError);
            var writes = reply.ToolCalls.Count(c => !c.IsError && (AgentTools.Find(c.Name)?.Write ?? false));
            await AdjustMoodAsync(agent, 2 + writes * 2 - errors * 3, errors > 0 ? $"{errors} herramienta(s) fallaron" : writes > 0 ? "trabajo completado" : "conversación fluida");
            return msg;
        }
        catch (OperationCanceledException)
        {
            var msg = new AgentMessage { Role = "error", Text = "Respuesta detenida por el usuario." };
            conv.Messages.Add(msg);
            return msg;
        }
        catch (Exception ex)
        {
            var msg = new AgentMessage { Role = "error", Text = ex.Message };
            conv.Messages.Add(msg);
            try { await AdjustMoodAsync(agent, -8, "error: " + Trim(ex.Message, 60)); } catch { }
            return msg;
        }
        finally
        {
            _busy.TryRemove(agent.Id!, out _);
            BusyChanged?.Invoke(agent.Id!, false);
            conv.UpdatedBy = user;
            await _convs.UpdateAsync(conv);
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>Consume un cupo de envío autónomo del día. Devuelve null si está permitido, o un mensaje si se alcanzó el límite.</summary>
    public async Task<string?> TryConsumeSendAsync(AiAgent agent)
    {
        var today = DateTime.UtcNow.Date;
        if (agent.SendCountDate != today) { agent.SendCountDate = today; agent.SendCountToday = 0; }
        var limit = agent.DailySendLimit <= 0 ? 20 : agent.DailySendLimit;
        if (agent.SendCountToday >= limit)
            return $"Límite diario de envíos autónomos alcanzado ({limit}). No se realizó el envío.";
        agent.SendCountToday++;
        try { await _agents.UpdateAsync(agent); } catch { }
        return null;
    }

    // ---------- acciones con confirmación ----------
    /// <summary>Ejecuta (o cancela) una acción pendiente de una conversación y deja constancia como mensaje de herramienta.</summary>
    public async Task<AgentMessage> ResolveActionAsync(AiAgent agent, AgentConversation conv, PendingAction action, bool approve, string user)
    {
        if (action.Status != "pending") return conv.Messages.Last();
        AgentMessage note;
        if (!approve)
        {
            action.Status = "cancelled";
            note = new AgentMessage { Role = "tool", ToolName = action.Tool, Text = "Acción cancelada por el usuario." };
        }
        else
        {
            var r = await _tools.ExecuteAsync(action.Tool, JsonDocument.Parse(action.InputJson).RootElement.Clone(), new ToolContext($"{user} · {agent.Name}", true, agent, user, true, conv));
            action.Status = r.IsError ? "failed" : "done"; action.Result = r.Output;
            var friendly = r.Output;
            try { var doc = JsonDocument.Parse(r.Output).RootElement; if (doc.ValueKind == JsonValueKind.Object) friendly = doc.TryGetProperty("nota", out var n) ? n.GetString() ?? r.Output : doc.TryGetProperty("ok", out var ok) && ok.GetBoolean() ? "Realizada correctamente." : r.Output; } catch { }
            note = new AgentMessage { Role = "tool", ToolName = action.Tool, Text = r.IsError ? "⚠ " + r.Output : "✔ " + Trim(friendly, 300) };
            await AdjustMoodAsync(agent, r.IsError ? -4 : 4, r.IsError ? "una acción confirmada falló" : "acción aprobada por " + user);
        }
        conv.Messages.Add(note);
        await _convs.UpdateAsync(conv);
        return note;
    }

    // ---------- memoria de largo plazo ----------
    public async Task<AgentMemory> RememberAsync(AiAgent agent, string? userName, string text, string? by)
    {
        var existing = await _memories.FindAsync(m => m.AgentId == agent.Id && m.UserName == userName && m.Text == text);
        if (existing.Count > 0) return existing[0];
        return await _memories.InsertAsync(new AgentMemory { AgentId = agent.Id!, UserName = userName, Text = text, Source = by, CreatedBy = by });
    }
    public async Task<bool> ForgetAsync(AiAgent agent, string id)
    {
        var m = await _memories.GetByIdAsync(id);
        if (m is null || m.AgentId != agent.Id) return false;
        await _memories.PurgeAsync(id);
        return true;
    }
    public async Task<List<AgentMemory>> GetMemoriesAsync(string agentId) => (await _memories.FindAsync(m => m.AgentId == agentId)).OrderByDescending(m => m.CreatedAt).ToList();

    private async Task<string> MemoryPromptAsync(AiAgent agent, string user)
    {
        var all = await _memories.FindAsync(m => m.AgentId == agent.Id && (m.UserName == null || m.UserName == user));
        if (all.Count == 0) return "\nMemoria de largo plazo: vacía. Si el usuario expresa una preferencia estable, un acuerdo o un hecho útil para el futuro, guárdalo con la herramienta recordar (sin pedir permiso para hechos triviales).\n";
        var sb = new System.Text.StringBuilder("\nMemoria de largo plazo (usa estos recuerdos cuando sean relevantes; puedes añadir con recordar u olvidar los obsoletos):\n");
        var budget = 4000;
        foreach (var m in all.OrderByDescending(m => m.CreatedAt).Take(60))
        {
            var line = $"- [{m.Id}] ({(m.UserName is null ? "equipo" : m.UserName)}) {m.Text}\n";
            if ((budget -= line.Length) < 0) break;
            sb.Append(line);
        }
        return sb.ToString();
    }

    // ---------- proactividad ----------
    /// <summary>Ejecuta una rutina del agente ahora y entrega el resultado a sus destinatarios. Devuelve el mensaje generado.</summary>
    public async Task<AgentMessage> RunRoutineAsync(AiAgent agent, AgentRoutine routine, string trigger = "programada", CancellationToken ct = default)
    {
        var conv = await GetConversationAsync(agent.Id!, SystemUser + ":" + routine.Id);
        if (conv.Messages.Count > 40) conv.Messages = conv.Messages.TakeLast(20).ToList();
        var prompt = routine.Prompt.Trim() + $"\n\n(Ejecución {trigger} de la rutina \"{routine.Name}\": no hay un usuario esperando en el chat. Entrega un informe breve, directo y útil para el equipo; si no hay nada relevante, dilo en una línea.)";
        var msg = await SendAsync(agent, conv, SystemUser, prompt, null, ct);
        routine.LastRunAt = DateTime.UtcNow; routine.RunCount++;
        if (msg.Role == "error") { routine.LastError = msg.Text; routine.LastOutput = null; }
        else
        {
            routine.LastError = null; routine.LastOutput = Trim(msg.Text, 2000);
            await DeliverAsync(agent, msg.Text, routine.Name, routine.Recipients, "agentes?chat=" + agent.Id);
            if (routine.SendEmail) await EmailRoutineAsync(agent, routine, msg.Text);
        }
        var fresh = await _agents.GetByIdAsync(agent.Id);
        if (fresh is not null)
        {
            var r = fresh.Routines.FirstOrDefault(x => x.Id == routine.Id);
            if (r is not null) { r.LastRunAt = routine.LastRunAt; r.RunCount = routine.RunCount; r.LastError = routine.LastError; r.LastOutput = routine.LastOutput; }
            fresh.MessageCount = Math.Max(fresh.MessageCount, agent.MessageCount);
            await _agents.UpdateAsync(fresh);
        }
        return msg;
    }

    /// <summary>Consulta puntual disparada por una automatización (evento del CRM). Devuelve la respuesta del agente.</summary>
    public async Task<AgentMessage> AskAsync(AiAgent agent, string prompt, string source, IEnumerable<string> recipients, string? link, CancellationToken ct = default)
    {
        var conv = await NewConversationAsync(agent.Id!, SystemUser + ":auto");
        var msg = await SendAsync(agent, conv, SystemUser, prompt, null, ct);
        if (msg.Role != "error") await DeliverAsync(agent, msg.Text, source, recipients.ToList(), link ?? "agentes?chat=" + agent.Id);
        return msg;
    }

    private async Task EmailRoutineAsync(AiAgent agent, AgentRoutine routine, string text)
    {
        var email = _sp.GetRequiredService<EmailService>();
        if (!email.IsConfigured) return;
        var users = (await _users.GetAllAsync()).Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email) && (routine.Recipients.Count == 0 || routine.Recipients.Contains(u.Name)));
        var plain = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*|`|^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (var u in users)
        {
            try { await email.SendAsync(u.Email, $"[Dana] {agent.Name}: {routine.Name}", plain, "agent", agent.Id!, agent.Name, agent.Name); } catch { }
        }
    }

    /// <summary>Entrega un mensaje proactivo: lo añade a la conversación de cada destinatario con el agente y le envía una notificación.</summary>
    private async Task DeliverAsync(AiAgent agent, string text, string source, List<string> recipients, string link)
    {
        var targets = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        if (targets.Count == 0) targets = (await _users.GetAllAsync()).Where(u => u.IsActive).Select(u => u.Name).ToList();
        foreach (var user in targets)
        {
            try
            {
                var conv = await GetConversationAsync(agent.Id!, user);
                conv.Messages.Add(new AgentMessage { Role = "assistant", Text = text, Source = source });
                await _convs.UpdateAsync(conv);
                await _notifications.NotifyAsync(user, NotificationKinds.System, $"🤖 {agent.Name} ({source}): {Trim(System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1"), @"[*#`_>]|\s+", m => m.Value[0] is '*' or '#' or '`' or '_' or '>' ? "" : " ").Trim(), 140)}", link, agent.Name);
            }
            catch { }
        }
    }

    private string BuildSystem(AiAgent agent, string user, AppSettings s, List<ToolDef> tools)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(agent.Instructions.Trim());
        sb.AppendLine();
        var mood = AgentMood.Describe(agent.Mood);
        sb.AppendLine($"Tu estado de ánimo actual es \"{mood.Label}\" ({agent.Mood}/100){(agent.MoodNote is null ? "" : ", porque: " + agent.MoodNote)}. Deja que se note sutilmente en el tono (más energía si estás entusiasmado, más sobrio si estás cansado), sin dejar de ser profesional ni mencionarlo salvo que te pregunten.");
        var boss = _agentsCache.FirstOrDefault(a => a.IsBoss && a.IsActive);
        if (agent.IsBoss) sb.AppendLine("Eres el JEFE del equipo de agentes: coordinas a los demás, puedes delegarles trabajo con consultar_agente, consolidas sus respuestas y tomas la decisión final. Los otros agentes te escalan lo que excede su rol.");
        else if (boss is not null && boss.Id != agent.Id) sb.AppendLine($"El jefe del equipo de agentes es {boss.Name} ({boss.Role}). Si una petición excede claramente tu rol o requiere coordinar a varios agentes, consúltale con consultar_agente (si la tienes) o sugiere al usuario hablar con {boss.Name}.");
        if (tools.Any(t => t.Name == "consultar_agente"))
        {
            var others = _agentsCache.Where(a => a.IsActive && a.Id != agent.Id && (agent.IsBoss || agent.Collaborators.Count == 0 || agent.Collaborators.Contains(a.Id!) || a.IsBoss)).Select(a => $"{a.Name} ({a.Role}{(a.IsBoss ? ", jefe" : "")})").ToList();
            if (others.Count > 0) sb.AppendLine("Puedes consultar a estos agentes con consultar_agente: " + string.Join(", ", others) + ". Hazlo cuando el tema sea de su especialidad; incluye su aporte en tu respuesta citando quién lo dijo.");
        }
        sb.AppendLine($"Contexto: trabajas dentro de Dana, el CRM de \"{s.CompanyName}\". Hablas con {user}. Fecha actual (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}. Moneda base: {s.BaseCurrency}.");
        sb.AppendLine("Responde siempre en español, de forma concisa y útil. Usa Markdown ligero (listas, negritas) cuando ayude.");
        if (tools.Count > 0)
        {
            sb.AppendLine("Tienes herramientas para consultar" + (tools.Any(t => t.Write) ? " y modificar" : "") + " los datos reales del CRM: úsalas en lugar de inventar información. Si buscas un nombre y no aparece en un tipo (p. ej. compañía), prueba en los otros tipos (prospecto, contacto, negociación) antes de decir que no existe; un cliente suele estar como prospecto o contacto aunque el usuario lo llame compañía.");
            sb.AppendLine("Cuando menciones un registro, puedes enlazarlo con la ruta relativa que devuelven las herramientas (p. ej. [Título](leads/ID)).");
            if (tools.Any(t => t.Name == "documento_crear")) sb.AppendLine("Si el usuario pide un documento (contrato, NDA, propuesta, carta, acta…), créalo en el sistema con documento_crear en vez de pegarlo en el chat, y responde con un resumen breve y el enlace (documentos/ID). Si el usuario adjuntó archivos en esta conversación, léelos con adjunto_leer (usa adjuntos_listar con tipo=conversation si no conoces el id).");
            if (tools.Any(t => t.Write)) sb.AppendLine("Antes de crear o modificar datos, asegúrate de tener los campos clave; no pidas confirmación extra si el usuario ya lo indicó claramente.");
            if (tools.Any(t => t.Write) && tools.Any(t => t.Name == "campos")) sb.AppendLine("Para ENRIQUECER un prospecto, contacto, compañía o negociación con información que investigaste: primero usa la herramienta 'campos' para ver los campos que existen. Guarda cada dato en su campo ESTÁNDAR siempre que exista uno (correo→email, teléfono→phone, sitio web→web, dirección→address, industria→industry, nº de empleados→employees, RUC/cédula→taxId, etc.); NUNCA pongas esos datos en 'custom' ni crees campos nuevos que dupliquen un campo estándar. Usa el objeto 'custom' de actualizar/crear solo para datos que realmente no tienen campo estándar (p. ej. año de fundación, horario, certificaciones), reutilizando por su título los campos personalizados ya creados. Si el registro ya existe, actualízalo en vez de duplicar. No inventes datos: distingue lo verificado de lo supuesto.");
            if (tools.Any(t => t.Confirm)) sb.AppendLine("REGLA OBLIGATORIA para correos y mensajes: cuando el usuario pida enviar un correo, SMS o WhatsApp, DEBES invocar la herramienta enviar_correo o enviar_mensaje con el contenido ya redactado, en esa misma respuesta y sin preguntar antes. Sin la invocación no se prepara nada: nunca describas un envío ni anuncies una tarjeta sin haber llamado a la herramienta. La herramienta no envía de inmediato: el sistema muestra al usuario una tarjeta para confirmar o cancelar; tras invocarla, dile brevemente que revise y confirme la tarjeta.");
        }
        else sb.AppendLine("No tienes acceso a los datos del CRM en esta conversación; si el usuario pide datos concretos, indícale que active herramientas para este agente.");
        return sb.ToString();
    }
}
