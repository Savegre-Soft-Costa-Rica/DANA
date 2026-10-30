using System.Collections.Concurrent;

namespace Dana.Services.Ai;

public record Presence(string Id, string Name, string Color, int Avatar, double X, double Y, string Dir, bool Moving, DateTime At);

/// <summary>Presencia en la oficina virtual: posición de cada usuario conectado (en memoria, por circuito).</summary>
public class OfficeService
{
    private readonly ConcurrentDictionary<string, Presence> _people = new();
    public event Action? Changed;

    public void Update(string id, string name, string color, int avatar, double x, double y, string dir, bool moving)
    {
        _people[id] = new Presence(id, name, color, avatar, x, y, dir, moving, DateTime.UtcNow);
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        if (_people.TryRemove(id, out _)) Changed?.Invoke();
    }

    public List<Presence> All()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        foreach (var k in _people.Where(p => p.Value.At < cutoff).Select(p => p.Key).ToList()) _people.TryRemove(k, out _);
        return _people.Values.ToList();
    }
}
