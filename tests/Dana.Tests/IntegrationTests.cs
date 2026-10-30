using Microsoft.Extensions.DependencyInjection;

namespace Dana.Tests;

/// <summary>Pruebas de integración contra MongoDB (se omiten si no hay servidor).</summary>
[Collection("mongo")]
public class IntegrationTests
{
    private readonly MongoFixture _f;
    public IntegrationTests(MongoFixture f) { _f = f; }

    private bool Skip => !_f.Available;

    private async Task<(Pipeline p, List<Stage> stages)> SeedAsync()
    {
        var demo = _f.Get<DemoDataService>();
        await demo.SeedAsync();
        var p = await _f.Get<PipelineService>().GetDefaultAsync();
        return (p, p.OrderedStages.ToList());
    }

    [Fact]
    public async Task Deals_MoveBetweenStages_RecordsHistoryAndClosure()
    {
        if (Skip) return;
        var (p, stages) = await SeedAsync();
        var deals = _f.Get<DealService>();
        var d = await deals.SaveAsync(new Deal { Title = "Prueba etapas", PipelineId = p.Id!, StageId = stages[0].Id }, "Test");
        var moved = await deals.MoveToStageAsync(d.Id!, stages[2].Id, "Test");
        Assert.Equal(stages[2].Id, moved!.StageId);
        Assert.Equal(2, moved.History.Count);
        Assert.Equal(stages[0].Name, moved.History[^1].FromStageName);
        var won = await deals.MoveToStageAsync(d.Id!, stages.First(s => s.Kind == StageKind.Success).Id, "Test");
        Assert.NotNull(won!.ClosedAt); Assert.Equal(100, won.Probability);
        var reopened = await deals.MoveToStageAsync(d.Id!, stages[1].Id, "Test");
        Assert.Null(reopened!.ClosedAt);
        Assert.Contains(await _f.Get<ActivityService>().GetForAsync("deal", d.Id!), a => a.Kind == ActivityKinds.Stage);
    }

    [Fact]
    public async Task Leads_Convert_CreatesContactCompanyDeal_Once()
    {
        if (Skip) return;
        var (p, _) = await SeedAsync();
        var leads = _f.Get<LeadService>();
        var lead = new Lead { Title = "Lead conv", Name = "Ana", LastName = "Paz", CompanyName = "Paz SA", Email = "a@p.com", Opportunity = 900 };
        await leads.SaveAsync(lead, "Test");
        var converted = await leads.ConvertAsync(lead, new ConvertOptions(true, true, true, p.Id), "Test");
        Assert.Equal(LeadStatuses.Converted, converted.Status);
        var deal = await _f.Get<DealService>().GetAsync(converted.ConvertedDealId);
        Assert.Equal(converted.ConvertedContactId, deal!.ContactId);
        Assert.Equal(converted.ConvertedCompanyId, deal.CompanyId);
        Assert.Equal(900, deal.Amount);
        var contact = await _f.Get<IRepository<Contact>>().GetByIdAsync(converted.ConvertedContactId);
        Assert.Equal(converted.ConvertedCompanyId, contact!.CompanyId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leads.ConvertAsync(converted, new ConvertOptions(true, false, false, null), "Test"));
    }

    [Fact]
    public async Task Pipelines_OnlyOneDefault_And_CannotDeleteWithDeals()
    {
        if (Skip) return;
        var (p, _) = await SeedAsync();
        var pipelines = _f.Get<PipelineService>();
        var p2 = Pipeline.CreateDefault("Servicios", isDefault: true);
        await pipelines.SaveAsync(p2);
        var all = await pipelines.GetAllAsync();
        Assert.Single(all, x => x.IsDefault);
        Assert.False((await pipelines.DeleteAsync(p.Id!)).Ok);
        Assert.True((await pipelines.DeleteAsync(p2.Id!)).Ok);
        p.IsDefault = true; await pipelines.SaveAsync(p);
    }

