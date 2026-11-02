using Dana.Models;

namespace Dana.Services;

public record NavItemDef(string Id, string Label, string Href, string Icon, string Section, bool AdminOnly = false, bool MatchAll = false);

public record NavItem(string Id, string Label, string Href, string Icon, string Section, bool MatchAll, bool IsCustom, bool NewTab, bool Hidden, int Sort, bool AdminOnly);

/// <summary>Catálogo del menú y resolución de la configuración personal de cada usuario.</summary>
public class NavService
{
    public static readonly string[] Sections = ["Principal", "Tableros", "Ventas", "Análisis", "IA", "Configuración", "Ayuda"];

    public static readonly NavItemDef[] Catalog =
    [
        new("home", "Inicio", "", "home", "Principal", MatchAll: true),
        new("mywork", "Mi trabajo", "mi-trabajo", "check", "Principal"),
        new("chat", "Chat", "chat", "chat", "Principal"),
        new("leads", "Prospectos", "leads", "dot:#a25ddc", "Tableros"),
        new("deals", "Negociaciones", "deals", "dot:#0073ea", "Tableros"),
        new("contacts", "Contactos", "contacts", "dot:#00c875", "Tableros"),
        new("companies", "Compañías", "companies", "dot:#fdab3d", "Tableros"),
        new("quotes", "Cotizaciones", "cotizaciones", "doc", "Ventas"),
        new("products", "Productos", "productos", "box", "Ventas"),
        new("invoices", "Facturas", "facturas", "doc", "Ventas"),
        new("documents", "Documentos", "documentos", "doc", "Ventas"),
        new("templates", "Plantillas de correo", "plantillas", "mail", "Ventas"),
        new("forms", "Formularios", "formularios", "form", "Ventas"),
        new("reports", "Informes", "informes", "chart", "Análisis"),
        new("calendar", "Calendario", "calendario", "calendar", "Análisis"),
        new("timeline", "Cronograma", "cronograma", "gantt", "Análisis"),
        new("import", "Importar", "importar", "import", "Análisis"),
        new("office", "Oficina", "oficina", "office", "IA"),
        new("agents", "Agentes", "agentes", "robot", "IA"),
        new("settings", "Configuración", "configuracion", "gear", "Configuración", MatchAll: true),
        new("pipelines", "Embudos", "pipelines", "list", "Configuración"),
        new("automations", "Automatizaciones", "automatizaciones", "bolt", "Configuración"),
        new("trash", "Papelera", "papelera", "trash", "Configuración"),
        new("company", "Empresa y moneda", "configuracion/empresa", "building", "Configuración", AdminOnly: true),
        new("audit", "Auditoría", "auditoria", "clock", "Configuración", AdminOnly: true),
        new("api", "API de prospectos", "configuracion/api", "code", "Configuración", AdminOnly: true),
        new("users", "Usuarios", "usuarios", "users", "Configuración", AdminOnly: true),
        new("ai", "Proveedores de IA", "configuracion/ia", "spark", "Configuración", AdminOnly: true),
        new("tutorial", "Tutorial", "tutorial", "help", "Ayuda"),
    ];

    private readonly UserService _users;
    public NavService(UserService users) { _users = users; }

    /// <summary>Lista final de elementos (catálogo + preferencias del usuario), en orden, incluyendo los ocultos (Hidden=true).</summary>
    public static List<NavItem> Resolve(MenuConfig? cfg, bool isAdmin)
    {
        var prefs = (cfg?.Items ?? new()).ToDictionary(p => p.Id);
        var items = new List<NavItem>();
        var i = 0;
        foreach (var d in Catalog)
        {
            if (d.AdminOnly && !isAdmin) continue;
            prefs.TryGetValue(d.Id, out var p);
            items.Add(new NavItem(d.Id, p?.Label ?? d.Label, d.Href, d.Icon, p?.Section ?? d.Section, d.MatchAll, false, false, p?.Hidden ?? false, p?.Sort ?? i * 10, d.AdminOnly));
            i++;
        }
        foreach (var p in cfg?.Items.Where(p => p.Url is not null) ?? [])
            items.Add(new NavItem(p.Id, p.Label ?? p.Url!, p.Url!, "custom:" + (p.Icon ?? "🔗"), p.Section ?? "Principal", false, true, p.OpenInNewTab, p.Hidden, p.Sort, false));
        return items.OrderBy(x => x.Sort).ToList();
    }

    public static List<string> SectionOrder(MenuConfig? cfg) =>
        (cfg?.SectionOrder is { Count: > 0 } so ? so.Concat(Sections.Where(s => !so.Contains(s))) : Sections).ToList();

    public async Task<MenuConfig?> GetAsync(string userName) => (await _users.FindByNameAsync(userName))?.Menu;

    public async Task SaveAsync(string userName, MenuConfig? cfg)
    {
        var u = await _users.FindByNameAsync(userName);
        if (u is null) return;
        u.Menu = cfg;
        await _users.SavePermissionsAsync(u);
    }

