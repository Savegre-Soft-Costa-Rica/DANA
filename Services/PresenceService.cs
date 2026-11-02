namespace Dana.Services;

/// <summary>Presencia en línea en memoria (Blazor Server). Cada circuito activo late periódicamente;
/// un usuario se considera "en línea" si se le vio en los últimos ~75 s.</summary>
public class PresenceService
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(75);
    private readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event Action? Changed;

    public void Heartbeat(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        bool changed;
        lock (_lock)
        {
            changed = !_seen.TryGetValue(user, out var t) || !Fresh(t);
            _seen[user] = DateTime.UtcNow;
        }
        if (changed) Notify();
    }

    public void Offline(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        bool removed;
        lock (_lock) { removed = _seen.Remove(user); }
        if (removed) Notify();
    }

    public bool IsOnline(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        lock (_lock) { return _seen.TryGetValue(user, out var t) && Fresh(t); }
    }

    public int OnlineCount()
    {
        lock (_lock) { return _seen.Values.Count(Fresh); }
    }

    private static bool Fresh(DateTime t) => DateTime.UtcNow - t < Window;
    private void Notify() { try { Changed?.Invoke(); } catch { } }
}