    [Fact]
    public async Task Tasks_Notify_Assignee_And_Mentions()
    {
        if (Skip) return;
        var users = _f.Get<UserService>();
        await users.RegisterAsync("admin.t@test.com", "Admin T", "Clave12345");
        await users.RegisterAsync("ana.t@test.com", "Ana T", "Clave12345");
        var tasks = _f.Get<TaskService>();
        var notif = _f.Get<NotificationService>();
        var before = await notif.CountUnreadAsync("Ana T");
        var t = await tasks.SaveAsync(new TaskItem { Title = "Llamar", Kind = TaskKinds.Call, AssignedTo = "Ana T", DueAt = DateTime.UtcNow.AddDays(-1) }, "Admin T");
        Assert.True(t.IsOverdue);
        Assert.Equal(before + 1, await notif.CountUnreadAsync("Ana T"));
        Assert.Equal(["Ana T"], await notif.FindMentionsAsync("Hola @Ana T y @nadie"));
        var done = await tasks.ToggleDoneAsync(t.Id!, "Ana T");
        Assert.True(done!.IsDone);
        Assert.Empty(await tasks.GetForUserAsync("Ana T", false));
    }

    [Fact]
    public async Task Automations_StageChange_NotifiesAndCreatesTask()
    {
        if (Skip) return;
        var (p, stages) = await SeedAsync();
        var users = _f.Get<UserService>();
        await users.RegisterAsync("admin.a@test.com", "Admin A", "Clave12345");
        await users.RegisterAsync("eve.a@test.com", "Eve A", "Clave12345");
        var autos = _f.Get<AutomationService>();
        var won = stages.First(s => s.Kind == StageKind.Success);
        await autos.SaveAsync(new Automation { Name = "Ganada test", BoardKey = "deals", Trigger = new() { Type = TriggerTypes.StageChanged, Params = new() { ["stageId"] = won.Id } },
            Actions = [new() { Type = ActionTypes.Notify, Params = new() { ["user"] = "Eve A", ["text"] = "Ganada: {{titulo}}" } }, new() { Type = ActionTypes.CreateTask, Params = new() { ["kind"] = TaskKinds.Task, ["title"] = "Facturar {{titulo}}", ["days"] = "2", ["user"] = "Eve A" } }] });
        var deals = _f.Get<DealService>();
        var d = await deals.SaveAsync(new Deal { Title = "Auto deal", PipelineId = p.Id!, StageId = stages[0].Id }, "Test");
        await deals.MoveToStageAsync(d.Id!, won.Id, "Test");
        Assert.Contains(await _f.Get<NotificationService>().GetForAsync("Eve A"), n => n.Text == "Ganada: Auto deal");
        Assert.Contains(await _f.Get<TaskService>().GetForEntityAsync("deal", d.Id!), t => t.Title == "Facturar Auto deal" && t.AssignedTo == "Eve A");
        Assert.Equal(1, (await autos.GetAllAsync()).First(a => a.Name == "Ganada test").RunCount);
    }

    [Fact]
    public async Task ReminderWorker_SendsOnce()
    {
        if (Skip) return;
        var (p, stages) = await SeedAsync();
        var tasks = _f.Get<TaskService>();
        await tasks.SaveAsync(new TaskItem { Title = "Reunión pronto", Kind = TaskKinds.Meeting, DueAt = DateTime.UtcNow.AddMinutes(10), AssignedTo = "Rem T" }, "Rem T");
        await _f.Get<DealService>().SaveAsync(new Deal { Title = "Cierre vencido", PipelineId = p.Id!, StageId = stages[0].Id, ExpectedCloseDate = DateTime.UtcNow.Date.AddDays(-3), AssignedTo = "Rem T" }, "Test");
        var worker = _f.Get<ReminderWorker>();
        await worker.RunOnceAsync();
        var notif = _f.Get<NotificationService>();
        var n1 = (await notif.GetForAsync("Rem T", 50)).Count;
        Assert.True(n1 >= 2);
        await worker.RunOnceAsync();
        Assert.Equal(n1, (await notif.GetForAsync("Rem T", 50)).Count);
    }

    [Fact]
    public async Task InboundApi_MapsSpanishFields_AndValidatesKey()
    {
        if (Skip) return;
        var inbound = _f.Get<InboundLeadService>();
        var ch = new ApiChannel { Name = "Web test", TitleTemplate = "Web: {{nombre}}" };
        await inbound.SaveAsync(ch);
        var r = await inbound.ProcessAsync(ch.Key, new(StringComparer.OrdinalIgnoreCase) { ["nombre"] = "Pedro Alfaro", ["correo"] = "p@x.com", ["extra"] = "dato" }, null, null);
        Assert.True(r.Ok);
        Assert.Equal("Web: Pedro", r.Lead!.Title); Assert.Equal("Alfaro", r.Lead.LastName); Assert.Contains("extra: dato", r.Lead.Comments);
        Assert.Equal(401, (await inbound.ProcessAsync("mala", new(), null, null)).Status);
        ch.IsEnabled = false; await inbound.SaveAsync(ch);
        Assert.Equal(403, (await inbound.ProcessAsync(ch.Key, new() { ["name"] = "x" }, null, null)).Status);
    }

