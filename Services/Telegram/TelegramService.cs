using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Dana.Data;
using Dana.Models;
using Dana.Services.Ai;

namespace Dana.Services.Telegram;

public class TelegramOptions
{
    public const string Section = "Telegram";

    /// <summary>Token del bot que entrega @BotFather. Va en User Secrets, no en appsettings.</summary>
    public string? BotToken { get; set; }

    /// <summary>Nombre del bot (p. ej. DanaCRMBot), solo para mostrarlo en la página de vinculación.</summary>
    public string? BotUsername { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}

/// <summary>
/// Conversación con los agentes de IA desde Telegram. Cada chat está vinculado a un usuario de Dana
/// y el agente actúa con los permisos de ese usuario. Recibe mensajes por long polling (no necesita URL pública).
/// </summary>
public class TelegramService
{
    public const int MaxMessageLength = 4000;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly TelegramOptions _options;
    private readonly HttpClient _http;
    private readonly IRepository<TelegramLink> _links;
    private readonly AgentService _agents;
    private readonly UserService _users;
    private readonly ILogger<TelegramService> _logger;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();

    public TelegramService(TelegramOptions options, IHttpClientFactory httpFactory, IRepository<TelegramLink> links,
        AgentService agents, UserService users, ILogger<TelegramService> logger)
    {
        _options = options;
        _http = httpFactory.CreateClient("telegram");
        _links = links;
        _agents = agents;
        _users = users;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;
    public string? BotUsername => _options.BotUsername;

    // ---------- Vinculación desde la UI de Dana ----------

    public async Task<TelegramLink?> GetLinkForUserAsync(string userName) =>
        (await _links.FindAsync(l => l.UserName == userName)).FirstOrDefault();

    public async Task<(string Code, DateTime ExpiresAt)> CreatePairingCodeAsync(string userName)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expires = DateTime.UtcNow.Add(CodeLifetime);
        var link = await GetLinkForUserAsync(userName) ?? new TelegramLink { UserName = userName };
        link.PairingCode = code;
        link.PairingExpiresAt = expires;
        if (link.Id is null) await _links.InsertAsync(link);
        else await _links.UpdateAsync(link);
        return (code, expires);
    }

    public async Task UnlinkUserAsync(string userName)
    {
        var link = await GetLinkForUserAsync(userName);
        if (link is null) return;
        link.ChatId = null;
        link.PairingCode = null;
        link.PairingExpiresAt = null;
        link.ConversationId = null;
        await _links.UpdateAsync(link);
    }

    // ---------- Recepción de mensajes ----------

