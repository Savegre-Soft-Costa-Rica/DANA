using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class NotificationService
{
    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<User> _users;

    public NotificationService(IRepository<Notification> notifications, IRepository<User> users)
    {
        _notifications = notifications;
        _users = users;
    }

    public Task<Notification> NotifyAsync(string recipient, string kind, string text, string? link, string? actor) =>
        _notifications.InsertAsync(new Notification { Recipient = recipient, Kind = kind, Text = text, Link = link, Actor = actor });

    public Task<List<Notification>> GetForAsync(string recipient, int limit = 30) =>
        _notifications.Collection.Find(n => n.Recipient == recipient).SortByDescending(n => n.CreatedAt).Limit(limit).ToListAsync();

    public Task<long> CountUnreadAsync(string recipient) => _notifications.CountAsync(n => n.Recipient == recipient && !n.IsRead);

    public Task MarkReadAsync(string id) =>
        _notifications.Collection.UpdateOneAsync(n => n.Id == id, Builders<Notification>.Update.Set(n => n.IsRead, true));

    public Task MarkAllReadAsync(string recipient) =>
        _notifications.Collection.UpdateManyAsync(n => n.Recipient == recipient && !n.IsRead, Builders<Notification>.Update.Set(n => n.IsRead, true));

    /// <summary>Detecta menciones "@Nombre Apellido" en un texto (nombres de usuarios activos).</summary>
    public async Task<List<string>> FindMentionsAsync(string text)
    {
        if (!text.Contains('@')) return new();
        var names = (await _users.GetAllAsync()).Where(u => u.IsActive).Select(u => u.Name).OrderByDescending(n => n.Length).ToList();
        return names.Where(n => text.Contains("@" + n, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task NotifyMentionsAsync(string text, string actor, string entityTitle, string link)
    {
        foreach (var name in await FindMentionsAsync(text))
        {
            if (string.Equals(name, actor, StringComparison.OrdinalIgnoreCase)) continue;
            await NotifyAsync(name, NotificationKinds.Mention, $"te mencionó en \"{entityTitle}\"", link, actor);
        }
    }
}