    [Fact]
    public async Task ExportImport_ExcelRoundTrip()
    {
        if (Skip) return;
        await SeedAsync();
        var svc = _f.Get<ExportImportService>();
        var xlsx = await svc.ExportXlsxAsync("contacts");
        using var ms = new MemoryStream(xlsx);
        var preview = ExportImportService.Parse(ms, "contactos.xlsx");
        Assert.Contains("Nombre", preview.Columns);
        var map = ExportImportService.SuggestMapping("contacts", preview.Columns);
        var before = await _f.Get<IRepository<Contact>>().CountAsync();
        var result = await svc.ImportAsync("contacts", preview.Columns, preview.Rows, map, "Test");
        Assert.Equal(preview.TotalRows, result.Created);
        Assert.Empty(result.Errors);
        Assert.Equal(before + preview.TotalRows, await _f.Get<IRepository<Contact>>().CountAsync());
    }

    [Fact]
    public async Task SoftDelete_Audit_Restore_Purge()
    {
        if (Skip) return;
        var contacts = _f.Get<IRepository<Contact>>();
        var audit = _f.Get<IRepository<AuditEntry>>();
        var c = await contacts.InsertAsync(new Contact { Name = "Papelera", CreatedBy = "Steven" });
        c.Position = "Gerente"; c.UpdatedBy = "Ana"; await contacts.UpdateAsync(c);
        await contacts.DeleteAsync(c.Id!, "Steven");
        Assert.Null(await contacts.GetByIdAsync(c.Id));
        Assert.Contains(await contacts.GetDeletedAsync(), x => x.Id == c.Id);
        await contacts.RestoreAsync(c.Id!, "Steven");
        Assert.NotNull(await contacts.GetByIdAsync(c.Id));
        await contacts.PurgeAsync(c.Id!, "Steven");
        var entries = (await audit.FindAsync(a => a.EntityId == c.Id)).OrderBy(a => a.CreatedAt).Select(a => a.Action).ToList();
        Assert.Equal(["create", "update", "delete", "restore", "purge"], entries);
    }

    [Fact]
    public async Task Permissions_Levels()
    {
        if (Skip) return;
        var users = _f.Get<UserService>();
        var perms = _f.Get<PermissionService>();
        await users.RegisterAsync("admin.p@test.com", "Admin P", "Clave12345");
        var (bob, _) = await users.RegisterAsync("bob.p@test.com", "Bob P", "Clave12345");
        await users.RegisterAsync("eve.p@test.com", "Eve P", "Clave12345");
        await perms.SaveTeamAsync(new Team { Name = "Equipo P", Members = ["Bob P", "Eve P"] });
        bob!.Permissions["deals"] = AccessLevels.Team; bob.Permissions["leads"] = AccessLevels.Own; bob.Permissions["contacts"] = AccessLevels.None;
        await users.SavePermissionsAsync(bob);
        var team = await perms.GetAccessAsync("Bob P", "deals", false);
        Assert.True(team.CanEdit && !team.CanSeeAll && team.CanSee("Eve P") && !team.CanSee("Admin P"));
        Assert.False((await perms.GetAccessAsync("Bob P", "leads", false)).CanSee("Eve P"));
        Assert.False((await perms.GetAccessAsync("Bob P", "contacts", false)).CanView);
        Assert.True((await perms.GetAccessAsync("Bob P", "contacts", true)).CanSeeAll);
    }

    [Fact]
    public async Task Quotes_AcceptUpdatesDealAmount()
    {
        if (Skip) return;
        var (p, stages) = await SeedAsync();
        var quotes = _f.Get<QuoteService>();
        var deal = await _f.Get<DealService>().SaveAsync(new Deal { Title = "Cotizable", PipelineId = p.Id!, StageId = stages[0].Id, Amount = 1 }, "Test");
        var q = await quotes.CreateForDealAsync(deal, "Test");
        q.Lines.Add(new QuoteLine { Description = "Licencia", Quantity = 2, UnitPrice = 100m, DiscountPercent = 10, TaxRate = 13m });
        await quotes.SaveAsync(q, "Test");
        await quotes.SetStatusAsync(q, "accepted", "Test");
        Assert.Equal(203.4m, (await _f.Get<DealService>().GetAsync(deal.Id))!.Amount);
        var q2 = await quotes.CreateForDealAsync(deal, "Test");
        Assert.NotEqual(q.Number, q2.Number);
    }

