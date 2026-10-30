using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public record ImportField(string Key, string Label, bool Required = false);
public record ImportPreview(List<string> Columns, List<string[]> Rows, int TotalRows);
public record ImportResult(int Created, int Skipped, List<string> Errors);

/// <summary>Exportación a Excel/CSV e importación desde Excel/CSV con mapeo de columnas.</summary>
public class ExportImportService
{
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly IRepository<Deal> _deals;
    private readonly PipelineService _pipelines;
    private readonly BoardConfigService _configs;
    private readonly LeadService _leadService;
    private readonly DealService _dealService;
    private readonly ActivityService _activities;

    public ExportImportService(IRepository<Lead> leads, IRepository<Contact> contacts, IRepository<Company> companies, IRepository<Deal> deals,
        PipelineService pipelines, BoardConfigService configs, LeadService leadService, DealService dealService, ActivityService activities)
    {
        _leads = leads; _contacts = contacts; _companies = companies; _deals = deals; _pipelines = pipelines; _configs = configs;
        _leadService = leadService; _dealService = dealService; _activities = activities;
    }

    // ---------------- Exportación ----------------
    public async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(string board)
    {
        var cfg = await _configs.GetAsync(board);
        var custom = cfg.CustomColumns;
        var pipelines = (await _pipelines.GetAllAsync()).ToDictionary(p => p.Id!);
        var companies = (await _companies.GetAllAsync()).ToDictionary(c => c.Id!, c => c.Title);
        var contacts = (await _contacts.GetAllAsync()).ToDictionary(c => c.Id!, c => c.FullName);
        List<string> headers; List<object?[]> rows = new();

        object? Custom(Entity e, CustomColumnDef d) => e.Custom.GetValueOrDefault(d.Id);
        switch (board)
        {
            case "leads":
                headers = ["Título", "Estado", "Nombre", "Apellido", "Cargo", "Compañía", "Teléfono", "Correo", "Fuente", "Oportunidad", "Moneda", "Responsable", "Comentarios", "Creado", "Archivado"];
                foreach (var l in await _leads.GetAllAsync())
                    rows.Add([l.Title, LeadStatuses.Get(l.Status).Name, l.Name, l.LastName, l.Position, l.CompanyName, l.Phone, l.Email, l.Source, l.Opportunity, l.Currency, l.AssignedTo, l.Comments, l.CreatedAt.ToLocalTime(), l.IsArchived ? "Sí" : "No", .. custom.Select(d => Custom(l, d))]);
                break;
            case "contacts":
                headers = ["Nombre", "Apellido", "Cargo", "Compañía", "Tipo", "Teléfono", "Correo", "Fuente", "Dirección", "Responsable", "Comentarios", "Creado", "Archivado"];
                foreach (var c in await _contacts.GetAllAsync())
                    rows.Add([c.Name, c.LastName, c.Position, c.CompanyId is null ? null : companies.GetValueOrDefault(c.CompanyId), c.Type, c.Phone, c.Email, c.Source, c.Address, c.AssignedTo, c.Comments, c.CreatedAt.ToLocalTime(), c.IsArchived ? "Sí" : "No", .. custom.Select(d => Custom(c, d))]);
                break;
            case "companies":
                headers = ["Nombre", "Tipo", "Industria", "Id. fiscal", "Teléfono", "Correo", "Sitio web", "Dirección", "Ingresos anuales", "Empleados", "Responsable", "Comentarios", "Creado", "Archivado"];
                foreach (var k in await _companies.GetAllAsync())
                    rows.Add([k.Title, k.Type, k.Industry, k.TaxId, k.Phone, k.Email, k.Web, k.Address, k.AnnualRevenue, k.Employees, k.AssignedTo, k.Comments, k.CreatedAt.ToLocalTime(), k.IsArchived ? "Sí" : "No", .. custom.Select(d => Custom(k, d))]);
                break;
            default:
                headers = ["Título", "Embudo", "Etapa", "Contacto", "Compañía", "Importe", "Moneda", "Probabilidad", "Cierre previsto", "Cerrada", "Tipo", "Fuente", "Responsable", "Comentarios", "Creada", "Archivada"];
                foreach (var d in await _deals.GetAllAsync())
                {
                    var p = pipelines.GetValueOrDefault(d.PipelineId);
                    rows.Add([d.Title, p?.Name, p?.GetStage(d.StageId)?.Name, d.ContactId is null ? null : contacts.GetValueOrDefault(d.ContactId), d.CompanyId is null ? null : companies.GetValueOrDefault(d.CompanyId), d.Amount, d.Currency, d.Probability, d.ExpectedCloseDate, d.ClosedAt?.ToLocalTime(), d.Type, d.Source, d.AssignedTo, d.Comments, d.CreatedAt.ToLocalTime(), d.IsArchived ? "Sí" : "No", .. custom.Select(x => Custom(d, x))]);
                }
                break;
        }
        headers.AddRange(custom.Select(d => d.Title));
        return (headers, rows);
    }