    /// <summary>Consulta a Telegram en bucle y despacha cada mensaje de texto sin bloquear el resto de chats.</summary>
    public async Task RunPollingAsync(CancellationToken ct)
    {
        long offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var doc = await CallAsync("getUpdates", new { offset, timeout = 25, allowed_updates = new[] { "message" } }, ct);
                foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
                {
                    offset = update.GetProperty("update_id").GetInt64() + 1;
                    if (TryGetText(update, out var chatId, out var text))
                        _ = HandleMessageAsync(chatId, text, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("Telegram: {Error}. Reintento en 5 s.", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleMessageAsync(long chatId, string text, CancellationToken ct)
    {
        // Un mensaje a la vez por chat: mantiene el orden de la conversación.
        var gate = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await HandleTextAsync(chatId, text, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar un mensaje de Telegram");
            await SafeReplyAsync(chatId, "Ocurrió un error al procesar su mensaje. Intente de nuevo.", ct);
        }
        finally { gate.Release(); }
    }

    private async Task HandleTextAsync(long chatId, string text, CancellationToken ct)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].Split('@')[0].ToLowerInvariant();   // /agentes@MiBot → /agentes
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        switch (command)
        {
            case "/start":
            case "/ayuda":
                await ReplyAsync(chatId, Help, ct);
                return;
            case "/vincular":
                await LinkAsync(chatId, arg, ct);
                return;
            case "/desvincular":
                await UnlinkChatAsync(chatId, ct);
                return;
            case "/agentes":
                await ListAgentsAsync(chatId, ct);
                return;
            case "/agente":
                await SelectAgentAsync(chatId, arg, ct);
                return;
            case "/nueva":
                await NewConversationAsync(chatId, ct);
                return;
        }

        await ChatAsync(chatId, text, ct);
    }

    private async Task LinkAsync(long chatId, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            await ReplyAsync(chatId, "Use /vincular CODIGO con el código que muestra Dana.", ct);
            return;
        }

        var link = (await _links.FindAsync(l => l.PairingCode == code.Trim())).FirstOrDefault();
        if (link is null || link.PairingExpiresAt < DateTime.UtcNow)
        {
            await ReplyAsync(chatId, "Código no válido o vencido. Genere uno nuevo en Dana (Agentes → Telegram).", ct);
            return;
        }

        var user = await _users.FindByNameAsync(link.UserName);
        if (user is null || !user.IsActive)
        {
            await ReplyAsync(chatId, "La cuenta de Dana no está activa.", ct);
            return;
        }

        // Un chat solo puede estar vinculado a un usuario a la vez.
        foreach (var other in await _links.FindAsync(l => l.ChatId == chatId && l.Id != link.Id))
        {
            other.ChatId = null;
            other.ConversationId = null;
            await _links.UpdateAsync(other);
        }

        link.ChatId = chatId;
        link.PairingCode = null;
        link.PairingExpiresAt = null;
        link.ConversationId = null;
        await _links.UpdateAsync(link);
        await ReplyAsync(chatId, $"Listo: este chat está vinculado con {link.UserName}. Use /agentes para ver los agentes disponibles.", ct);
    }

    private async Task UnlinkChatAsync(long chatId, CancellationToken ct)
    {
        var link = (await _links.FindAsync(l => l.ChatId == chatId)).FirstOrDefault();
        if (link is null)
        {
            await ReplyAsync(chatId, "Este chat no está vinculado.", ct);
            return;
        }
        await UnlinkUserAsync(link.UserName);
        await ReplyAsync(chatId, "Chat desvinculado.", ct);
    }

    private async Task ListAgentsAsync(long chatId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(chatId, ct);
        if (link is null) return;

        var agents = (await _agents.GetAllAsync()).Where(a => a.IsActive).ToList();
        if (agents.Count == 0)
        {
            await ReplyAsync(chatId, "No hay agentes activos en Dana.", ct);
            return;
        }

        var current = await ResolveAgentAsync(link);
        var lines = agents.Select((a, i) => $"{i + 1}. {a.Name} — {a.Role}{(a.Id == current?.Id ? "  ← activo" : "")}");
        await ReplyAsync(chatId, "Agentes disponibles:\n" + string.Join("\n", lines) + "\n\nUse /agente NOMBRE o /agente NUMERO para cambiar.", ct);
    }

    private async Task SelectAgentAsync(long chatId, string arg, CancellationToken ct)
    {
        var link = await RequireLinkAsync(chatId, ct);
        if (link is null) return;

        var agents = (await _agents.GetAllAsync()).Where(a => a.IsActive).ToList();
        var agent = int.TryParse(arg, out var n) && n >= 1 && n <= agents.Count
            ? agents[n - 1]
            : agents.FirstOrDefault(a => string.Equals(a.Name, arg, StringComparison.OrdinalIgnoreCase));

        if (agent is null)
        {
            await ReplyAsync(chatId, "No encontré ese agente. Use /agentes para ver la lista.", ct);
            return;
        }

        link.AgentId = agent.Id;
        link.ConversationId = null;
        await _links.UpdateAsync(link);
        await ReplyAsync(chatId, $"Ahora habla con {agent.Name} ({agent.Role}). Empieza una conversación nueva.", ct);
    }

    private async Task NewConversationAsync(long chatId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(chatId, ct);
        if (link is null) return;
        link.ConversationId = null;
        await _links.UpdateAsync(link);
        await ReplyAsync(chatId, "Conversación nueva.", ct);
    }

    private async Task ChatAsync(long chatId, string text, CancellationToken ct)
    {
        var link = await RequireLinkAsync(chatId, ct);
        if (link is null) return;

        var user = await _users.FindByNameAsync(link.UserName);
        if (user is null || !user.IsActive)
        {
            await ReplyAsync(chatId, "Su cuenta de Dana ya no está activa.", ct);
            return;
        }

        var agent = await ResolveAgentAsync(link);
        if (agent is null)
        {
            await ReplyAsync(chatId, "No hay agentes activos en Dana.", ct);
            return;
        }

        var conv = link.ConversationId is null ? null : await _agents.GetConversationByIdAsync(link.ConversationId);
        if (conv is null || conv.AgentId != agent.Id)
        {
            conv = await _agents.NewConversationAsync(agent.Id!, link.UserName);
            link.ConversationId = conv.Id;
            await _links.UpdateAsync(link);
        }

        await SendTypingAsync(chatId, ct);
        var reply = await _agents.SendAsync(agent, conv, link.UserName, text, ct: ct);
        await ReplyAsync(chatId, reply.Role == "error" ? "⚠ " + reply.Text : reply.Text, ct);
    }

    private async Task<TelegramLink?> RequireLinkAsync(long chatId, CancellationToken ct)
    {
        var link = (await _links.FindAsync(l => l.ChatId == chatId)).FirstOrDefault();
        if (link is null)
            await ReplyAsync(chatId, "Este chat no está vinculado a Dana. Genere un código en Dana (Agentes → Telegram) y envíe /vincular CODIGO.", ct);
        return link;
    }

    private async Task<AiAgent?> ResolveAgentAsync(TelegramLink link)
    {
        var agents = (await _agents.GetAllAsync()).Where(a => a.IsActive).ToList();
        return agents.FirstOrDefault(a => a.Id == link.AgentId) ?? agents.FirstOrDefault();
    }

    private const string Help =
        "Hable con los agentes de Dana por aquí.\n\n" +
        "/vincular CODIGO — vincula este chat con su cuenta de Dana\n" +
        "/agentes — lista los agentes\n" +
        "/agente NOMBRE o NUMERO — cambia de agente\n" +
        "/nueva — empieza una conversación nueva\n" +
        "/desvincular — desvincula este chat\n\n" +
        "Cualquier otro texto se envía al agente activo.";

    // ---------- Telegram Bot API ----------

    private async Task<JsonDocument> CallAsync(string method, object payload, CancellationToken ct)
    {
        try
        {
            using var content = JsonContent.Create(payload);
            // La ruta lleva el token: no se incluye en los mensajes de error ni en los registros.
            using var response = await _http.PostAsync($"bot{_options.BotToken}/{method}", content, ct);
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "error desconocido";
                doc.Dispose();
                throw new InvalidOperationException($"Telegram {method}: {description}");
            }
            return doc;
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"Telegram {method}: error de red.");
        }
    }