    [Fact]
    public async Task Reports_ComputeAllDefaultWidgets()
    {
        if (Skip) return;
        await SeedAsync();
        var reports = _f.Get<ReportService>();
        var dash = await reports.GetDashboardAsync();
        var snap = await reports.LoadAsync();
        foreach (var w in dash.Widgets) Assert.NotNull(reports.Compute(w, snap));
        var funnel = reports.Compute(new ReportWidget { Type = WidgetTypes.Funnel, Board = "deals", Metric = "count" }, snap);
        Assert.Equal(snap.Pipelines.Values.First(p => p.IsDefault).Stages.Count, funnel.Labels.Count);
    }
}

[Collection("mongo")]
public class CatalogTests
{
    private readonly MongoFixture _f;
    public CatalogTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task Catalog_FiltersByCategoryTagAndVisibility()
    {
        if (!_f.Available) return;
        var quotes = _f.Get<QuoteService>();
        await quotes.SaveCategoryAsync(new ProductCategory { Name = "Servicios" }, "Test");
        await quotes.SaveProductAsync(new Product { Name = "Instalación", Category = "Servicios", Tags = ["oferta"], Price = 100 }, "Test");
        await quotes.SaveProductAsync(new Product { Name = "Licencia", Category = "Software", Tags = ["anual"], Price = 50 }, "Test");
        await quotes.SaveProductAsync(new Product { Name = "Oculto", IsPublic = false, Price = 1 }, "Test");
        await quotes.SaveProductAsync(new Product { Name = "Inactivo", IsActive = false, Price = 1 }, "Test");
        var all = await quotes.GetCatalogAsync();
        Assert.DoesNotContain(all, p => p.Name is "Oculto" or "Inactivo");
        Assert.Single(await quotes.GetCatalogAsync(category: "servicios"));
        Assert.Single(await quotes.GetCatalogAsync(tag: "ANUAL"));
        Assert.Single(await quotes.GetCatalogAsync(q: "licen"));
        var pub = QuoteService.ToPublic(all.First(p => p.Name == "Instalación"), "https://x");
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(pub));
        Assert.Equal("Instalación", doc.RootElement.GetProperty("name").GetString());
        Assert.StartsWith("https://x/catalog/images/", doc.RootElement.GetProperty("images").GetArrayLength() == 0 ? "https://x/catalog/images/" : doc.RootElement.GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task Catalog_RenamingCategoryUpdatesProducts()
    {
        if (!_f.Available) return;
        var quotes = _f.Get<QuoteService>();
        var cat = new ProductCategory { Name = "Hardware" };
        await quotes.SaveCategoryAsync(cat, "Test");
        var p = new Product { Name = "Router", Category = "Hardware" };
        await quotes.SaveProductAsync(p, "Test");
        cat.Name = "Equipos"; await quotes.SaveCategoryAsync(cat, "Test");
        Assert.Equal("Equipos", (await quotes.GetProductAsync(p.Id))!.Category);
        await quotes.DeleteCategoryAsync(cat, "Test");
        Assert.Null((await quotes.GetProductAsync(p.Id))!.Category);
    }
}

[Collection("mongo")]
public class AgentTests
{
    private readonly MongoFixture _f;
    public AgentTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task Tools_SearchCreateUpdateAndStats()
    {
        if (!_f.Available) return;
        var tools = _f.Get<Dana.Services.Ai.AgentTools>();
        var leads = _f.Get<LeadService>();
        await leads.SaveAsync(new Lead { Title = "Agente prueba", Email = "agente@test.com", CompanyName = "ACME Robots" }, "Test");

        var found = await tools.ExecuteAsync("buscar", Json("{\"tipo\":\"lead\",\"texto\":\"ACME Rob\"}"), "Test", false);
        Assert.False(found.IsError);
        Assert.Contains("Agente prueba", found.Output);

        var denied = await tools.ExecuteAsync("crear", Json("{\"tipo\":\"task\",\"datos\":{\"title\":\"x\"}}"), "Test", false);
        Assert.True(denied.IsError);

        var created = await tools.ExecuteAsync("crear", Json("{\"tipo\":\"contact\",\"datos\":{\"name\":\"Rosa\",\"lastName\":\"Mora\",\"email\":\"rosa@test.com\"}}"), "Test · Dana", true);
        Assert.False(created.IsError);
        var id = System.Text.Json.JsonDocument.Parse(created.Output).RootElement.GetProperty("id").GetString()!;

        var updated = await tools.ExecuteAsync("actualizar", Json($"{{\"tipo\":\"contact\",\"id\":\"{id}\",\"datos\":{{\"phone\":\"8888-0000\"}}}}"), "Test · Dana", true);
        Assert.False(updated.IsError);
        var contact = await _f.Get<IRepository<Contact>>().GetByIdAsync(id);
        Assert.Equal("8888-0000", contact!.Phone);
        Assert.Equal("Test · Dana", contact.CreatedBy);

        var get = await tools.ExecuteAsync("obtener", Json($"{{\"tipo\":\"contact\",\"id\":\"{id}\"}}"), "Test", false);
        Assert.Contains("Rosa", get.Output);

        var stats = await tools.ExecuteAsync("estadisticas", Json("{}"), "Test", false);
        Assert.False(stats.IsError);
        Assert.Contains("prospectos", stats.Output);

        var unknown = await tools.ExecuteAsync("nada", Json("{}"), "Test", true);
        Assert.True(unknown.IsError);
    }