    /// <summary>Convierte la lista resuelta (ya editada) en preferencias a guardar.</summary>
    public static MenuConfig ToConfig(List<NavItem> items, List<string> sectionOrder)
    {
        var cfg = new MenuConfig { SectionOrder = sectionOrder };
        foreach (var it in items)
        {
            var def = Catalog.FirstOrDefault(d => d.Id == it.Id);
            if (it.IsCustom)
                cfg.Items.Add(new MenuItemPref { Id = it.Id, Label = it.Label, Section = it.Section, Hidden = it.Hidden, Sort = it.Sort, Url = it.Href, Icon = it.Icon.Replace("custom:", ""), OpenInNewTab = it.NewTab });
            else if (def is not null && (it.Label != def.Label || it.Section != def.Section || it.Hidden || it.Sort != Array.IndexOf(Catalog, def) * 10))
                cfg.Items.Add(new MenuItemPref { Id = it.Id, Label = it.Label == def.Label ? null : it.Label, Section = it.Section == def.Section ? null : it.Section, Hidden = it.Hidden, Sort = it.Sort });
        }
        return cfg;
    }

    public static readonly Dictionary<string, string> Icons = new()
    {
        ["home"] = "<path d=\"M3 9.5 10 3l7 6.5V17a1 1 0 0 1-1 1h-4v-5H8v5H4a1 1 0 0 1-1-1V9.5Z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        ["chat"] = "<path d=\"M3 5.5A1.5 1.5 0 0 1 4.5 4h11A1.5 1.5 0 0 1 17 5.5v6A1.5 1.5 0 0 1 15.5 13H8l-4 3v-3H4.5A1.5 1.5 0 0 1 3 11.5z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        ["check"] = "<path d=\"M4 10.5l4 4 8-9\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
        ["doc"] = "<path d=\"M5 3h7l4 4v10H5z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/><path d=\"M12 3v4h4M7 10h6M7 13h6\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
        ["box"] = "<path d=\"M3 6l7-3 7 3v8l-7 3-7-3zM3 6l7 3 7-3M10 9v8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        ["mail"] = "<rect x=\"3\" y=\"5\" width=\"14\" height=\"10\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M3.5 6l6.5 5 6.5-5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/>",
        ["form"] = "<rect x=\"4\" y=\"3\" width=\"12\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M7 7h6M7 10h6M7 13h4\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
        ["chart"] = "<path d=\"M3 16h14M5 13V9M9 13V5M13 13V8M17 13v-3\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>",
        ["calendar"] = "<rect x=\"3\" y=\"4\" width=\"14\" height=\"13\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M3 8h14M7 2v4M13 2v4\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
        ["gantt"] = "<path d=\"M3 5h8M6 10h10M4 15h7\" stroke=\"currentColor\" stroke-width=\"3\" stroke-linecap=\"round\"/>",
        ["import"] = "<path d=\"M10 3v10m0 0-4-4m4 4 4-4M4 16h12\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
        ["office"] = "<path d=\"M3 17V5l7-2v14M10 17V9l7-2v10M3 17h14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/><path d=\"M5.5 7h2M5.5 10h2M5.5 13h2M12.5 11h2M12.5 14h2\" stroke=\"currentColor\" stroke-width=\"1.3\"/>",
        ["robot"] = "<rect x=\"4\" y=\"7\" width=\"12\" height=\"9\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M10 7V4M8 4h4\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/><circle cx=\"7.5\" cy=\"11\" r=\"1.2\" fill=\"currentColor\"/><circle cx=\"12.5\" cy=\"11\" r=\"1.2\" fill=\"currentColor\"/><path d=\"M8 14h4\" stroke=\"currentColor\" stroke-width=\"1.4\" stroke-linecap=\"round\"/>",
        ["spark"] = "<path d=\"M10 2l1.8 5.2L17 9l-5.2 1.8L10 16l-1.8-5.2L3 9l5.2-1.8z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/><path d=\"M16 14l.7 1.8 1.8.7-1.8.7L16 19l-.7-1.8-1.8-.7 1.8-.7z\" fill=\"currentColor\"/>",
        ["gear"] = "<path d=\"M10 6.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zM8.6 2h2.8l.4 2.1a6.2 6.2 0 0 1 1.5.9l2-.7 1.4 2.4-1.6 1.4a6.3 6.3 0 0 1 0 1.8l1.6 1.4-1.4 2.4-2-.7a6.2 6.2 0 0 1-1.5.9l-.4 2.1H8.6l-.4-2.1a6.2 6.2 0 0 1-1.5-.9l-2 .7-1.4-2.4 1.6-1.4a6.3 6.3 0 0 1 0-1.8L3.3 6.7l1.4-2.4 2 .7a6.2 6.2 0 0 1 1.5-.9z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>",
        ["list"] = "<path d=\"M3 5h14M3 10h10M3 15h6\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>",
        ["bolt"] = "<path d=\"M11 2 4 11h5l-1 7 7-9h-5l1-7z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        ["trash"] = "<path d=\"M4 6h12M8 6V4h4v2M6 6l1 11h6l1-11\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
        ["building"] = "<path d=\"M3 17V7l5-3v13M8 17V9l5-2v10M13 17V11l4-1v7M2 17h16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        ["clock"] = "<circle cx=\"10\" cy=\"10\" r=\"7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M10 6v4l3 2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
        ["code"] = "<path d=\"M7 6 3 10l4 4M13 6l4 4-4 4M11 4l-2 12\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
        ["users"] = "<circle cx=\"8\" cy=\"7\" r=\"3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M2.5 16c.6-3 3-4.5 5.5-4.5s4.9 1.5 5.5 4.5M13 8.5a2.5 2.5 0 1 0 0-5M14.5 11.5c2 .4 3 1.8 3.3 4.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
        ["help"] = "<circle cx=\"10\" cy=\"10\" r=\"7.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\"/><path d=\"M8 8a2 2 0 1 1 3 1.7c-.7.4-1 .8-1 1.5M10 14h.01\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>",
    };
}
