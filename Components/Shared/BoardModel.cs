using System.Globalization;
using Dana.Models;

namespace Dana.Components.Shared;

public enum ColumnType { Name, Text, LongText, Number, Money, Date, Status, Select, Person, Link, Email, Phone, Checkbox }

public enum BoardView { Table, Kanban }

public record StatusOption(string Value, string Label, string Color);

public static class ColumnTypes
{
    public static readonly (ColumnType Type, string Label)[] Creatable =
    [
        (ColumnType.Text, "Texto"), (ColumnType.LongText, "Texto largo"), (ColumnType.Number, "Número"), (ColumnType.Money, "Dinero"),
        (ColumnType.Date, "Fecha"), (ColumnType.Status, "Estado"), (ColumnType.Select, "Desplegable"), (ColumnType.Person, "Persona"),
        (ColumnType.Checkbox, "Casilla"), (ColumnType.Link, "Enlace"), (ColumnType.Email, "Correo"), (ColumnType.Phone, "Teléfono"),
    ];
}

public class BoardGroup
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Color { get; set; } = "#0073ea";
}

public class BoardChange<T> where T : Entity { public T Item { get; set; } = default!; public BoardColumn<T> Column { get; set; } = default!; }
public class BoardMove<T> where T : Entity { public T Item { get; set; } = default!; public BoardGroup Group { get; set; } = default!; }
public class BoardAdd { public BoardGroup Group { get; set; } = default!; public string Title { get; set; } = ""; }

public record FilterColumnInfo(string Key, string Title, ColumnType Type, List<StatusOption> Options);

public static class FilterOps
{
    public static readonly (string Op, string Label)[] All =
    [
        ("contains", "contiene"), ("notcontains", "no contiene"), ("is", "es"), ("isnot", "no es"),
        ("empty", "está vacío"), ("notempty", "no está vacío"), ("gt", "mayor que"), ("lt", "menor que"),
    ];
    public static bool NeedsValue(string op) => op is not ("empty" or "notempty");
}