    [Fact]
    public async Task Agents_DefaultsSeeded_AndKeysProtected()
    {
        if (!_f.Available) return;
        var agents = _f.Get<Dana.Services.Ai.AgentService>();
        await agents.EnsureDefaultsAsync();
        var all = await agents.GetAllAsync();
        Assert.True(all.Count >= 4);
        Assert.Contains(all, a => a.Name == "Dana" && a.AllowWrites);

        var ai = new AiSettings { AnthropicKeyProtected = agents.Protect("sk-ant-secret") };
        Assert.NotEqual("sk-ant-secret", ai.AnthropicKeyProtected);
        Assert.Equal("sk-ant-secret", agents.KeyFor(ai, AiProviders.Anthropic));
        Assert.True(agents.IsConfigured(ai, AiProviders.Anthropic));
        Assert.False(agents.IsConfigured(ai, AiProviders.OpenAi));
        Assert.Equal(AiProviders.Anthropic, agents.ResolveProvider(new AiAgent { Provider = AiProviders.OpenAi }, ai));

        var conv = await agents.GetConversationAsync(all[0].Id!, "tester");
        Assert.NotNull(conv.Id);
        var again = await agents.GetConversationAsync(all[0].Id!, "tester");
        Assert.Equal(conv.Id, again.Id);
    }

    private static System.Text.Json.JsonElement Json(string s) => System.Text.Json.JsonDocument.Parse(s).RootElement.Clone();
}

[Collection("mongo")]
public class AgentScopeTests
{
    private readonly MongoFixture _f;
    public AgentScopeTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task Memory_RememberForgetAndScope()
    {
        if (!_f.Available) return;
        var agents = _f.Get<Dana.Services.Ai.AgentService>();
        await agents.EnsureDefaultsAsync();
        var a = (await agents.GetAllAsync())[0];
        var m1 = await agents.RememberAsync(a, null, "Los clientes principales son hoteles", "test");
        var m2 = await agents.RememberAsync(a, "ana", "Ana prefiere tablas", "ana");
        var dup = await agents.RememberAsync(a, null, "Los clientes principales son hoteles", "test");
        Assert.Equal(m1.Id, dup.Id);
        var all = await agents.GetMemoriesAsync(a.Id!);
        Assert.Contains(all, m => m.Id == m1.Id); Assert.Contains(all, m => m.Id == m2.Id);
        Assert.True(await agents.ForgetAsync(a, m2.Id!));
        Assert.False(await agents.ForgetAsync(a, m2.Id!));
        Assert.DoesNotContain(await agents.GetMemoriesAsync(a.Id!), m => m.Id == m2.Id);
    }

