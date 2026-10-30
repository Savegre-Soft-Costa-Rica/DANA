namespace Dana.Services;

/// <summary>Panel lateral (drawer) para abrir un elemento sin salir del tablero, como en monday.</summary>
public class DrawerService
{
    public Type? ComponentType { get; private set; }
    public Dictionary<string, object?> Parameters { get; private set; } = new();
    public string? Title { get; private set; }
    public bool IsOpen => ComponentType is not null;

    private Func<Task>? _onChanged;
    private Func<Task>? _onClosed;

    public event Action? StateChanged;

    public void Open<TComponent>(string? id, string? title = null, Func<Task>? onChanged = null, Func<Task>? onClosed = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        ComponentType = typeof(TComponent);
        Parameters = new Dictionary<string, object?> { ["Id"] = id, ["Embedded"] = true };
        Title = title;
        _onChanged = onChanged;
        _onClosed = onClosed;
        StateChanged?.Invoke();
    }

    public async Task NotifyChangedAsync()
    {
        if (_onChanged is not null) await _onChanged();
    }

    public async Task CloseAsync()
    {
        if (!IsOpen) return;
        ComponentType = null;
        Parameters = new();
        StateChanged?.Invoke();
        if (_onClosed is not null) await _onClosed();
        _onClosed = null;
        _onChanged = null;
    }
}
