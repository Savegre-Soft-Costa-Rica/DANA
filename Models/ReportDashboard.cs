using MongoDB.Bson;

namespace Dana.Models;

public static class WidgetTypes
{
    public const string Number = "number";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Donut = "donut";
    public const string Funnel = "funnel";
    public const string Table = "table";
    public const string Forecast = "forecast";
    public const string Leaderboard = "leaderboard";
    public const string StageTime = "stage_time";
    public const string Conversion = "conversion";

    public static readonly (string Id, string Label, string Hint)[] All =
    [
        (Number, "Número", "Un total grande (cantidad o importe)"),
        (Bar, "Barras", "Comparar categorías"),
        (Line, "Línea", "Evolución en el tiempo (por mes)"),
        (Donut, "Dona", "Proporción por categoría"),
        (Funnel, "Embudo", "Negociaciones por etapa"),
        (Table, "Tabla", "Valores por categoría en una tabla"),
        (Forecast, "Previsión de ventas", "Importe × probabilidad por mes de cierre"),
        (Leaderboard, "Rendimiento por vendedor", "Ganado, abierto y perdido por responsable"),
        (StageTime, "Tiempo por etapa", "Días promedio que pasa una negociación en cada etapa"),
        (Conversion, "Conversión de prospectos", "Convertidos vs. descartados por fuente"),
    ];
}

public static class ReportGroups
{
    public static readonly (string Id, string Label, string[] Boards)[] All =
    [
        ("stage", "Etapa", ["deals"]), ("status", "Estado", ["leads"]), ("assignee", "Responsable", ["deals", "leads", "contacts", "companies"]),
        ("source", "Fuente", ["deals", "leads", "contacts"]), ("type", "Tipo", ["deals", "contacts", "companies"]), ("month", "Mes de creación", ["deals", "leads", "contacts", "companies"]),
        ("closemonth", "Mes de cierre previsto", ["deals"]), ("pipeline", "Embudo", ["deals"]), ("industry", "Industria", ["companies"]), ("company", "Compañía", ["deals", "contacts"]),
    ];
}

public class ReportWidget
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Title { get; set; } = "";
    public string Type { get; set; } = WidgetTypes.Bar;
    public string Board { get; set; } = "deals";          // deals | leads | contacts | companies
    public string Metric { get; set; } = "count";         // count | amount | weighted
    public string GroupBy { get; set; } = "stage";
    public string Period { get; set; } = "all";           // 30d | 90d | year | all
    public string? Person { get; set; }
    public string Scope { get; set; } = "all";            // all | open | won | lost (deals)
    public int Width { get; set; } = 6;                   // 3,4,6,8,12
}

public class ReportDashboard : Entity
{
    public string Name { get; set; } = "Panel de informes";
    public List<ReportWidget> Widgets { get; set; } = new();

    public static ReportDashboard CreateDefault() => new()
    {
        Widgets =
        [
            new() { Title = "Importe abierto", Type = WidgetTypes.Number, Board = "deals", Metric = "amount", Scope = "open", Width = 3 },
            new() { Title = "Ganado este año", Type = WidgetTypes.Number, Board = "deals", Metric = "amount", Scope = "won", Period = "year", Width = 3 },
            new() { Title = "Prospectos activos", Type = WidgetTypes.Number, Board = "leads", Metric = "count", Scope = "open", Width = 3 },
            new() { Title = "Previsión ponderada", Type = WidgetTypes.Number, Board = "deals", Metric = "weighted", Scope = "open", Width = 3 },
            new() { Title = "Embudo de ventas", Type = WidgetTypes.Funnel, Board = "deals", Metric = "amount", GroupBy = "stage", Width = 6 },
            new() { Title = "Previsión de ventas", Type = WidgetTypes.Forecast, Board = "deals", Width = 6 },
            new() { Title = "Rendimiento por vendedor", Type = WidgetTypes.Leaderboard, Board = "deals", Period = "year", Width = 6 },
            new() { Title = "Negociaciones creadas por mes", Type = WidgetTypes.Line, Board = "deals", Metric = "count", GroupBy = "month", Period = "year", Width = 6 },
            new() { Title = "Prospectos por fuente", Type = WidgetTypes.Donut, Board = "leads", Metric = "count", GroupBy = "source", Width = 4 },
            new() { Title = "Tiempo por etapa", Type = WidgetTypes.StageTime, Board = "deals", Width = 4 },
            new() { Title = "Conversión de prospectos", Type = WidgetTypes.Conversion, Board = "leads", Period = "year", Width = 4 },
        ]
    };
}

/// <summary>Resultado calculado de un widget.</summary>
public class ChartData
{
    public List<string> Labels { get; set; } = new();
    public List<ChartSeries> Series { get; set; } = new();
    public decimal Total { get; set; }
    public string Format { get; set; } = "N0";   // N0 | N2 | P0
    public string? Subtitle { get; set; }
    public List<string[]>? Rows { get; set; }     // para tablas
    public List<string>? Columns { get; set; }
}

public class ChartSeries
{
    public string Name { get; set; } = "";
    public List<decimal> Values { get; set; } = new();
    public List<string>? Colors { get; set; }  // color por punto (cuando el color sigue a la entidad: etapas, estados)
    public string Color { get; set; } = "#2a78d6";
}