    [Fact]
    public async Task ConfirmableTool_CreatesPendingAction_AndResolves()
    {
        if (!_f.Available) return;
        var agents = _f.Get<Dana.Services.Ai.AgentService>();
        var tools = _f.Get<Dana.Services.Ai.AgentTools>();
        await agents.EnsureDefaultsAsync();
        var a = (await agents.GetAllAsync())[0];
        var conv = await agents.NewConversationAsync(a.Id!, "ana");
        var input = System.Text.Json.JsonDocument.Parse("{\"para\":\"cliente@test.com\",\"asunto\":\"Hola\",\"cuerpo\":\"Prueba\"}").RootElement.Clone();
        var r = await tools.ExecuteAsync("enviar_correo", input, new Dana.Services.Ai.ToolContext("ana · Dana", true, a, "ana", false, conv));
        Assert.False(r.IsError);
        Assert.Contains("PENDIENTE", r.Output);
        var action = conv.Messages.Single(m => m.Role == "action").Action!;
        Assert.Equal("pending", action.Status);
        var note = await agents.ResolveActionAsync(a, conv, action, true, "ana");
        Assert.Equal("done", action.Status);   // SMTP no configurado: queda registrado, no es error
        Assert.StartsWith("✔", note.Text);
        // sin usuario (rutina) no se permite
        var r2 = await tools.ExecuteAsync("enviar_correo", input, new Dana.Services.Ai.ToolContext("Sistema · Dana", true, a, Dana.Services.Ai.AgentService.SystemUser, false, conv));
        Assert.True(r2.IsError);
    }
}

[Collection("mongo")]
public class DocumentTests
{
    private readonly MongoFixture _f;
    public DocumentTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task Templates_FillVersionsAndWordExport()
    {
        if (!_f.Available) return;
        var docs = _f.Get<DocumentService>();
        var deals = _f.Get<DealService>();
        var companies = _f.Get<IRepository<Company>>();
        var pipes = _f.Get<PipelineService>();
        await pipes.EnsureDefaultAsync();
        var co = await companies.InsertAsync(new Company { Title = "Magic Globos" });
        var deal = await deals.SaveAsync(new Deal { Title = "Decoración evento", Amount = 1500, Currency = "USD", CompanyId = co.Id, PipelineId = (await pipes.GetDefaultAsync()).Id! }, "Test");

        var tpl = DocumentService.FindTemplate("nda")!;
        var body = await docs.FillAsync(tpl.Body, "deal", deal.Id, "Elena");
        Assert.Contains("Magic Globos", body);
        Assert.DoesNotContain("{{contraparte}}", body);
        Assert.Contains("1,500.00 USD", await docs.FillAsync("{{monto}}", "deal", deal.Id, null));

        var d = await docs.CreateAsync(new CrmDocument { Title = "NDA Magic Globos", Kind = "nda", Content = body, EntityType = "deal", EntityId = deal.Id }, "Elena");
        Assert.Equal("Decoración evento", d.EntityTitle);
        Assert.Equal(1, d.Version);
        d.Content += "\n\n## 7. Anexo\nTexto nuevo.";
        await docs.SaveAsync(d, "Elena", "Añadido anexo");
        var again = (await docs.GetAsync(d.Id))!;
        Assert.Equal(2, again.Version);
        Assert.Equal(2, again.Versions.Count);
        Assert.Single(await docs.GetForAsync("deal", deal.Id!));

        var bytes = await docs.ToDocxAsync(again);
        Assert.True(bytes.Length > 1000);
        var text = DocumentService.ReadDocx(bytes);
        Assert.Contains("ACUERDO DE CONFIDENCIALIDAD", text);
        Assert.Contains("Magic Globos", text);

        var html = DocumentService.ToHtml("| A | B |\n| --- | --- |\n| 1 | **2** |");
        Assert.Contains("<table class=\"doc-table\">", html);
        Assert.Contains("<strong>2</strong>", html);
    }

    [Fact]
    public async Task AgentTools_CreateAndUpdateDocument()
    {
        if (!_f.Available) return;
        var tools = _f.Get<Dana.Services.Ai.AgentTools>();
        var ctx = new Dana.Services.Ai.ToolContext("ana · Elena", true);
        var created = await tools.ExecuteAsync("documento_crear", Json("{\"titulo\":\"Carta prueba\",\"tipo\":\"carta\",\"plantilla\":\"carta\",\"asunto\":\"Bienvenida\"}"), ctx);
        Assert.False(created.IsError);
        var id = System.Text.Json.JsonDocument.Parse(created.Output).RootElement.GetProperty("id").GetString()!;
        var got = await tools.ExecuteAsync("documento_obtener", Json($"{{\"id\":\"{id}\"}}"), ctx);
        Assert.Contains("Bienvenida", got.Output);
        var upd = await tools.ExecuteAsync("documento_actualizar", Json($"{{\"id\":\"{id}\",\"contenido\":\"# Nueva\\n\\nTexto\",\"estado\":\"final\",\"nota\":\"revisión\"}}"), ctx);
        Assert.False(upd.IsError);
        Assert.Contains("\"version\":2", upd.Output);
        var found = await tools.ExecuteAsync("documentos_buscar", Json("{\"texto\":\"Carta prueba\"}"), ctx);
        Assert.Contains(id, found.Output);
        var denied = await tools.ExecuteAsync("documento_crear", Json("{\"titulo\":\"x\",\"tipo\":\"otro\",\"contenido\":\"y\"}"), new Dana.Services.Ai.ToolContext("ana", false));
        Assert.True(denied.IsError);
    }

