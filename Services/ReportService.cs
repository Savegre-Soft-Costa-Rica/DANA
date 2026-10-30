using System.Globalization;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class ReportService
{
    // Paleta categórica validada (orden fijo, nunca cíclica)
    public static readonly string[] Palette = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];
    private static readonly CultureInfo Es = new("es-ES");

    private readonly IRepository<ReportDashboard> _dashboards;
    private readonly IRepository<Deal> _deals;
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly PipelineService _pipelines;
    private readonly SettingsService _settings;

    public ReportService(IRepository<ReportDashboard> dashboards, IRepository<Deal> deals, IRepository<Lead> leads,
        IRepository<Contact> contacts, IRepository<Company> companies, PipelineService pipelines, SettingsService settings)
    {
        _dashboards = dashboards; _deals = deals; _leads = leads; _contacts = contacts; _companies = companies; _pipelines = pipelines; _settings = settings;
    }

    /// <summary>Importe convertido a la moneda base configurada.</summary>
    public decimal Base(decimal amount, string? currency) => _settings.Current.ToBase(amount, currency);

    public async Task<ReportDashboard> GetDashboardAsync()
    {
        var d = (await _dashboards.GetAllAsync()).FirstOrDefault();
        if (d is not null) return d;
        return await _dashboards.InsertAsync(ReportDashboard.CreateDefault());
    }

    public Task SaveAsync(ReportDashboard d) => _dashboards.UpdateAsync(d);

    public async Task<ReportDashboard> ResetAsync(ReportDashboard d)
    {
        d.Widgets = ReportDashboard.CreateDefault().Widgets;
        await _dashboards.UpdateAsync(d);
        return d;
    }

    // ---------- datos en memoria para una pasada de cálculo ----------
    public class Snapshot
    {
        public List<Deal> Deals = new(); public List<Lead> Leads = new(); public List<Contact> Contacts = new(); public List<Company> Companies = new();
        public Dictionary<string, Pipeline> Pipelines = new(); public Dictionary<string, string> CompanyNames = new();
        public Stage? StageOf(Deal d) => Pipelines.GetValueOrDefault(d.PipelineId)?.GetStage(d.StageId);
        public StageKind KindOf(Deal d) => StageOf(d)?.Kind ?? StageKind.Process;
    }

    public async Task<Snapshot> LoadAsync()
    {
        var s = new Snapshot
        {
            Deals = (await _deals.GetAllAsync()).Where(x => !x.IsArchived).ToList(),
            Leads = (await _leads.GetAllAsync()).Where(x => !x.IsArchived).ToList(),
            Contacts = (await _contacts.GetAllAsync()).Where(x => !x.IsArchived).ToList(),
            Companies = (await _companies.GetAllAsync()).Where(x => !x.IsArchived).ToList(),
            Pipelines = (await _pipelines.GetAllAsync()).ToDictionary(p => p.Id!)
        };
        s.CompanyNames = s.Companies.ToDictionary(c => c.Id!, c => c.Title);
        return s;
    }

    private static DateTime? PeriodStart(string period) => period switch
    {
        "30d" => DateTime.UtcNow.AddDays(-30), "90d" => DateTime.UtcNow.AddDays(-90),
        "year" => new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), _ => null
    };

    public ChartData Compute(ReportWidget w, Snapshot s)
    {
        var from = PeriodStart(w.Period);
        return w.Type switch
        {
            WidgetTypes.Forecast => Forecast(w, s),
            WidgetTypes.Leaderboard => Leaderboard(w, s, from),
            WidgetTypes.StageTime => StageTime(w, s, from),
            WidgetTypes.Conversion => Conversion(w, s, from),
            WidgetTypes.Funnel => Funnel(w, s, from),
            _ => Grouped(w, s, from)
        };
    }

    // ----- selección de elementos según tablero, periodo, persona, alcance -----
    private IEnumerable<Entity> Items(ReportWidget w, Snapshot s, DateTime? from)
    {
        IEnumerable<Entity> items = w.Board switch
        {
            "leads" => s.Leads.Where(l => w.Scope != "open" || !LeadStatuses.Get(l.Status).IsFinal),
            "contacts" => s.Contacts,
            "companies" => s.Companies,
            _ => s.Deals.Where(d => w.Scope switch { "open" => s.KindOf(d) == StageKind.Process, "won" => s.KindOf(d) == StageKind.Success, "lost" => s.KindOf(d) == StageKind.Failure, _ => true })
        };
        if (from is not null)
            items = items.Where(i => (i is Deal d && w.Scope is "won" or "lost" ? d.ClosedAt ?? d.CreatedAt : i.CreatedAt) >= from);
        if (!string.IsNullOrEmpty(w.Person))
            items = items.Where(i => CrmEntities.AssigneeOf(i) == w.Person);
        return items;
    }

    private decimal Value(ReportWidget w, Entity e) => w.Metric switch
    {
        "amount" => e is Deal d ? Base(d.Amount, d.Currency) : e is Lead l ? Base(l.Opportunity, l.Currency) : 0,
        "weighted" => e is Deal d ? Base(d.Amount, d.Currency) * d.Probability / 100m : e is Lead l ? Base(l.Opportunity, l.Currency) : 0,
        _ => 1
    };

    private (string Key, string Label, string? Color) GroupOf(ReportWidget w, Entity e, Snapshot s)
    {
        switch (w.GroupBy)
        {
            case "stage" when e is Deal d:
                var st = s.StageOf(d); return (d.StageId, st?.Name ?? "—", st?.Color);
            case "status" when e is Lead l:
                var ls = LeadStatuses.Get(l.Status); return (ls.Id, ls.Name, ls.Color);
            case "assignee": var a = CrmEntities.AssigneeOf(e); return (a ?? "", a ?? "Sin asignar", null);
            case "source": var src = e switch { Deal d => d.Source, Lead l => l.Source, Contact c => c.Source, _ => null }; return (src ?? "", src ?? "Sin fuente", null);
            case "type": var t = e switch { Deal d => d.Type, Contact c => c.Type, Company k => k.Type, _ => null }; return (t ?? "", t ?? "Sin tipo", null);
            case "industry": var ind = (e as Company)?.Industry; return (ind ?? "", ind ?? "Sin industria", null);
            case "pipeline" when e is Deal d: return (d.PipelineId, s.Pipelines.GetValueOrDefault(d.PipelineId)?.Name ?? "—", null);
            case "company": var cid = e switch { Deal d => d.CompanyId, Contact c => c.CompanyId, _ => null }; return (cid ?? "", cid is null ? "Sin compañía" : s.CompanyNames.GetValueOrDefault(cid, "—"), null);
            case "closemonth" when e is Deal d: return MonthKey(d.ExpectedCloseDate);
            default: return MonthKey(e.CreatedAt);
        }
    }

    private static (string, string, string?) MonthKey(DateTime? dt) => dt is null ? ("", "Sin fecha", null) : (dt.Value.ToString("yyyy-MM"), dt.Value.ToString("MMM yyyy", Es), null);

    private ChartData Grouped(ReportWidget w, Snapshot s, DateTime? from)
    {
        var items = Items(w, s, from).ToList();
        var data = new ChartData { Format = w.Metric == "count" ? "N0" : "N0", Total = items.Sum(i => Value(w, i)) };
        if (w.Type == WidgetTypes.Number) { data.Subtitle = $"{items.Count} elemento(s)"; return data; }

        var groups = items.GroupBy(i => GroupOf(w, i, s)).Select(g => (g.Key.Key, g.Key.Label, g.Key.Color, Value: g.Sum(i => Value(w, i)), Count: g.Count())).ToList();
        groups = w.GroupBy is "month" or "closemonth" ? groups.OrderBy(g => g.Key).ToList()
               : w.GroupBy == "stage" ? groups.OrderBy(g => s.Pipelines.Values.SelectMany(p => p.OrderedStages).ToList().FindIndex(x => x.Id == g.Key)).ToList()
               : w.GroupBy == "status" ? groups.OrderBy(g => Array.FindIndex(LeadStatuses.All, x => x.Id == g.Key)).ToList()
               : groups.OrderByDescending(g => g.Value).ToList();

        // Máximo 8 categorías: el resto se agrupa en "Otros"
        if (groups.Count > 8 && w.GroupBy is not ("month" or "closemonth"))
        {
            var rest = groups.Skip(7).ToList();
            groups = groups.Take(7).Append(("__other", "Otros", "#9ca3af", rest.Sum(g => g.Value), rest.Sum(g => g.Count))).ToList();
        }

        data.Labels = groups.Select(g => g.Label).ToList();
        var entityColors = groups.All(g => g.Color is not null);
        data.Series.Add(new ChartSeries
        {
            Name = w.Metric == "count" ? "Cantidad" : w.Metric == "amount" ? "Importe" : "Ponderado",
            Values = groups.Select(g => g.Value).ToList(),
            Colors = entityColors ? groups.Select(g => g.Color!).ToList() : null,
            Color = Palette[0]
        });
        if (w.Type == WidgetTypes.Table)
        {
            data.Columns = [GroupLabel(w.GroupBy), "Cantidad", "Importe"];
            data.Rows = groups.Select(g => new[] { g.Label, g.Count.ToString("N0"), items.Where(i => GroupOf(w, i, s).Key == g.Key).Sum(i => i is Deal d ? Base(d.Amount, d.Currency) : i is Lead l ? Base(l.Opportunity, l.Currency) : 0).ToString("N2") }).ToList();
        }
        return data;
    }

    public static string GroupLabel(string groupBy) => ReportGroups.All.FirstOrDefault(g => g.Id == groupBy).Label ?? groupBy;

    private ChartData Funnel(ReportWidget w, Snapshot s, DateTime? from)
    {
        var deals = Items(w, s, from).OfType<Deal>().ToList();
        var pipeline = s.Pipelines.Values.OrderByDescending(p => p.IsDefault).FirstOrDefault();
        var data = new ChartData { Format = "N0" };
        if (pipeline is null) return data;
        var inPipeline = deals.Where(d => d.PipelineId == pipeline.Id).ToList();
        data.Subtitle = pipeline.Name;
        var series = new ChartSeries { Name = w.Metric == "count" ? "Cantidad" : "Importe", Colors = new() };
        foreach (var st in pipeline.OrderedStages)
        {
            var inStage = inPipeline.Where(d => d.StageId == st.Id).ToList();
            data.Labels.Add(st.Name);
            series.Values.Add(w.Metric == "count" ? inStage.Count : inStage.Sum(d => Base(d.Amount, d.Currency)));
            series.Colors.Add(st.Color);
        }
        data.Series.Add(series);
        data.Total = series.Values.Sum();
        return data;
    }

    private ChartData Forecast(ReportWidget w, Snapshot s)
    {
        var open = s.Deals.Where(d => s.KindOf(d) == StageKind.Process && (string.IsNullOrEmpty(w.Person) || d.AssignedTo == w.Person)).ToList();
        var months = Enumerable.Range(0, 6).Select(i => DateTime.UtcNow.Date.AddMonths(i)).Select(m => new DateTime(m.Year, m.Month, 1)).ToList();
        var data = new ChartData { Format = "N0", Labels = months.Select(m => m.ToString("MMM yyyy", Es)).ToList() };
        var weighted = new ChartSeries { Name = "Ponderado (importe × prob.)", Color = Palette[0] };
        var total = new ChartSeries { Name = "Importe total", Color = Palette[1] };
        foreach (var m in months)
        {
            var inMonth = open.Where(d => d.ExpectedCloseDate is not null && d.ExpectedCloseDate.Value.Year == m.Year && d.ExpectedCloseDate.Value.Month == m.Month).ToList();
            weighted.Values.Add(Math.Round(inMonth.Sum(d => Base(d.Amount, d.Currency) * d.Probability / 100m)));
            total.Values.Add(inMonth.Sum(d => Base(d.Amount, d.Currency)));
        }
        var noDate = open.Where(d => d.ExpectedCloseDate is null).ToList();
        data.Series.AddRange([weighted, total]);
        data.Total = open.Sum(d => Base(d.Amount, d.Currency) * d.Probability / 100m);
        data.Subtitle = noDate.Count > 0 ? $"{noDate.Count} negociación(es) abiertas sin fecha de cierre no se incluyen" : null;
        return data;
    }

    private ChartData Leaderboard(ReportWidget w, Snapshot s, DateTime? from)
    {
        var deals = s.Deals.Where(d => from is null || (d.ClosedAt ?? d.CreatedAt) >= from).ToList();
        var people = deals.Select(d => d.AssignedTo ?? "Sin asignar").Distinct().ToList();
        var rows = people.Select(p =>
        {
            var mine = deals.Where(d => (d.AssignedTo ?? "Sin asignar") == p).ToList();
            return (Person: p, Won: mine.Where(d => s.KindOf(d) == StageKind.Success).Sum(d => Base(d.Amount, d.Currency)), Open: mine.Where(d => s.KindOf(d) == StageKind.Process).Sum(d => Base(d.Amount, d.Currency)),
                    Lost: mine.Where(d => s.KindOf(d) == StageKind.Failure).Sum(d => Base(d.Amount, d.Currency)), WonCount: mine.Count(d => s.KindOf(d) == StageKind.Success), Count: mine.Count);
        }).OrderByDescending(r => r.Won).ThenByDescending(r => r.Open).ToList();
        var data = new ChartData { Format = "N0", Labels = rows.Select(r => r.Person).ToList(), Total = rows.Sum(r => r.Won) };
        data.Series.Add(new ChartSeries { Name = "Ganado", Values = rows.Select(r => r.Won).ToList(), Color = "#008300" });
        data.Series.Add(new ChartSeries { Name = "Abierto", Values = rows.Select(r => r.Open).ToList(), Color = Palette[0] });
        data.Series.Add(new ChartSeries { Name = "Perdido", Values = rows.Select(r => r.Lost).ToList(), Color = "#e34948" });
        data.Columns = ["Responsable", "Ganado", "Abierto", "Perdido", "Negociaciones", "Tasa de éxito"];
        data.Rows = rows.Select(r => new[] { r.Person, r.Won.ToString("N0"), r.Open.ToString("N0"), r.Lost.ToString("N0"), r.Count.ToString(), r.Count == 0 ? "—" : (100.0 * r.WonCount / Math.Max(1, r.Count)).ToString("N0") + " %" }).ToList();
        return data;
    }

    private ChartData StageTime(ReportWidget w, Snapshot s, DateTime? from)
    {
        var pipeline = s.Pipelines.Values.OrderByDescending(p => p.IsDefault).FirstOrDefault();
        var data = new ChartData { Format = "N1", Subtitle = "Días promedio por etapa (negociaciones que ya salieron de la etapa)" };
        if (pipeline is null) return data;
        var durations = new Dictionary<string, List<double>>();
        foreach (var d in s.Deals.Where(d => d.PipelineId == pipeline.Id && (from is null || d.CreatedAt >= from)))
        {
            var h = d.History.OrderBy(x => x.At).ToList();
            for (var i = 0; i < h.Count - 1; i++)
                durations.GetOrAdd(h[i].ToStageId).Add((h[i + 1].At - h[i].At).TotalDays);
        }
        var series = new ChartSeries { Name = "Días promedio", Colors = new() };
        foreach (var st in pipeline.OrderedStages.Where(x => x.Kind == StageKind.Process))
        {
            data.Labels.Add(st.Name);
            series.Values.Add(durations.TryGetValue(st.Id, out var list) && list.Count > 0 ? (decimal)Math.Round(list.Average(), 1) : 0);
            series.Colors.Add(st.Color);
        }
        data.Series.Add(series);
        data.Total = series.Values.Sum();
        return data;
    }

    private ChartData Conversion(ReportWidget w, Snapshot s, DateTime? from)
    {
        var leads = s.Leads.Where(l => from is null || l.CreatedAt >= from).ToList();
        var groups = leads.GroupBy(l => l.Source ?? "Sin fuente").OrderByDescending(g => g.Count()).Take(8).ToList();
        var data = new ChartData { Format = "N0", Labels = groups.Select(g => g.Key).ToList() };
        data.Series.Add(new ChartSeries { Name = "Convertidos", Values = groups.Select(g => (decimal)g.Count(l => l.Status == LeadStatuses.Converted)).ToList(), Color = "#008300" });
        data.Series.Add(new ChartSeries { Name = "En trabajo", Values = groups.Select(g => (decimal)g.Count(l => !LeadStatuses.Get(l.Status).IsFinal)).ToList(), Color = Palette[0] });
        data.Series.Add(new ChartSeries { Name = "Descartados", Values = groups.Select(g => (decimal)g.Count(l => l.Status == LeadStatuses.Junk)).ToList(), Color = "#e34948" });
        var converted = leads.Count(l => l.Status == LeadStatuses.Converted);
        var closed = leads.Count(l => LeadStatuses.Get(l.Status).IsFinal);
        data.Total = closed == 0 ? 0 : Math.Round(100m * converted / closed);
        data.Format = "P";
        data.Subtitle = $"{converted} convertidos de {closed} cerrados · {leads.Count} prospectos en el periodo";
        return data;
    }
}

internal static class DictExt
{
    public static List<double> GetOrAdd(this Dictionary<string, List<double>> d, string key)
    {
        if (!d.TryGetValue(key, out var l)) { l = new(); d[key] = l; }
        return l;
    }
}
