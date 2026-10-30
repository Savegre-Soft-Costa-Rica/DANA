using MongoDB.Bson;

namespace Dana.Models;

public class StatusOptionDef
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#579bfc";
}

public class CustomColumnDef
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Title { get; set; } = "";
    public string Type { get; set; } = "Text";   // nombre de ColumnType
    public int Width { get; set; } = 150;
    public List<StatusOptionDef> Options { get; set; } = new();
}

public class FilterRuleDef
{
    public string ColumnKey { get; set; } = "";
    public string Op { get; set; } = "contains";
    public string? Value { get; set; }
}

public class SavedView
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = "";
    public string View { get; set; } = "Table";
    public string? Search { get; set; }
    public string? Person { get; set; }
    public List<FilterRuleDef> Filters { get; set; } = new();
    public string? SortKey { get; set; }
    public bool SortDesc { get; set; }
    public bool ShowArchived { get; set; }
}

/// <summary>Configuración de un tablero (leads, deals, contacts, companies): columnas, etiquetas, vistas.</summary>
public class BoardConfig : Entity
{
    public string BoardKey { get; set; } = "";
    public List<CustomColumnDef> CustomColumns { get; set; } = new();
    public List<string> ColumnOrder { get; set; } = new();
    public List<string> HiddenColumns { get; set; } = new();
    /// <summary>Etiquetas (opciones) redefinidas por columna de estado/selección.</summary>
    public Dictionary<string, List<StatusOptionDef>> LabelOverrides { get; set; } = new();
    public List<SavedView> Views { get; set; } = new();

    public List<StatusOptionDef>? OverridesFor(string columnKey) =>
        LabelOverrides.TryGetValue(columnKey, out var list) && list.Count > 0 ? list : null;
}