    private static System.Text.Json.JsonElement Json(string s) => System.Text.Json.JsonDocument.Parse(s).RootElement.Clone();
}

[Collection("mongo")]
public class SignatureTests
{
    private readonly MongoFixture _f;
    public SignatureTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task RequestSignAndComplete()
    {
        if (!_f.Available) return;
        var docs = _f.Get<DocumentService>();
        var d = await docs.CreateAsync(new CrmDocument { Title = "Contrato firma", Kind = "contrato", Content = "# Contrato\n\nTexto." }, "ana");
        var s1 = await docs.RequestSignatureAsync(d, "Yamileth Hernández", "y@test.com", "Gerente", "ana");
        var s2 = await docs.RequestSignatureAsync(d, "Steven Gazo", null, null, "ana");
        Assert.Equal("review", d.Status);
        Assert.True(s1.Token.Length >= 32);
        Assert.Null(await docs.FindByTokenAsync("nope"));
        var signed = await docs.SignAsync(s1.Token, "Yamileth Hernández", "Gerente General", "data:image/png;base64,iVBORw0KGgo=", "127.0.0.1");
        Assert.NotNull(signed);
        Assert.Equal("review", signed!.Status);
        Assert.Null(await docs.SignAsync(s1.Token, "otra vez", null, null, null));   // ya firmado
        var done = await docs.SignAsync(s2.Token, "Steven Gazo", null, null, null);
        Assert.Equal("signed", done!.Status);
        Assert.True(done.AllSigned);
        Assert.Contains("Firmas", DocumentService.SignaturesHtml(done));
        Assert.Contains("ACUERDO", DocumentService.ReadDocx(await docs.ToDocxAsync(new CrmDocument { Title = "x", Content = "# ACUERDO", Signers = done.Signers })));
        Assert.Contains("firmado el", DocumentService.ReadDocx(await docs.ToDocxAsync(done)));
    }

    [Fact]
    public async Task Email_WithAttachment_IsLoggedWithoutSmtp()
    {
        if (!_f.Available) return;
        var email = _f.Get<EmailService>();
        var (sent, error) = await email.SendAsync("a@b.com", "Asunto", "Cuerpo", "document", "x", "Doc", "ana", [new EmailService.EmailAttachment("doc.docx", [1, 2, 3], "application/octet-stream")]);
        Assert.False(sent); Assert.Null(error);
        var acts = await _f.Get<ActivityService>().GetForAsync("document", "x");
        Assert.Contains(acts, a => a.Text.Contains("Adjuntos: doc.docx"));
    }
}

[Collection("mongo")]
public class WordPdfTests
{
    private readonly MongoFixture _f;
    public WordPdfTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task Pdf_Export_And_Docx_RoundTrip_Import()
    {
        if (!_f.Available) return;
        var docs = _f.Get<DocumentService>();
        var d = new CrmDocument { Title = "Contrato PDF", Kind = "contrato", Content = "# CONTRATO DE PRUEBA\n\nPárrafo con **negrita** y acentos: ñáéíóú.\n\n## Partes\n- Uno\n- Dos\n\n| Concepto | Importe |\n| --- | --- |\n| Servicio | 100 |\n| **Total** | **100** |", Signers = [new DocumentSigner { Name = "Ana", SignedAt = DateTime.UtcNow, SignedName = "Ana Mora", Ip = "1.2.3.4" }] };
        var pdf = await docs.ToPdfAsync(d, "/home/steven.gazo/Repositories/Test/Dana/wwwroot");
        Assert.True(pdf.Length > 2000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        using (var ms = new MemoryStream(pdf)) Assert.Contains("CONTRATO DE PRUEBA", DocumentService.PdfToMarkdown(ms));

        var docx = await docs.ToDocxAsync(d);
        using var dms = new MemoryStream(docx);
        var md = DocumentService.DocxToMarkdown(dms);
        Assert.Contains("CONTRATO DE PRUEBA", md);
        Assert.Contains("| Concepto | Importe |", md);
        Assert.Contains("**negrita**", md);
        Assert.Contains("| Servicio | 100 |", md);

        using var txt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("# Hola\n\nTexto"));
        Assert.Equal("# Hola\n\nTexto", DocumentService.ImportToMarkdown("nota.md", txt));
        Assert.Throws<InvalidOperationException>(() => DocumentService.ImportToMarkdown("x.exe", new MemoryStream()));
    }
}

[Collection("mongo")]
public class TemplateAndReminderTests
{
    private readonly MongoFixture _f;
    public TemplateAndReminderTests(MongoFixture f) { _f = f; }