    private async Task ReplyAsync(long chatId, string text, CancellationToken ct)
    {
        foreach (var chunk in Chunk(text))
        {
            using var doc = await CallAsync("sendMessage", new { chat_id = chatId, text = chunk }, ct);
        }
    }

    private async Task SafeReplyAsync(long chatId, string text, CancellationToken ct)
    {
        try { await ReplyAsync(chatId, text, ct); }
        catch (Exception ex) { _logger.LogWarning("No se pudo responder en Telegram: {Error}", ex.Message); }
    }

    private async Task SendTypingAsync(long chatId, CancellationToken ct)
    {
        try { using var doc = await CallAsync("sendChatAction", new { chat_id = chatId, action = "typing" }, ct); }
        catch (Exception ex) { _logger.LogDebug("sendChatAction falló: {Error}", ex.Message); }
    }

    private static bool TryGetText(JsonElement update, out long chatId, out string text)
    {
        chatId = 0;
        text = "";
        if (!update.TryGetProperty("message", out var message) || !message.TryGetProperty("text", out var t)) return false;
        chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
        text = t.GetString()?.Trim() ?? "";
        return text.Length > 0;
    }

    /// <summary>Parte el texto en trozos que Telegram acepta (4096 caracteres como máximo), cortando en saltos de línea cuando es posible.</summary>
    public static IEnumerable<string> Chunk(string text)
    {
        while (text.Length > MaxMessageLength)
        {
            var cut = text.LastIndexOf('\n', MaxMessageLength);
            if (cut < MaxMessageLength / 2) cut = MaxMessageLength;
            yield return text[..cut];
            text = text[cut..].TrimStart('\n');
        }
        yield return text;
    }
}
