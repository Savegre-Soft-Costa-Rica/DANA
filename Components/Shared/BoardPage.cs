using Dana.Models;
using Dana.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Dana.Components.Shared;

/// <summary>Lógica común de una página de tablero: configuración, filtros, vistas guardadas, archivar, duplicar.</summary>
public abstract class BoardPage<T> : ComponentBase, IDisposable where T : Entity
{
    [Inject] protected ChangeNotifier Notifier { get; set; } = default!;
    [Inject] protected PermissionService Permissions { get; set; } = default!;
    protected BoardAccess Access { get; private set; } = new(AccessLevels.Edit, true, true, new());
    protected string MeName { get; private set; } = "";
    private System.Threading.Timer? _refreshTimer;
    private DateTime _lastOwnChange;
    [Inject] protected BoardConfigService Configs { get; set; } = default!;
    [Inject] protected UserService Users { get; set; } = default!;
    [Inject] protected DrawerService Drawer { get; set; } = default!;
    [Inject] protected CurrentUser Me { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected DialogService Dialogs { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected abstract string BoardKey { get; }

    protected BoardConfig Config { get; set; } = new();
    protected List<string> People { get; set; } = new();
    protected List<T>? Items { get; set; }
    protected List<BoardColumn<T>> BaseColumns { get; set; } = new();
    protected List<BoardColumn<T>> Columns { get; set; } = new();
    protected List<BoardGroup> Groups { get; set; } = new();

    protected BoardView View { get; set; } = BoardView.Table;
    protected string Search { get; set; } = "";
    protected string Person { get; set; } = "";
    protected List<FilterRuleDef> Filters { get; set; } = new();
    protected bool ShowArchived { get; set; }
    protected string? ActiveViewId { get; set; }

    protected int ArchivedCount => Items?.Count(i => i.IsArchived) ?? 0;
    protected List<FilterColumnInfo> FilterColumns => Columns.Select(c => c.Info).ToList();
    protected List<FilterColumnInfo> HiddenColumns => BaseColumns.Concat(CustomColumns())
        .Where(c => c.Type != ColumnType.Name && Config.HiddenColumns.Contains(c.Key)).Select(c => c.Info).ToList();

    protected IEnumerable<T> Filtered => (Items ?? [])
        .Where(i => Access.CanSee(PersonOf(i)))
        .Where(i => i.IsArchived == ShowArchived)
        .Where(i => string.IsNullOrEmpty(Person) || PersonOf(i) == Person)
        .Where(i => Filters.All(f =>
        {
            var col = Columns.FirstOrDefault(c => c.Key == f.ColumnKey);
            return col is null || col.Matches(i, f);
        }));

    protected virtual string? PersonOf(T item) => Columns.FirstOrDefault(c => c.Type == ColumnType.Person)?.GetValue(item)?.ToString();

    // ----- ciclo de vida -----
    protected override async Task OnInitializedAsync()
    {
        MeName = await Me.NameAsync();
        Access = await Permissions.GetAccessAsync(MeName, BoardKey, await Me.IsAdminAsync());
        if (!Access.CanView) { Nav.NavigateTo("acceso-denegado-tablero"); return; }
        People = (await Users.GetAllAsync()).Where(u => u.IsActive).Select(u => u.Name).OrderBy(n => n).ToList();
        Config = await Configs.GetAsync(BoardKey);
        if (Nav.Uri.Contains("view=kanban")) View = BoardView.Kanban;
        await BuildAsync();
        Items = await LoadItemsAsync();
        Notifier.Changed += OnExternalChange;
    }

    /// <summary>Colección Mongo que alimenta este tablero (para refresco en tiempo real).</summary>
    protected virtual string CollectionName => BoardKey;

    private void OnExternalChange(string collection, string? id)
    {
        if (collection != CollectionName) return;
        if ((DateTime.UtcNow - _lastOwnChange).TotalMilliseconds < 1500) return; // cambio propio: ya está en pantalla
        _refreshTimer?.Dispose();
        _refreshTimer = new System.Threading.Timer(_ => InvokeAsync(ReloadAsync), null, 700, Timeout.Infinite);
    }

    protected void MarkOwnChange() => _lastOwnChange = DateTime.UtcNow;

    public virtual void Dispose()
    {
        Notifier.Changed -= OnExternalChange;
        _refreshTimer?.Dispose();
    }

    /// <summary>Construye BaseColumns y Groups; luego aplica la configuración.</summary>
    protected async Task BuildAsync()
    {
        await BuildBaseColumnsAsync();
        ApplyConfig();
    }

    protected abstract Task BuildBaseColumnsAsync();
    protected abstract Task<List<T>> LoadItemsAsync();
    protected abstract Task SaveItemAsync(T item, BoardColumn<T>? column);
    protected abstract Task DeleteItemAsync(T item);
    protected abstract Task<T> CreateItemAsync(BoardAdd add);
    protected abstract Task MoveItemAsync(BoardMove<T> move);
    protected abstract void OpenItem(T? item);
    protected abstract Task<T> InsertCopyAsync(T copy);

    protected List<BoardColumn<T>> CustomColumns() => Config.CustomColumns.Select(d => BoardColumn<T>.FromCustom(d, People)).ToList();

    /// <summary>Etiquetas efectivas de una columna de estado (redefinidas por el usuario o predeterminadas).</summary>
    protected List<StatusOption> OptionsFor(string columnKey, IEnumerable<StatusOption> defaults)
    {
        var ov = Config.OverridesFor(columnKey);
        return ov is null ? defaults.ToList() : BoardColumn<T>.FromDefs(ov);
    }

    protected void ApplyConfig()
    {
        var all = BaseColumns.Concat(CustomColumns()).ToList();
        foreach (var c in all.Where(c => c.Type is ColumnType.Status or ColumnType.Select && !c.IsCustom))
        {
            var ov = Config.OverridesFor(c.Key);
            if (ov is not null) c.Options = BoardColumn<T>.FromDefs(ov);
        }
        if (!Access.CanEdit) foreach (var c in all) { c.SetValue = null; }
        var visible = all.Where(c => c.Type == ColumnType.Name || !Config.HiddenColumns.Contains(c.Key)).ToList();
        var order = Config.ColumnOrder;
        Columns = visible
            .OrderBy(c => c.Type == ColumnType.Name ? -1 : 0)
            .ThenBy(c => { var i = order.IndexOf(c.Key); return i < 0 ? int.MaxValue : i; })
            .ThenBy(c => all.IndexOf(c))
            .ToList();
    }

    public async Task ConfigChangedAsync()
    {
        await Configs.SaveAsync(Config);
        await BuildAsync();
        StateHasChanged();
    }

    public async Task ReloadAsync()
    {
        Items = await LoadItemsAsync();
        StateHasChanged();
    }

    // ----- eventos del tablero -----
    protected Task OnChangedAsync(BoardChange<T> c) { MarkOwnChange(); return SaveItemAsync(c.Item, c.Column); }
    protected Task OnMoveAsync(BoardMove<T> m) { MarkOwnChange(); return MoveItemAsync(m); }

    protected async Task OnAddAsync(BoardAdd a)
    {
        MarkOwnChange();
        var item = await CreateItemAsync(a);
        Items!.Insert(0, item);
    }

    protected async Task OnDeleteAsync(T item)
    {
        MarkOwnChange();
        await DeleteItemAsync(item);
        Items!.Remove(item);
    }

    protected async Task OnArchiveAsync(T item)
    {
        MarkOwnChange();
        item.IsArchived = !item.IsArchived;
        await SaveItemAsync(item, null);
    }

    protected async Task OnDuplicateAsync(T item)
    {
        MarkOwnChange();
        var copy = Entity.Clone(item);
        copy.IsArchived = false;
        copy.CreatedBy = await Me.NameAsync();
        var created = await InsertCopyAsync(copy);
        Items!.Insert(Items.IndexOf(item) + 1, created);
    }

    protected void Open(T item) => OpenItem(item);
    protected void New() => OpenItem(null);

    // ----- vistas guardadas -----
    protected void SelectView(string? id)
    {
        ActiveViewId = id;
        var v = Config.Views.FirstOrDefault(x => x.Id == id);
        if (v is null) { Filters = new(); Search = ""; Person = ""; ShowArchived = false; View = BoardView.Table; return; }
        View = v.View == "Kanban" ? BoardView.Kanban : BoardView.Table;
        Search = v.Search ?? "";
        Person = v.Person ?? "";
        ShowArchived = v.ShowArchived;
        Filters = v.Filters.Select(f => new FilterRuleDef { ColumnKey = f.ColumnKey, Op = f.Op, Value = f.Value }).ToList();
    }

    protected async Task SaveViewAsync()
    {
        var existing = Config.Views.FirstOrDefault(x => x.Id == ActiveViewId);
        var name = await Dialogs.PromptAsync("Nombre de la vista:", existing?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var v = existing ?? new SavedView();
        v.Name = name.Trim();
        v.View = View.ToString();
        v.Search = Search;
        v.Person = Person;
        v.ShowArchived = ShowArchived;
        v.Filters = Filters.Select(f => new FilterRuleDef { ColumnKey = f.ColumnKey, Op = f.Op, Value = f.Value }).ToList();
        if (existing is null) Config.Views.Add(v);
        ActiveViewId = v.Id;
        await Configs.SaveAsync(Config);
    }

    protected async Task DeleteViewAsync(string id)
    {
        var v = Config.Views.FirstOrDefault(x => x.Id == id);
        if (v is null) return;
        if (!await Dialogs.ConfirmAsync($"¿Eliminar la vista \"{v.Name}\"?")) return;
        Config.Views.Remove(v);
        if (ActiveViewId == id) SelectView(null);
        await Configs.SaveAsync(Config);
    }

    protected async Task ShowColumnAsync(string key)
    {
        Config.HiddenColumns.Remove(key);
        await ConfigChangedAsync();
    }
}