    [Fact]
    public async Task CustomTemplates_MergeWithBuiltins_AndAgentCanUseThem()
    {
        if (!_f.Available) return;
        var docs = _f.Get<DocumentService>();
        var before = (await docs.GetAllTemplatesAsync()).Count;
        var t = new DocumentTemplateEntity { Title = "Contrato de mantenimiento", Kind = "contrato", Body = "# MANTENIMIENTO\n\nCliente: {{contraparte}}" };
        await docs.SaveTemplateAsync(t, "ana");
        var all = await docs.GetAllTemplatesAsync();
        Assert.Equal(before + 1, all.Count);
        Assert.NotNull(await docs.FindTemplateAsync("mantenimiento"));
        Assert.NotNull(await docs.FindTemplateAsync(t.Id!));
        var tools = _f.Get<Dana.Services.Ai.AgentTools>();
        var r = await tools.ExecuteAsync("documento_crear", System.Text.Json.JsonDocument.Parse("{\"titulo\":\"Mant ACME\",\"tipo\":\"contrato\",\"plantilla\":\"Contrato de mantenimiento\"}").RootElement.Clone(), new Dana.Services.Ai.ToolContext("ana · Elena", true));
        Assert.False(r.IsError);
        var id = System.Text.Json.JsonDocument.Parse(r.Output).RootElement.GetProperty("id").GetString();
        Assert.Contains("MANTENIMIENTO", (await docs.GetAsync(id))!.Content);
        var list = await tools.ExecuteAsync("documentos_plantillas", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(), new Dana.Services.Ai.ToolContext("ana", false));
        Assert.Contains("Contrato de mantenimiento", list.Output);
        await docs.DeleteTemplateAsync(t.Id!, "ana");
        Assert.Equal(before, (await docs.GetAllTemplatesAsync()).Count);
    }

    [Fact]
    public async Task SignatureReminders_AreDueAfterThreeDays_AndNotRepeatedTooSoon()
    {
        if (!_f.Available) return;
        var now = DateTime.UtcNow;
        var fresh = new DocumentSigner { Name = "A", RequestedAt = now.AddDays(-1) };
        var old = new DocumentSigner { Name = "B", RequestedAt = now.AddDays(-4) };
        var reminded = new DocumentSigner { Name = "C", RequestedAt = now.AddDays(-10), LastReminderAt = now.AddDays(-1) };
        var signed = new DocumentSigner { Name = "D", RequestedAt = now.AddDays(-10), SignedAt = now };
        Assert.False(DocumentService.IsReminderDue(fresh, now));
        Assert.True(DocumentService.IsReminderDue(old, now));
        Assert.False(DocumentService.IsReminderDue(reminded, now));
        Assert.False(DocumentService.IsReminderDue(signed, now));

        var docs = _f.Get<DocumentService>();
        var d = await docs.CreateAsync(new CrmDocument { Title = "Recordatorio", Content = "x" }, "ana");
        var sg = await docs.RequestSignatureAsync(d, "Pendiente", null, null, "ana");
        sg.RequestedAt = now.AddDays(-5);
        await docs.SaveAsync(d, "ana");
        var sent = await docs.SendSignatureRemindersAsync(_f.Get<EmailService>(), "http://localhost");
        Assert.True(sent >= 1);
        var again = (await docs.GetAsync(d.Id))!;
        Assert.NotNull(again.Signers[0].LastReminderAt);
        Assert.Contains(await _f.Get<NotificationService>().GetForAsync("ana"), n => n.Text.Contains("aún no ha firmado"));
    }
}
