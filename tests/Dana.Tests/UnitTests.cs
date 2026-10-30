using System.Text;
using System.Text.Json;
using Dana.Components.Shared;
using MongoDB.Bson;

namespace Dana.Tests;

/// <summary>Pruebas puras (sin MongoDB).</summary>
public class UnitTests
{
    static UnitTests() => MongoContext.ConfigureSerialization();

    [Fact]
    public void CustomColumn_Money_RoundTripsThroughText()
    {
        var col = BoardColumn<Deal>.FromCustom(new CustomColumnDef { Id = "n1", Title = "N", Type = "Money" }, []);
        var deal = new Deal();
        col.SetFromText(deal, "1234.5");
        Assert.Equal("1234.5", deal.Custom["n1"]);
        Assert.Equal(1234.5m, (decimal?)col.GetValue(deal));
        Assert.Equal("1,234.50", col.Display(deal));
    }

    [Fact]
    public void CustomColumn_Date_And_Checkbox()
    {
        var date = BoardColumn<Deal>.FromCustom(new CustomColumnDef { Id = "d1", Title = "D", Type = "Date" }, []);
        var chk = BoardColumn<Deal>.FromCustom(new CustomColumnDef { Id = "c1", Title = "C", Type = "Checkbox" }, []);
        var deal = new Deal();
        date.SetFromText(deal, "2026-12-01");
        chk.SetValue!(deal, true);
        Assert.Equal("01/12/2026", date.Display(deal));
        Assert.Equal("true", deal.Custom["c1"]);
        Assert.True(chk.Matches(deal, new FilterRuleDef { ColumnKey = "c1", Op = "is", Value = "true" }));
    }

    [Theory]
    [InlineData("gt", "5000", true)]
    [InlineData("lt", "5000", false)]
    [InlineData("gt", "7000", false)]
    public void Filters_NumericComparison(string op, string value, bool expected)
    {
        var col = BoardColumn<Deal>.Money("amount", "Importe", d => d.Amount, (d, v) => d.Amount = v ?? 0);
        var deal = new Deal { Amount = 6000 };
        Assert.Equal(expected, col.Matches(deal, new FilterRuleDef { ColumnKey = "amount", Op = op, Value = value }));
    }

    [Fact]
    public void Filters_Contains_IsCaseInsensitive_And_Empty()
    {
        var col = BoardColumn<Deal>.Text("t", "T", d => d.Title, null);
        var deal = new Deal { Title = "Licencias ERP" };
        Assert.True(col.Matches(deal, new FilterRuleDef { Op = "contains", Value = "licEN" }));
        Assert.True(col.Matches(deal, new FilterRuleDef { Op = "notempty" }));
        Assert.False(col.Matches(new Deal(), new FilterRuleDef { Op = "notempty" }));
    }

    [Fact]
    public void EmailTemplate_RendersPlaceholders()
    {
        var text = EmailService.Render("Hola {{nombre}} de {{compañía}}, saludos {{usuario}}", new EmailContext("María", "R", "ACME", "m@x", "T", "Steven"));
        Assert.Equal("Hola María de ACME, saludos Steven", text);
    }

    [Fact]
    public void Quote_Totals_ApplyDiscountAndTax()
    {
        var q = new Quote();
        q.Lines.Add(new QuoteLine { Description = "Licencia", Quantity = 2, UnitPrice = 100m, DiscountPercent = 10, TaxRate = 13m });
        q.Lines.Add(new QuoteLine { Description = "Soporte", Quantity = 1, UnitPrice = 50m, TaxRate = 0 });
        Assert.Equal(230m, q.Subtotal);
        Assert.Equal(23.4m, q.Tax);
        Assert.Equal(253.4m, q.Total);
    }

    [Fact]
    public void Settings_ConvertsToBaseCurrency()
    {
        var s = new AppSettings { BaseCurrency = "USD", Rates = new() { ["USD"] = 1m, ["CRC"] = 500m } };
        Assert.Equal(10m, s.ToBase(5000m, "CRC"));
        Assert.Equal(7m, s.ToBase(7m, "USD"));
        Assert.Equal(7m, s.ToBase(7m, null));
    }

    [Fact]
    public void Audit_Diff_DetectsChangedFieldsAndCustomValues()
    {
        var before = new Contact { Name = "Ana", Position = null, Custom = { ["x"] = "1" } }.ToBsonDocument();
        var after = new Contact { Name = "Ana", Position = "CTO", Custom = { ["x"] = "2" } }.ToBsonDocument();
        var changes = MongoRepository<Contact>.Diff(before, after);
        Assert.Contains(changes, c => c.Field == "position" && c.Old is null && c.New == "CTO");
        Assert.Contains(changes, c => c.Field == "custom.x" && c.Old == "1" && c.New == "2");
        Assert.DoesNotContain(changes, c => c.Field == "updatedAt");
    }

