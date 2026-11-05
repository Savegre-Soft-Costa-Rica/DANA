namespace Dana.Services;

/// <summary>Suscribe una página a los cambios de ciertas colecciones de Mongo y dispara un refresco
/// con rebote cuando otra persona (u otra pestaña) guarda algo en la base de datos. Ignora los cambios
/// propios recientes para no recargar sobre lo que ya está en pantalla, y respeta una condición opcional
/// (<paramref name="canRefresh"/>) para no interrumpir al usuario mientras arrastra o escribe.
/// Pensado para páginas que no heredan de <see cref="Dana.Components.Shared.BoardPage{T}"/>, que ya trae su propio refresco.</summary>
public sealed class RealtimeRefresh : IDisposable
{
    private readonly ChangeNotifier _notifier;
    private readonly HashSet<string> _collections;
    private readonly Func<Func<Task>, Task> _invoke;
    private readonly Func<Task> _reload;
    private readonly Func<bool>? _canRefresh;
    private System.Threading.Timer? _timer;
    private DateTime _lastOwnChange;

    /// <param name="notifier">Bus de cambios en memoria (singleton).</param>
    /// <param name="invoke">El <c>InvokeAsync</c> del componente, para volver a su hilo de render.</param>
    /// <param name="reload">Recarga los datos y vuelve a renderizar.</param>
    /// <param name="canRefresh">Si devuelve false, se omite el refresco (p. ej. mientras se arrastra o escribe).</param>
    /// <param name="collections">Nombres de las colecciones Mongo que alimentan la página.</param>
    public RealtimeRefresh(ChangeNotifier notifier, Func<Func<Task>, Task> invoke, Func<Task> reload,
        Func<bool>? canRefresh, params string[] collections)
    {
        _notifier = notifier;
        _invoke = invoke;
        _reload = reload;
        _canRefresh = canRefresh;
        _collections = new HashSet<string>(collections, StringComparer.Ordinal);
        _notifier.Changed += OnChanged;
    }

    /// <summary>Marca un cambio hecho por el propio usuario para no recargar encima de él.</summary>
    public void MarkOwnChange() => _lastOwnChange = DateTime.UtcNow;

    private void OnChanged(string collection, string? id)
    {
        if (!_collections.Contains(collection)) return;
        if ((DateTime.UtcNow - _lastOwnChange).TotalMilliseconds < 1500) return; // cambio propio: ya está en pantalla
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = Fire(), null, 700, System.Threading.Timeout.Infinite);
    }

    private Task Fire() => _invoke(async () =>
    {
        if (_canRefresh is not null && !_canRefresh()) return; // no interrumpir al usuario
        await _reload();
    });

    public void Dispose()
    {
        _notifier.Changed -= OnChanged;
        _timer?.Dispose();
    }
}