/// <summary>Columna de un tablero: cómo leer/escribir el valor de un elemento y cómo mostrarlo.</summary>
public class BoardColumn<T> where T : Entity
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public ColumnType Type { get; set; }
    public int Width { get; set; } = 150;
    public string? Format { get; set; }
    public Func<T, object?> GetValue { get; set; } = _ => null;
    public Action<T, object?>? SetValue { get; set; }
    public List<StatusOption> Options { get; set; } = new();
    public bool ShowInCard { get; set; } = true;
    public bool ReadOnly => SetValue is null;

    /// <summary>Columna creada por el usuario (se puede renombrar/eliminar).</summary>
    public bool IsCustom { get; set; }
    /// <summary>Permite "Editar etiquetas" (estado/desplegable).</summary>
    public bool LabelsEditable { get; set; }
    /// <summary>Valores que no se pueden eliminar al editar etiquetas (tienen semántica en el sistema).</summary>
    public HashSet<string> LockedValues { get; set; } = new();
    public string? CustomId { get; set; }

    public static readonly string[] LabelPalette =
        ["#579bfc", "#9cd326", "#ff7575", "#ffcb00", "#cab641", "#66ccff", "#ff642e", "#7f5347", "#c4c4c4", "#784bd1", "#00c875", "#bb3354"];

    public static List<StatusOption> Labels(IEnumerable<string> values) =>
        values.Select((v, i) => new StatusOption(v, v, LabelPalette[i % LabelPalette.Length])).ToList();

    public static List<StatusOption> FromDefs(IEnumerable<StatusOptionDef> defs) => defs.Select(d => new StatusOption(d.Value, d.Label, d.Color)).ToList();

    public static BoardColumn<T> Name(string title, Func<T, string?> get, Action<T, string>? set) => new()
    {
        Key = "__name", Title = title, Type = ColumnType.Name,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, v?.ToString() ?? "")
    };

    public static BoardColumn<T> Text(string key, string title, Func<T, string?> get, Action<T, string?>? set = null, int width = 160, ColumnType type = ColumnType.Text) => new()
    {
        Key = key, Title = title, Type = type, Width = width,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, Empty(v))
    };

    public static BoardColumn<T> Email(string key, string title, Func<T, string?> get, Action<T, string?>? set = null, int width = 190) => Text(key, title, get, set, width, ColumnType.Email);
    public static BoardColumn<T> Phone(string key, string title, Func<T, string?> get, Action<T, string?>? set = null, int width = 140) => Text(key, title, get, set, width, ColumnType.Phone);
    public static BoardColumn<T> Link(string key, string title, Func<T, string?> get, Action<T, string?>? set = null, int width = 170) => Text(key, title, get, set, width, ColumnType.Link);
    public static BoardColumn<T> LongText(string key, string title, Func<T, string?> get, Action<T, string?>? set = null, int width = 220) => Text(key, title, get, set, width, ColumnType.LongText);

    public static BoardColumn<T> Number(string key, string title, Func<T, decimal?> get, Action<T, decimal?>? set = null, string format = "N0", int width = 110) => new()
    {
        Key = key, Title = title, Type = ColumnType.Number, Width = width, Format = format,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, ToDecimal(v))
    };

    public static BoardColumn<T> Money(string key, string title, Func<T, decimal?> get, Action<T, decimal?>? set = null, int width = 130) => new()
    {
        Key = key, Title = title, Type = ColumnType.Money, Width = width, Format = "N2",
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, ToDecimal(v))
    };

    public static BoardColumn<T> Date(string key, string title, Func<T, DateTime?> get, Action<T, DateTime?>? set = null, int width = 130) => new()
    {
        Key = key, Title = title, Type = ColumnType.Date, Width = width,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, ToDate(v))
    };

    public static BoardColumn<T> Status(string key, string title, Func<T, string?> get, Action<T, string?>? set, IEnumerable<StatusOption> options, int width = 150, bool labelsEditable = true) => new()
    {
        Key = key, Title = title, Type = ColumnType.Status, Width = width, Options = options.ToList(), LabelsEditable = labelsEditable,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, Empty(v))
    };

    public static BoardColumn<T> Select(string key, string title, Func<T, string?> get, Action<T, string?>? set, IEnumerable<StatusOption> options, int width = 160, bool labelsEditable = true) => new()
    {
        Key = key, Title = title, Type = ColumnType.Select, Width = width, Options = options.ToList(), LabelsEditable = labelsEditable,
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, Empty(v))
    };

    public static BoardColumn<T> Person(string key, string title, Func<T, string?> get, Action<T, string?>? set, IEnumerable<string> people, int width = 150) => new()
    {
        Key = key, Title = title, Type = ColumnType.Person, Width = width,
        Options = people.Select(p => new StatusOption(p, p, Avatar.ColorFor(p))).ToList(),
        GetValue = x => get(x), SetValue = set is null ? null : (x, v) => set(x, Empty(v))
    };

    /// <summary>Columna personalizada definida en la configuración del tablero; el valor vive en Entity.Custom.</summary>
    public static BoardColumn<T> FromCustom(CustomColumnDef def, IEnumerable<string> people)
    {
        var type = Enum.TryParse<ColumnType>(def.Type, out var t) && t != ColumnType.Name ? t : ColumnType.Text;
        var col = new BoardColumn<T>
        {
            Key = "c:" + def.Id, CustomId = def.Id, Title = def.Title, Type = type, Width = def.Width > 0 ? def.Width : 150,
            IsCustom = true, LabelsEditable = type is ColumnType.Status or ColumnType.Select,
            Format = type == ColumnType.Money ? "N2" : "N0",
            Options = type == ColumnType.Person ? people.Select(p => new StatusOption(p, p, Avatar.ColorFor(p))).ToList() : FromDefs(def.Options),
        };
        col.GetValue = x => ParseCustom(type, x.Custom.GetValueOrDefault(def.Id));
        col.SetValue = (x, v) => x.Custom[def.Id] = SerializeCustom(type, v);
        return col;
    }

    private static object? ParseCustom(ColumnType type, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return type == ColumnType.Checkbox ? false : null;
        return type switch
        {
            ColumnType.Number or ColumnType.Money => decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            ColumnType.Date => DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null,
            ColumnType.Checkbox => raw == "true",
            _ => raw
        };
    }

    private static string? SerializeCustom(ColumnType type, object? v) => v switch
    {
        null => null,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        bool b => b ? "true" : "false",
        _ => string.IsNullOrWhiteSpace(v.ToString()) ? null : v.ToString()
    };

    public StatusOption? OptionFor(object? value)
    {
        var s = value?.ToString();
        return string.IsNullOrEmpty(s) ? null : Options.FirstOrDefault(o => o.Value == s);
    }

    public FilterColumnInfo Info => new(Key, Title, Type, Options);

    /// <summary>Texto para mostrar (tarjetas Kanban, búsqueda, ordenación).</summary>
    public string Display(T item)
    {
        var v = GetValue(item);
        if (v is null) return "";
        return Type switch
        {
            ColumnType.Number or ColumnType.Money => v is decimal d ? d.ToString(Format ?? "N0", CultureInfo.InvariantCulture) : v.ToString() ?? "",
            ColumnType.Date => v is DateTime dt ? dt.ToString("dd/MM/yyyy") : "",
            ColumnType.Status or ColumnType.Select => OptionFor(v)?.Label ?? v.ToString() ?? "",
            ColumnType.Checkbox => v is true ? "✓" : "",
            _ => v.ToString() ?? ""
        };
    }

    public string EditText(T item)
    {
        var v = GetValue(item);
        return Type switch
        {
            ColumnType.Date => v is DateTime dt ? dt.ToString("yyyy-MM-dd") : "",
            ColumnType.Number or ColumnType.Money => v is decimal d ? d.ToString(CultureInfo.InvariantCulture) : "",
            _ => v?.ToString() ?? ""
        };
    }

    public void SetFromText(T item, string text)
    {
        if (SetValue is null) return;
        object? v = Type switch
        {
            ColumnType.Date => string.IsNullOrWhiteSpace(text) ? null : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt.Date : null,
            ColumnType.Number or ColumnType.Money => string.IsNullOrWhiteSpace(text) ? null : decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => text
        };
        SetValue(item, v);
    }

    /// <summary>Evalúa una regla de filtro sobre un elemento.</summary>
    public bool Matches(T item, FilterRuleDef rule)
    {
        var raw = GetValue(item);
        var text = Display(item);
        var val = rule.Value ?? "";
        var rawText = raw?.ToString() ?? "";
        switch (rule.Op)
        {
            case "empty": return string.IsNullOrEmpty(text) || raw is false;
            case "notempty": return !(string.IsNullOrEmpty(text) || raw is false);
            case "is":
                return Type is ColumnType.Status or ColumnType.Select or ColumnType.Person ? rawText == val
                     : Type == ColumnType.Checkbox ? (raw is true) == (val == "true")
                     : text.Equals(val, StringComparison.OrdinalIgnoreCase) || rawText.Equals(val, StringComparison.OrdinalIgnoreCase);
            case "isnot": return !Matches(item, new FilterRuleDef { ColumnKey = rule.ColumnKey, Op = "is", Value = rule.Value });
            case "contains": return text.Contains(val, StringComparison.OrdinalIgnoreCase);
            case "notcontains": return !text.Contains(val, StringComparison.OrdinalIgnoreCase);
            case "gt":
            case "lt":
                if (raw is decimal d && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return rule.Op == "gt" ? d > n : d < n;
                if (raw is DateTime dt && DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dv)) return rule.Op == "gt" ? dt > dv : dt < dv;
                return false;
            default: return true;
        }
    }

    private static string? Empty(object? v) => string.IsNullOrWhiteSpace(v?.ToString()) ? null : v!.ToString();
    private static decimal? ToDecimal(object? v) => v switch { null => null, decimal d => d, int i => i, double x => (decimal)x, string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r, _ => null };
    private static DateTime? ToDate(object? v) => v switch { DateTime d => d, string s when DateTime.TryParse(s, out var r) => r, _ => null };
}