    [Fact]
    public void Inbound_Flatten_ReadsNestedOneLevel()
    {
        using var doc = JsonDocument.Parse("""{"name":"Ana","meta":{"utm":"x","n":3},"ok":true}""");
        var flat = InboundLeadService.Flatten(doc.RootElement);
        Assert.Equal("Ana", flat["name"]);
        Assert.Equal("x", flat["meta.utm"]);
        Assert.Equal("x", flat["utm"]);
        Assert.Equal("3", flat["n"]);
    }

    [Fact]
    public void Import_ParsesCsvWithSemicolonsAndQuotes()
    {
        var csv = "nombre;apellido;correo\nLuis;Mora;luis@x.com\n\"Ana, María\";Paz;ana@x.com";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var preview = ExportImportService.Parse(ms, "c.csv");
        Assert.Equal(3, preview.Columns.Count);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("Ana, María", preview.Rows[1][0]);
        var map = ExportImportService.SuggestMapping("contacts", preview.Columns);
        Assert.Equal("name", map["nombre"]);
        Assert.Equal("email", map["correo"]);
    }

    [Fact]
    public void Nav_ResolvesPreferencesAndRoundTrips()
    {
        var cfg = new MenuConfig { Items = [new MenuItemPref { Id = "leads", Label = "Oportunidades", Hidden = false, Sort = 1 }, new MenuItemPref { Id = "link_1", Label = "Soporte", Url = "https://x", Icon = "🛟", Section = "Ayuda", Sort = 5 }] };
        var items = NavService.Resolve(cfg, isAdmin: false);
        Assert.Equal("Oportunidades", items.First(i => i.Id == "leads").Label);
        Assert.DoesNotContain(items, i => i.Id == "users");           // solo admin
        Assert.Contains(items, i => i.IsCustom && i.Href == "https://x");
        var back = NavService.ToConfig(items, NavService.SectionOrder(cfg));
        Assert.Contains(back.Items, p => p.Id == "leads" && p.Label == "Oportunidades");
        Assert.Contains(back.Items, p => p.Url == "https://x");
    }

    [Fact]
    public void LeadStatuses_FallbackToNew()
    {
        Assert.Equal("Nuevo", LeadStatuses.Get("desconocido").Name);
        Assert.True(LeadStatuses.Get(LeadStatuses.Converted).IsFinal);
    }

    [Fact]
    public void Pipeline_DefaultHasWonAndLost()
    {
        var p = Pipeline.CreateDefault();
        Assert.Contains(p.Stages, s => s.Kind == StageKind.Success);
        Assert.Contains(p.Stages, s => s.Kind == StageKind.Failure);
        Assert.Equal("Nuevo", p.FirstStage!.Name);
    }

    [Fact]
    public void Entity_Clone_DropsIdentityAndCopiesCustom()
    {
        var d = new Deal { Id = ObjectId.GenerateNewId().ToString(), Title = "X", Custom = { ["k"] = "v" }, DeletedAt = DateTime.UtcNow };
        var c = Entity.Clone(d);
        Assert.Null(c.Id); Assert.Null(c.DeletedAt); Assert.Equal("v", c.Custom["k"]); Assert.NotSame(d.Custom, c.Custom);
    }
}

public class MarkdownTests
{
    [Fact]
    public void Markdown_RendersListsBoldLinksAndEscapesHtml()
    {
        var html = Dana.Services.Ai.Markdown.ToHtml("Hola **Ana**\n\n- uno\n- [Ver](leads/abc)\n\n<script>x</script> `c`");
        Assert.Contains("<strong>Ana</strong>", html);
        Assert.Contains("<ul><li>uno</li><li><a href=\"leads/abc\">Ver</a></li></ul>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<code>c</code>", html);
    }

    [Fact]
    public void Markdown_BlocksJavascriptLinks_AndOpensExternalInNewTab()
    {
        var html = Dana.Services.Ai.Markdown.ToHtml("[a](javascript:alert(1)) [b](https://x.y)");
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("target=\"_blank\"", html);
    }
}

public class RoutineSchedulerTests
{
    private static readonly TimeZoneInfo Cr = Dana.Services.Ai.RoutineScheduler.Zone("America/Costa_Rica"); // UTC-6, sin horario de verano