    public async Task<byte[]> ExportXlsxAsync(string board)
    {
        var (headers, rows) = await BuildExportAsync(board);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(BoardTitle(board));
        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#e6f1fd");
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c];
                var cell = ws.Cell(r + 2, c + 1);
                switch (v)
                {
                    case null: break;
                    case decimal dec: cell.Value = dec; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; break;
                    default: cell.Value = v.ToString(); break;
                }
            }
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200), 8, 50);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportCsvAsync(string board)
    {
        var (headers, rows) = await BuildExportAsync(board);
        var sb = new StringBuilder();
        string Esc(object? v) { var s = v switch { null => "", DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"), decimal d => d.ToString(CultureInfo.InvariantCulture), _ => v.ToString() ?? "" }; return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        sb.AppendLine(string.Join(",", headers.Select(Esc)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Esc)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static string BoardTitle(string board) => board switch { "leads" => "Prospectos", "contacts" => "Contactos", "companies" => "Compañías", _ => "Negociaciones" };

    // ---------------- Importación ----------------
    public static List<ImportField> FieldsFor(string board) => board switch
    {
        "leads" => [new("title", "Título"), new("name", "Nombre"), new("lastName", "Apellido"), new("company", "Compañía"), new("phone", "Teléfono"), new("email", "Correo"), new("source", "Fuente"), new("status", "Estado"), new("opportunity", "Oportunidad"), new("assignedTo", "Responsable"), new("comments", "Comentarios")],
        "contacts" => [new("name", "Nombre", true), new("lastName", "Apellido"), new("position", "Cargo"), new("company", "Compañía (nombre)"), new("type", "Tipo"), new("phone", "Teléfono"), new("email", "Correo"), new("source", "Fuente"), new("address", "Dirección"), new("assignedTo", "Responsable"), new("comments", "Comentarios")],
        "companies" => [new("title", "Nombre", true), new("type", "Tipo"), new("industry", "Industria"), new("taxId", "Id. fiscal"), new("phone", "Teléfono"), new("email", "Correo"), new("web", "Sitio web"), new("address", "Dirección"), new("revenue", "Ingresos anuales"), new("employees", "Empleados"), new("assignedTo", "Responsable"), new("comments", "Comentarios")],
        _ => [new("title", "Título", true), new("stage", "Etapa (nombre)"), new("contact", "Contacto (nombre)"), new("company", "Compañía (nombre)"), new("amount", "Importe"), new("probability", "Probabilidad %"), new("closeDate", "Cierre previsto"), new("type", "Tipo"), new("source", "Fuente"), new("assignedTo", "Responsable"), new("comments", "Comentarios")]
    };

    /// <summary>Lee un .xlsx o .csv y devuelve columnas y filas (como texto).</summary>
    public static ImportPreview Parse(Stream stream, string fileName, int previewRows = 5)
    {
        var columns = new List<string>(); var rows = new List<string[]>();
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = reader.ReadToEnd();
            var sep = text.Split('\n')[0].Count(c => c == ';') > text.Split('\n')[0].Count(c => c == ',') ? ';' : ',';
            var all = ParseCsv(text, sep);
            if (all.Count == 0) return new(columns, rows, 0);
            columns = all[0].ToList();
            rows = all.Skip(1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        }
        else
        {
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var range = ws.RangeUsed();
            if (range is null) return new(columns, rows, 0);
            var first = range.FirstRow();
            columns = first.Cells().Select(c => c.GetString().Trim()).ToList();
            foreach (var row in range.RowsUsed().Skip(1))
            {
                var vals = new string[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = row.Cell(i + 1);
                    vals[i] = cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd") : cell.GetFormattedString().Trim();
                }
                if (vals.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(vals);
            }
        }
        return new(columns, rows, rows.Count);
    }

    private static List<string[]> ParseCsv(string text, char sep)
    {
        var result = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == sep) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n' || ch == '\r') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(field.ToString()); field.Clear(); result.Add(row.ToArray()); row.Clear(); }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row.ToArray()); }
        return result;
    }

    /// <summary>Sugiere un mapeo columna→campo por nombre.</summary>
    public static Dictionary<string, string> SuggestMapping(string board, List<string> columns)
    {
        var map = new Dictionary<string, string>();
        var synonyms = new Dictionary<string, string[]>
        {
            ["title"] = ["título", "titulo", "title", "nombre de la negociación", "asunto"], ["name"] = ["nombre", "name", "first name", "firstname"], ["lastName"] = ["apellido", "apellidos", "last name", "lastname"],
            ["company"] = ["compañía", "compania", "empresa", "company", "organización"], ["phone"] = ["teléfono", "telefono", "phone", "celular", "móvil"], ["email"] = ["correo", "email", "e-mail", "mail"],
            ["source"] = ["fuente", "source", "origen"], ["status"] = ["estado", "status"], ["opportunity"] = ["oportunidad", "monto", "opportunity"], ["assignedTo"] = ["responsable", "asignado", "owner", "vendedor"],
            ["comments"] = ["comentarios", "comments", "notas", "mensaje"], ["position"] = ["cargo", "puesto", "position", "title"], ["type"] = ["tipo", "type"], ["address"] = ["dirección", "direccion", "address"],
            ["industry"] = ["industria", "industry", "sector"], ["taxId"] = ["id. fiscal", "cédula", "cedula", "rut", "nit", "tax id"], ["web"] = ["sitio web", "web", "website", "url"], ["revenue"] = ["ingresos", "revenue"], ["employees"] = ["empleados", "employees"],
            ["stage"] = ["etapa", "stage"], ["contact"] = ["contacto", "contact"], ["amount"] = ["importe", "monto", "amount", "valor"], ["probability"] = ["probabilidad", "probability"], ["closeDate"] = ["cierre", "fecha de cierre", "close date"],
        };
        var fields = FieldsFor(board).Select(f => f.Key).ToHashSet();
        foreach (var col in columns)
        {
            var c = col.Trim().ToLowerInvariant();
            foreach (var (field, syns) in synonyms)
                if (fields.Contains(field) && !map.ContainsValue(field) && syns.Any(s => s == c)) { map[col] = field; break; }
        }
        return map;
    }

    public async Task<ImportResult> ImportAsync(string board, List<string> columns, List<string[]> rows, Dictionary<string, string> mapping, string author)
    {
        var result = new ImportResult(0, 0, new());
        var created = 0; var skipped = 0;
        string? Get(string[] row, string field)
        {
            var col = mapping.FirstOrDefault(kv => kv.Value == field).Key;
            if (col is null) return null;
            var idx = columns.IndexOf(col);
            return idx < 0 || idx >= row.Length ? null : string.IsNullOrWhiteSpace(row[idx]) ? null : row[idx].Trim();
        }
        decimal Dec(string? s) => decimal.TryParse(s?.Replace("$", "").Replace(" ", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : decimal.TryParse(s, NumberStyles.Any, new CultureInfo("es-ES"), out var d2) ? d2 : 0;

        var companies = (await _companies.GetAllAsync()).ToList();
        var contacts = (await _contacts.GetAllAsync()).ToList();
        var pipeline = await _pipelines.GetDefaultAsync();

        async Task<string?> CompanyIdAsync(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var found = companies.FirstOrDefault(c => string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase));
            if (found is null) { found = await _companies.InsertAsync(new Company { Title = name.Trim(), Type = "Cliente" }); companies.Add(found); }
            return found.Id;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                switch (board)
                {
                    case "leads":
                    {
                        var title = Get(row, "title") ?? $"{Get(row, "name")} {Get(row, "lastName")}".Trim();
                        if (string.IsNullOrWhiteSpace(title) && Get(row, "email") is null) { skipped++; continue; }
                        var status = LeadStatuses.All.FirstOrDefault(s => string.Equals(s.Name, Get(row, "status"), StringComparison.OrdinalIgnoreCase))?.Id ?? LeadStatuses.New;
                        await _leadService.SaveAsync(new Lead { Title = string.IsNullOrWhiteSpace(title) ? Get(row, "email")! : title, Name = Get(row, "name"), LastName = Get(row, "lastName"), CompanyName = Get(row, "company"), Phone = Get(row, "phone"), Email = Get(row, "email"), Source = Get(row, "source") ?? "Otro", Status = status, Opportunity = Dec(Get(row, "opportunity")), AssignedTo = Get(row, "assignedTo"), Comments = Get(row, "comments") }, author);
                        break;
                    }
                    case "contacts":
                    {
                        var name = Get(row, "name");
                        if (name is null) { skipped++; continue; }
                        var c = new Contact { Name = name, LastName = Get(row, "lastName"), Position = Get(row, "position"), CompanyId = await CompanyIdAsync(Get(row, "company")), Type = Get(row, "type"), Phone = Get(row, "phone"), Email = Get(row, "email"), Source = Get(row, "source"), Address = Get(row, "address"), AssignedTo = Get(row, "assignedTo"), Comments = Get(row, "comments") };
                        await _contacts.InsertAsync(c); contacts.Add(c);
                        await _activities.LogAsync("contact", c.Id!, c.FullName, ActivityKinds.Created, "Importado desde archivo", author);
                        break;
                    }
                    case "companies":
                    {
                        var title = Get(row, "title");
                        if (title is null) { skipped++; continue; }
                        if (companies.Any(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                        var k = new Company { Title = title, Type = Get(row, "type"), Industry = Get(row, "industry"), TaxId = Get(row, "taxId"), Phone = Get(row, "phone"), Email = Get(row, "email"), Web = Get(row, "web"), Address = Get(row, "address"), AnnualRevenue = Get(row, "revenue") is null ? null : Dec(Get(row, "revenue")), Employees = int.TryParse(Get(row, "employees"), out var emp) ? emp : null, AssignedTo = Get(row, "assignedTo"), Comments = Get(row, "comments") };
                        await _companies.InsertAsync(k); companies.Add(k);
                        await _activities.LogAsync("company", k.Id!, k.Title, ActivityKinds.Created, "Importada desde archivo", author);
                        break;
                    }
                    default:
                    {
                        var title = Get(row, "title");
                        if (title is null) { skipped++; continue; }
                        var stageName = Get(row, "stage");
                        var stage = pipeline.Stages.FirstOrDefault(s => string.Equals(s.Name, stageName, StringComparison.OrdinalIgnoreCase)) ?? pipeline.FirstStage!;
                        var contactName = Get(row, "contact");
                        var contact = contactName is null ? null : contacts.FirstOrDefault(c => string.Equals(c.FullName, contactName, StringComparison.OrdinalIgnoreCase));
                        DateTime? close = DateTime.TryParse(Get(row, "closeDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var cd) ? cd.Date : DateTime.TryParse(Get(row, "closeDate"), new CultureInfo("es-ES"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var cd2) ? cd2.Date : null;
                        await _dealService.SaveAsync(new Deal { Title = title, PipelineId = pipeline.Id!, StageId = stage.Id, ContactId = contact?.Id, CompanyId = await CompanyIdAsync(Get(row, "company")) ?? contact?.CompanyId, Amount = Dec(Get(row, "amount")), Probability = int.TryParse(Get(row, "probability")?.Replace("%", ""), out var pr) ? Math.Clamp(pr, 0, 100) : 50, ExpectedCloseDate = close, Type = Get(row, "type"), Source = Get(row, "source"), AssignedTo = Get(row, "assignedTo"), Comments = Get(row, "comments") }, author);
                        break;
                    }
                }
                created++;
            }
            catch (Exception ex) { result.Errors.Add($"Fila {i + 2}: {ex.Message}"); }
        }
        return result with { Created = created, Skipped = skipped };
    }
}
