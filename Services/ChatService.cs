using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>Chat interno entre empleados: directos y grupos, con entrega en tiempo real (Blazor Server).</summary>
public class ChatService
{
    private readonly IRepository<ChatChannel> _channels;
    private readonly IRepository<ChatMessage> _messages;
    private readonly NotificationService _notifications;

    /// <summary>Se dispara al llegar un mensaje nuevo (channelId). Las páginas de chat abiertas se refrescan.</summary>
    public event Action<string>? MessageSent;

    public ChatService(IRepository<ChatChannel> channels, IRepository<ChatMessage> messages, NotificationService notifications)
    {
        _channels = channels;
        _messages = messages;
        _notifications = notifications;
    }

    public async Task<List<ChatChannel>> GetChannelsForAsync(string user)
    {
        var all = await _channels.GetAllAsync();
        return all.Where(c => c.IsMember(user))
                  .OrderByDescending(c => c.LastAt ?? c.CreatedAt)
                  .ToList();
    }

    public Task<ChatChannel?> GetChannelAsync(string id) => _channels.GetByIdAsync(id);

    /// <summary>Busca (o crea) el canal directo entre dos personas.</summary>
    public async Task<ChatChannel> GetOrCreateDmAsync(string a, string b, string by)
    {
        var all = await _channels.GetAllAsync();
        var existing = all.FirstOrDefault(c => c.Kind == ChatKinds.Dm && c.Members.Count == 2 && c.IsMember(a) && c.IsMember(b));
        if (existing is not null) return existing;
        var ch = new ChatChannel { Kind = ChatKinds.Dm, Members = new() { a, b }, Color = Components.Shared.Avatar.ColorFor(b), CreatedBy = by };
        return await _channels.InsertAsync(ch);
    }

    public async Task<ChatChannel> CreateGroupAsync(string name, IEnumerable<string> members, string by)
    {
        var list = members.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!list.Contains(by, StringComparer.OrdinalIgnoreCase)) list.Add(by);
        var ch = new ChatChannel { Kind = ChatKinds.Group, Name = name.Trim(), Members = list, Color = Components.Shared.Avatar.ColorFor(name), CreatedBy = by };
        return await _channels.InsertAsync(ch);
    }

    public async Task UpdateGroupAsync(ChatChannel ch, string name, IEnumerable<string> members, string by)
    {
        ch.Name = name.Trim();
        ch.Members = members.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!ch.Members.Contains(by, StringComparer.OrdinalIgnoreCase)) ch.Members.Add(by);
        await _channels.UpdateAsync(ch);
        MessageSent?.Invoke(ch.Id!);
    }

    public Task<List<ChatMessage>> GetMessagesAsync(string channelId, int limit = 200) =>
        _messages.Collection.Find(m => m.ChannelId == channelId && m.DeletedAt == null)
            .SortBy(m => m.CreatedAt).Limit(limit).ToListAsync();

    public async Task<ChatMessage> SendAsync(ChatChannel ch, string author, string text, List<string>? attachmentIds = null)
    {
        var msg = new ChatMessage { ChannelId = ch.Id!, Author = author, Text = text.Trim(), AttachmentIds = attachmentIds ?? new(), CreatedBy = author };
        await _messages.InsertAsync(msg);

        ch.LastText = !string.IsNullOrWhiteSpace(msg.Text) ? (msg.Text.Length > 120 ? msg.Text[..120] : msg.Text) : "📎 Adjunto";
        ch.LastAuthor = author;
        ch.LastAt = msg.CreatedAt;
        ch.LastRead[author] = msg.CreatedAt;
        await _channels.UpdateAsync(ch);

        // Solo se notifica en la campana a quien fue mencionado con @ (el resto lo ve por el contador de no leídos)
        var mentioned = await _notifications.FindMentionsAsync(msg.Text);
        var where = ch.Kind == ChatKinds.Group ? $"el grupo \"{ch.Name}\"" : "el chat";
        foreach (var m in mentioned.Where(m => ch.IsMember(m) && !string.Equals(m, author, StringComparison.OrdinalIgnoreCase)))
        {
            try { await _notifications.NotifyAsync(m, NotificationKinds.Mention, $"te mencionó en {where}: {ch.LastText}", "chat", author); } catch { }
        }

        MessageSent?.Invoke(ch.Id!);
        return msg;
    }

    public async Task MarkReadAsync(ChatChannel ch, string user)
    {
        ch.LastRead[user] = DateTime.UtcNow;
        await _channels.UpdateAsync(ch);
    }

    public int UnreadCount(ChatChannel ch, string user)
    {
        if (ch.LastAt is null || string.Equals(ch.LastAuthor, user, StringComparison.OrdinalIgnoreCase)) return 0;
        var read = ch.LastRead.TryGetValue(user, out var t) ? t : DateTime.MinValue;
        return ch.LastAt > read ? 1 : 0;
    }

    public async Task LeaveOrDeleteAsync(ChatChannel ch, string user)
    {
        if (ch.Kind == ChatKinds.Group)
        {
            ch.Members.RemoveAll(m => string.Equals(m, user, StringComparison.OrdinalIgnoreCase));
            if (ch.Members.Count == 0) await _channels.DeleteAsync(ch.Id!, user);
            else await _channels.UpdateAsync(ch);
        }
        else
        {
            await _channels.DeleteAsync(ch.Id!, user);
        }
        MessageSent?.Invoke(ch.Id!);
    }
}