    [Fact]
    public void Daily_RunsOnceAfterTimeOfDay()
    {
        var r = new AgentRoutine { Kind = RoutineKinds.Daily, TimeOfDay = "08:00", WeekdaysOnly = false };
        var tue0730 = new DateTime(2026, 11, 3, 13, 30, 0, DateTimeKind.Utc); // 07:30 local
        var tue0805 = new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc);
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(r, tue0730, Cr));
        Assert.True(Dana.Services.Ai.RoutineScheduler.IsDue(r, tue0805, Cr));
        r.LastRunAt = tue0805;
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(r, tue0805.AddHours(3), Cr));
        Assert.True(Dana.Services.Ai.RoutineScheduler.IsDue(r, tue0805.AddDays(1), Cr));
    }

    [Fact]
    public void WeekdaysOnly_SkipsWeekend_And_Hours_RespectsInterval()
    {
        var daily = new AgentRoutine { Kind = RoutineKinds.Daily, TimeOfDay = "08:00", WeekdaysOnly = true };
        var sat = new DateTime(2026, 11, 7, 15, 0, 0, DateTimeKind.Utc);
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(daily, sat, Cr));
        var hours = new AgentRoutine { Kind = RoutineKinds.Hours, EveryHours = 4, WeekdaysOnly = false, LastRunAt = sat };
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(hours, sat.AddHours(3), Cr));
        Assert.True(Dana.Services.Ai.RoutineScheduler.IsDue(hours, sat.AddHours(4), Cr));
        var manual = new AgentRoutine { Kind = RoutineKinds.Manual };
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(manual, sat, Cr));
        var weekly = new AgentRoutine { Kind = RoutineKinds.Weekly, Weekday = 1, TimeOfDay = "09:00" };
        var mon = new DateTime(2026, 11, 2, 15, 30, 0, DateTimeKind.Utc);
        Assert.True(Dana.Services.Ai.RoutineScheduler.IsDue(weekly, mon, Cr));
        Assert.False(Dana.Services.Ai.RoutineScheduler.IsDue(weekly, mon.AddDays(1), Cr));
        Assert.Equal("lunes a las 09:00", Dana.Services.Ai.RoutineScheduler.Describe(weekly));
    }
}

public class AgentTemplateTests
{
    [Fact]
    public void Templates_AreConsistentWithToolCatalog()
    {
        var names = Dana.Services.Ai.AgentTools.Catalog.Select(t => t.Name).ToHashSet();
        Assert.True(Dana.Services.Ai.AgentTemplates.All_.Length >= 8);
        foreach (var t in Dana.Services.Ai.AgentTemplates.All_)
        {
            Assert.All(t.Tools, tool => Assert.Contains(tool, names));
            var a = Dana.Services.Ai.AgentTemplates.Create(t, 0);
            Assert.Equal(t.Id, a.Template);
            Assert.Equal(t.Tools.Distinct().Count(), a.Tools.Count);
        }
        var cot = Dana.Services.Ai.AgentTemplates.Find("cotizador")!;
        Assert.Contains("cotizacion_crear", cot.Tools);
        Assert.True(cot.AllowWrites);
        Assert.Contains("buscar_web", Dana.Services.Ai.AgentTemplates.Find("prospeccion")!.Tools);
    }

    [Fact]
    public void Mood_DescribesAndClamps()
    {
        Assert.Equal("Entusiasmado", AgentMood.Describe(90).Label);
        Assert.Equal("Frustrado", AgentMood.Describe(10).Label);
        Assert.Equal(100, AgentMood.Clamp(140));
        Assert.Equal(0, AgentMood.Clamp(-5));
    }
}

public class SseAccumulatorTests
{
    [Fact]
    public void Accumulates_Text_ToolCalls_And_Usage()
    {
        var acc = new Dana.Services.Ai.OpenAiProvider.SseAccumulator();
        var deltas = new List<string>();
        acc.Feed("{\"choices\":[{\"delta\":{\"content\":\"Hola \"}}]}", deltas.Add);
        acc.Feed("{\"choices\":[{\"delta\":{\"content\":\"mundo\"}}]}", deltas.Add);
        acc.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"buscar\",\"arguments\":\"{\\\"tipo\\\":\"}}]}}]}", deltas.Add);
        acc.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"lead\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}", deltas.Add);
        acc.Feed("{\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":7}}", deltas.Add);
        Assert.Equal("Hola mundo", acc.Content.ToString());
        Assert.Equal(["Hola ", "mundo"], deltas);
        Assert.Single(acc.ToolCalls);
        Assert.Equal("buscar", acc.ToolCalls[0].Name);
        Assert.Equal("{\"tipo\":\"lead\"}", acc.ToolCalls[0].Arguments.ToString());
        Assert.Equal("tool_calls", acc.FinishReason);
        Assert.Equal(12, acc.InputTokens); Assert.Equal(7, acc.OutputTokens);
        var msg = acc.ToAssistantMessage();
        Assert.Equal("call_1", msg["tool_calls"]![0]!["id"]!.GetValue<string>());
    }
}
