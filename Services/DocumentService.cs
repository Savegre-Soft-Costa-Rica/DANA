using System.Text;
using System.Text.RegularExpressions;
using Dana.Data;
using Dana.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Dana.Services;

/// <summary>Documentos del CRM: plantillas legales/comerciales, versiones, exportación a Word y extracción de texto de adjuntos.</summary>
public partial class DocumentService
{
    public record Template(string Id, string Kind, string Title, string Description, string Body);

    private readonly IRepository<CrmDocument> _docs;
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly IRepository<Deal> _deals;
    private readonly SettingsService _settings;
    private readonly ActivityService _activities;
    private readonly AttachmentService _attachments;
    private readonly NotificationService _notifications;
    private readonly IRepository<DocumentTemplateEntity> _templates;

    public DocumentService(IRepository<CrmDocument> docs, IRepository<Lead> leads, IRepository<Contact> contacts, IRepository<Company> companies, IRepository<Deal> deals, SettingsService settings, ActivityService activities, AttachmentService attachments, NotificationService notifications, IRepository<DocumentTemplateEntity> templates)
    {
        _notifications = notifications; _templates = templates;
        _docs = docs; _leads = leads; _contacts = contacts; _companies = companies; _deals = deals; _settings = settings; _activities = activities; _attachments = attachments;
    }

    // ---------- plantillas ----------
    public static readonly Template[] Templates =
    [
        new("nda", "nda", "Acuerdo de confidencialidad (NDA)", "Protección de información confidencial entre {{empresa}} y la contraparte.",
            """
            # ACUERDO DE CONFIDENCIALIDAD

            **Fecha:** {{fecha}}

            ## Partes
            1. **{{empresa}}** ("la Parte Reveladora"), con identificación fiscal {{empresa_id}}, domicilio en {{empresa_direccion}}.
            2. **{{contraparte}}** ("la Parte Receptora"), representada por {{contacto}}.

            ## 1. Objeto
            Regular el intercambio de información confidencial con motivo de {{asunto}}.

            ## 2. Información confidencial
            Toda información técnica, comercial, financiera o de clientes, en cualquier soporte, identificada como confidencial o que razonablemente deba considerarse como tal.

            ## 3. Obligaciones de la Parte Receptora
            - Usar la información únicamente para el fin indicado.
            - No divulgarla a terceros sin autorización escrita.
            - Aplicar medidas de protección equivalentes a las que usa con su propia información.
            - Devolver o destruir la información al finalizar la relación.

            ## 4. Excepciones
            No se considera confidencial la información pública, la ya conocida legítimamente por la Parte Receptora o la que deba revelarse por mandato legal.

            ## 5. Vigencia
            Este acuerdo rige desde la firma y las obligaciones de confidencialidad se mantienen por **3 años** tras su terminación.

            ## 6. Ley aplicable y jurisdicción
            Se rige por las leyes de {{pais}}. Cualquier controversia se someterá a los tribunales de {{ciudad}}.

            ## Firmas

            | {{empresa}} | {{contraparte}} |
            | --- | --- |
            | Nombre: ______________________ | Nombre: {{contacto}} |
            | Cargo: _______________________ | Cargo: ______________________ |
            | Firma: _______________________ | Firma: ______________________ |
            """),
        new("contrato", "contrato", "Contrato de prestación de servicios", "Servicios, precio, plazos, garantías y terminación.",
            """
            # CONTRATO DE PRESTACIÓN DE SERVICIOS

            **Fecha:** {{fecha}}

            ## Partes
            **{{empresa}}** ("el Proveedor") y **{{contraparte}}** ("el Cliente"), representado por {{contacto}}.

            ## 1. Objeto
            El Proveedor prestará al Cliente los servicios descritos en la propuesta "{{asunto}}", que forma parte integral de este contrato.

            ## 2. Precio y forma de pago
            El precio total es de **{{monto}}**. Se facturará {{forma_pago}}. Los pagos vencen a 30 días desde la fecha de factura.

            ## 3. Plazo
            Los servicios iniciarán el {{fecha}} y tendrán una duración de ______ meses, prorrogable por acuerdo escrito.

            ## 4. Obligaciones del Proveedor
            - Prestar los servicios con diligencia y conforme a las buenas prácticas del sector.
            - Mantener la confidencialidad de la información del Cliente.
            - Informar oportunamente de cualquier incidencia.

            ## 5. Obligaciones del Cliente
            - Facilitar la información y accesos necesarios.
            - Pagar en los plazos acordados.

            ## 6. Propiedad intelectual
            Los entregables pasan al Cliente una vez pagados en su totalidad. El Proveedor conserva sus herramientas, metodologías y conocimientos previos.

            ## 7. Garantía y responsabilidad
            El Proveedor garantiza los entregables por 90 días. La responsabilidad total se limita al importe pagado en los últimos 12 meses.

            ## 8. Terminación
            Cualquiera de las partes puede terminar el contrato con 30 días de aviso escrito. El Cliente pagará los servicios prestados hasta la fecha.

            ## 9. Ley aplicable
            Leyes de {{pais}}; tribunales de {{ciudad}}.

            ## Firmas

            | {{empresa}} | {{contraparte}} |
            | --- | --- |
            | Firma: _______________________ | Firma: {{contacto}} |
            """),
        new("propuesta", "propuesta", "Propuesta comercial", "Resumen ejecutivo, alcance, inversión y siguientes pasos.",
            """
            # PROPUESTA COMERCIAL

            **Para:** {{contraparte}} · {{contacto}}
            **De:** {{empresa}}
            **Fecha:** {{fecha}}

            ## Resumen ejecutivo
            {{asunto}}.

            ## Situación actual y objetivos
            - Objetivo 1
            - Objetivo 2

            ## Alcance de la solución
            1. Fase 1: diagnóstico
            2. Fase 2: implementación
            3. Fase 3: acompañamiento

            ## Inversión
            | Concepto | Importe |
            | --- | --- |
            | Servicios | {{monto}} |
            | **Total** | **{{monto}}** |

            ## Plazos
            Inicio estimado: a convenir. Duración: ______ semanas.

            ## Siguientes pasos
            1. Aprobación de la propuesta.
            2. Firma del contrato.
            3. Reunión de arranque.

            Validez de la propuesta: 15 días.
            """),
        new("carta", "carta", "Carta formal", "Comunicado formal con membrete.",
            """
            **{{empresa}}**
            {{empresa_direccion}}

            {{fecha}}

            Estimado/a {{contacto}}
            {{contraparte}}

            **Asunto:** {{asunto}}

            Por medio de la presente, …

            Quedamos a su disposición para cualquier consulta.

            Atentamente,

            ______________________
            {{autor}}
            {{empresa}}
            """),
        new("acta", "acta", "Acta de reunión", "Asistentes, acuerdos y tareas.",
            """
            # ACTA DE REUNIÓN

            **Fecha:** {{fecha}} · **Con:** {{contraparte}} ({{contacto}}) · **Tema:** {{asunto}}

            ## Asistentes
            - {{autor}} ({{empresa}})
            - {{contacto}} ({{contraparte}})

            ## Temas tratados
            1. …

            ## Acuerdos
            - …

            ## Tareas
            | Tarea | Responsable | Fecha |
            | --- | --- | --- |
            | … | … | … |
            """),
    ];

    public static Template? FindTemplate(string id) => Templates.FirstOrDefault(t => t.Id == id);

    /// <summary>Plantillas predefinidas + propias (las propias llevan el id del documento de Mongo).</summary>
    public async Task<List<Template>> GetAllTemplatesAsync()
    {
        var custom = (await _templates.GetAllAsync()).OrderBy(t => t.Title).Select(t => new Template(t.Id!, t.Kind, t.Title, t.Description ?? "Plantilla propia", t.Body));
        return Templates.Concat(custom).ToList();
    }
    public async Task<Template?> FindTemplateAsync(string idOrTitle)
    {
        var all = await GetAllTemplatesAsync();
        return all.FirstOrDefault(t => t.Id == idOrTitle) ?? all.FirstOrDefault(t => t.Title.Equals(idOrTitle, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault(t => t.Title.Contains(idOrTitle, StringComparison.OrdinalIgnoreCase));
    }
    public Task<List<DocumentTemplateEntity>> GetCustomTemplatesAsync() => _templates.GetAllAsync();
    public Task<DocumentTemplateEntity?> GetCustomTemplateAsync(string? id) => _templates.GetByIdAsync(id);
    public async Task SaveTemplateAsync(DocumentTemplateEntity t, string? by) { t.UpdatedBy = by; if (t.Id is null) { t.CreatedBy = by; await _templates.InsertAsync(t); } else await _templates.UpdateAsync(t); }
    public Task DeleteTemplateAsync(string id, string? by) => _templates.DeleteAsync(id, by);

    // ---------- recordatorios de firma ----------
    public static bool IsReminderDue(DocumentSigner s, DateTime nowUtc, int everyDays = 3) =>
        !s.IsSigned && (nowUtc - s.RequestedAt).TotalDays >= everyDays && (s.LastReminderAt is null || (nowUtc - s.LastReminderAt.Value).TotalDays >= everyDays);

    /// <summary>Recuerda a los firmantes pendientes (correo si hay SMTP y dirección) y avisa a quien solicitó la firma. Devuelve cuántos recordatorios se enviaron.</summary>
    public async Task<int> SendSignatureRemindersAsync(EmailService email, string baseUrl)
    {
        var count = 0;
        foreach (var d in await _docs.FindAsync(x => x.Signers.Any(sg => sg.SignedAt == null)))
        {
            var changed = false;
            foreach (var sg in d.Signers.Where(sg => IsReminderDue(sg, DateTime.UtcNow)))
            {
                var link = $"{baseUrl.TrimEnd('/')}/firmar/{sg.Token}";
                if (sg.Email is not null && email.IsConfigured)
                    try { await email.SendAsync(sg.Email, $"Recordatorio: firma pendiente de \"{d.Title}\"", $"Hola {sg.Name},\n\nSigue pendiente su firma del documento \"{d.Title}\". Puede firmarlo aquí:\n{link}\n\nGracias.", d.EntityType ?? "document", d.EntityId ?? d.Id!, d.EntityTitle ?? d.Title, "Dana"); } catch { }
                var owner = sg.RequestedBy ?? d.Author ?? d.CreatedBy;
                if (owner is not null) await _notifications.NotifyAsync(owner, NotificationKinds.Due, $"✍ {sg.Name} aún no ha firmado \"{d.Title}\" ({(int)(DateTime.UtcNow - sg.RequestedAt).TotalDays} días)", $"documentos/{d.Id}", "Dana");
                sg.LastReminderAt = DateTime.UtcNow; changed = true; count++;
            }
            if (changed) await _docs.UpdateAsync(d);
        }
        return count;
    }

    /// <summary>Rellena los marcadores {{…}} con los datos de la empresa y del registro ligado.</summary>
    public async Task<string> FillAsync(string body, string? entityType, string? entityId, string? author, string? subject = null)
    {
        var s = await _settings.GetAsync();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["empresa"] = s.CompanyName, ["empresa_id"] = s.TaxId ?? "______", ["empresa_direccion"] = s.Address ?? "______", ["fecha"] = DateTime.Now.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES")),
            ["autor"] = author ?? "", ["pais"] = "Costa Rica", ["ciudad"] = "San José", ["forma_pago"] = "según cotización aceptada", ["monto"] = "______", ["contraparte"] = "______", ["contacto"] = "______", ["asunto"] = subject ?? "______",
        };
        if (entityType is not null && entityId is not null)
        {
            switch (entityType)
            {
                case "lead": { var l = await _leads.GetByIdAsync(entityId); if (l is not null) { vars["contraparte"] = l.CompanyName ?? l.FullName; vars["contacto"] = l.FullName.Length > 0 ? l.FullName : l.Title; vars["asunto"] = subject ?? l.Title; if (l.Opportunity > 0) vars["monto"] = $"{l.Opportunity:N2} {l.Currency}"; } break; }
                case "contact": { var c = await _contacts.GetByIdAsync(entityId); if (c is not null) { vars["contacto"] = c.FullName; var co = c.CompanyId is null ? null : await _companies.GetByIdAsync(c.CompanyId); vars["contraparte"] = co?.Title ?? c.FullName; } break; }
                case "company": { var k = await _companies.GetByIdAsync(entityId); if (k is not null) { vars["contraparte"] = k.Title; } break; }
                case "deal":
                {
                    var d = await _deals.GetByIdAsync(entityId);
                    if (d is not null)
                    {
                        vars["asunto"] = subject ?? d.Title; vars["monto"] = $"{d.Amount:N2} {d.Currency}";
                        var co = d.CompanyId is null ? null : await _companies.GetByIdAsync(d.CompanyId);
                        var ct = d.ContactId is null ? null : await _contacts.GetByIdAsync(d.ContactId);
                        vars["contraparte"] = co?.Title ?? ct?.FullName ?? "______"; vars["contacto"] = ct?.FullName ?? "______";
                    }
                    break;
                }
            }
        }
        return VarRx().Replace(body, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    // ---------- CRUD ----------
    public async Task<List<CrmDocument>> GetAllAsync() => (await _docs.GetAllAsync()).OrderByDescending(d => d.UpdatedAt).ToList();
    public Task<CrmDocument?> GetAsync(string? id) => _docs.GetByIdAsync(id);
    public async Task<List<CrmDocument>> GetForAsync(string entityType, string entityId) => (await _docs.FindAsync(d => d.EntityType == entityType && d.EntityId == entityId)).OrderByDescending(d => d.UpdatedAt).ToList();

    public async Task<string?> EntityTitleAsync(string? type, string? id) => type switch
    {
        "lead" => (await _leads.GetByIdAsync(id))?.Title,
        "contact" => (await _contacts.GetByIdAsync(id))?.FullName,
        "company" => (await _companies.GetByIdAsync(id))?.Title,
        "deal" => (await _deals.GetByIdAsync(id))?.Title,
        _ => null
    };

    public async Task<CrmDocument> CreateAsync(CrmDocument d, string? by)
    {
        d.Author ??= by; d.CreatedBy = by; d.UpdatedBy = by; d.Version = 1;
        if (d.EntityType is not null && d.EntityId is not null) d.EntityTitle ??= await EntityTitleAsync(d.EntityType, d.EntityId);
        d.Versions = [new DocumentVersion { Number = 1, By = by, Content = d.Content, Note = "Creación" }];
        await _docs.InsertAsync(d);
        if (d.EntityType is not null && d.EntityId is not null)
            await _activities.LogAsync(d.EntityType, d.EntityId, d.EntityTitle ?? "", ActivityKinds.System, $"📄 Creó el documento \"{d.Title}\" ({DocumentKinds.Get(d.Kind).Label})", by);
        return d;
    }

    /// <summary>Guarda cambios; si el contenido cambió crea una versión nueva.</summary>
    public async Task SaveAsync(CrmDocument d, string? by, string? note = null)
    {
        var prev = await _docs.GetByIdAsync(d.Id);
        d.UpdatedBy = by;
        if (prev is not null && prev.Content != d.Content)
        {
            d.Version = prev.Version + 1;
            d.Versions = prev.Versions.TakeLast(29).ToList();
            d.Versions.Add(new DocumentVersion { Number = d.Version, By = by, Content = d.Content, Note = note });
        }
        else if (prev is not null) { d.Version = prev.Version; d.Versions = prev.Versions; }
        await _docs.UpdateAsync(d);
    }

    public Task DeleteAsync(string id, string? by) => _docs.DeleteAsync(id, by);

    // ---------- firma electrónica ----------
    public async Task<DocumentSigner> RequestSignatureAsync(CrmDocument d, string name, string? email, string? role, string? by)
    {
        var signer = new DocumentSigner { Name = name.Trim(), Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(), Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(), RequestedBy = by };
        d.Signers.Add(signer);
        if (d.Status is "draft") d.Status = "review";
        d.UpdatedBy = by;
        await _docs.UpdateAsync(d);
        if (d.EntityType is not null && d.EntityId is not null) await _activities.LogAsync(d.EntityType, d.EntityId, d.EntityTitle ?? "", ActivityKinds.System, $"✍ Solicitó la firma de \"{d.Title}\" a {signer.Name}", by);
        return signer;
    }

    public async Task<(CrmDocument Doc, DocumentSigner Signer)?> FindByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32) return null;
        var d = (await _docs.FindAsync(x => x.Signers.Any(s => s.Token == token))).FirstOrDefault();
        var signer = d?.Signers.FirstOrDefault(s => s.Token == token);
        return d is null || signer is null ? null : (d, signer);
    }

    /// <summary>Registra la firma. Devuelve null si el token no existe o ya estaba firmado.</summary>
    public async Task<CrmDocument?> SignAsync(string token, string signedName, string? role, string? imageDataUrl, string? ip)
    {
        var found = await FindByTokenAsync(token);
        if (found is null || found.Value.Signer.IsSigned) return null;
        var (d, signer) = found.Value;
        if (imageDataUrl is not null && (!imageDataUrl.StartsWith("data:image/png;base64,") || imageDataUrl.Length > 400_000)) imageDataUrl = null;
        signer.SignedAt = DateTime.UtcNow; signer.SignedName = signedName.Trim(); signer.Role = string.IsNullOrWhiteSpace(role) ? signer.Role : role.Trim(); signer.SignatureImage = imageDataUrl; signer.Ip = ip;
        if (d.AllSigned) d.Status = "signed";
        d.UpdatedBy = signer.Name;
        await _docs.UpdateAsync(d);
        if (d.EntityType is not null && d.EntityId is not null) await _activities.LogAsync(d.EntityType, d.EntityId, d.EntityTitle ?? "", ActivityKinds.System, $"✍ {signer.SignedName} firmó \"{d.Title}\"{(d.AllSigned ? " · documento completamente firmado" : "")}", signer.SignedName);
        var owner = signer.RequestedBy ?? d.Author ?? d.CreatedBy;
        if (owner is not null) await _notifications.NotifyAsync(owner, NotificationKinds.System, $"✍ {signer.SignedName} firmó \"{d.Title}\"{(d.AllSigned ? " (todas las firmas completas)" : "")}", $"documentos/{d.Id}", signer.SignedName);
        return d;
    }

    public async Task RemoveSignerAsync(CrmDocument d, DocumentSigner signer, string? by)
    {
        d.Signers.Remove(signer); d.UpdatedBy = by;
        if (d.Status == "signed" && !d.AllSigned) d.Status = "final";
        await _docs.UpdateAsync(d);
    }

    /// <summary>Bloque HTML de firmas para la vista previa, impresión y página de firma.</summary>
    public static string SignaturesHtml(CrmDocument d)
    {
        if (d.Signers.Count == 0) return "";
        var sb = new StringBuilder("<div class=\"sig-block\"><h2>Firmas</h2><div class=\"sig-grid\">");
        foreach (var s in d.Signers)
        {
            sb.Append("<div class=\"sig-box\">");
            if (s.IsSigned && s.SignatureImage is not null) sb.Append("<img class=\"sig-img\" src=\"").Append(s.SignatureImage).Append("\" alt=\"firma\">");
            else if (s.IsSigned) sb.Append("<div class=\"sig-typed\">").Append(System.Net.WebUtility.HtmlEncode(s.SignedName ?? s.Name)).Append("</div>");
            else sb.Append("<div class=\"sig-pending\">Pendiente de firma</div>");
            sb.Append("<div class=\"sig-name\">").Append(System.Net.WebUtility.HtmlEncode(s.SignedName ?? s.Name)).Append("</div>");
            if (s.Role is not null) sb.Append("<div class=\"sig-meta\">").Append(System.Net.WebUtility.HtmlEncode(s.Role)).Append("</div>");
            if (s.IsSigned) sb.Append("<div class=\"sig-meta\">Firmado el ").Append(s.SignedAt!.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")).Append(s.Ip is null ? "" : " · IP " + System.Net.WebUtility.HtmlEncode(s.Ip)).Append("</div>");
            sb.Append("</div>");
        }
        return sb.Append("</div></div>").ToString();
    }

    // ---------- exportación ----------
    /// <summary>Markdown → HTML; las tablas (| a | b |) se convierten aparte porque el conversor base no las soporta.</summary>
    public static string ToHtml(string markdown)
    {
        var lines = markdown.Replace("\r", "").Split('\n'); var sb = new StringBuilder(); var buf = new StringBuilder(); var i = 0;
        void Flush() { if (buf.Length > 0) { sb.Append(Dana.Services.Ai.Markdown.ToHtml(buf.ToString())); buf.Clear(); } }
        while (i < lines.Length)
        {
            if (lines[i].TrimStart().StartsWith("|") && i + 1 < lines.Length && TableSepRx().IsMatch(lines[i + 1]))
            {
                Flush();
                sb.Append("<table class=\"doc-table\"><thead><tr>");
                foreach (var c in Cells(lines[i])) sb.Append("<th>").Append(InlineHtml(c)).Append("</th>");
                sb.Append("</tr></thead><tbody>"); i += 2;
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|")) { sb.Append("<tr>"); foreach (var c in Cells(lines[i])) sb.Append("<td>").Append(InlineHtml(c)).Append("</td>"); sb.Append("</tr>"); i++; }
                sb.Append("</tbody></table>");
                continue;
            }
            buf.Append(lines[i]).Append('\n'); i++;
        }
        Flush();
        return sb.ToString();
    }
    private static IEnumerable<string> Cells(string line) => line.Trim().Trim('|').Split('|').Select(c => c.Trim());
    private static string InlineHtml(string s) { var h = Dana.Services.Ai.Markdown.ToHtml(s); return h.StartsWith("<p>") ? h[3..^4] : h; }

    /// <summary>Genera un .docx (Open XML) a partir del Markdown del documento, con membrete de la empresa.</summary>
    public async Task<byte[]> ToDocxAsync(CrmDocument d)
    {
        var s = await _settings.GetAsync();
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
            var body = main.Document.Body!;
            body.Append(Para(s.CompanyName, bold: true, size: 20, color: "0073EA"));
            body.Append(Para(string.Join(" · ", new[] { s.Address, s.Phone, s.Email, s.Web }.Where(x => !string.IsNullOrWhiteSpace(x))!), size: 16, color: "666666"));
            body.Append(Para(""));
            foreach (var line in d.Content.Replace("\r", "").Split('\n'))
            {
                var t = line.TrimEnd();
                if (t.Length == 0) { body.Append(Para("")); continue; }
                var h = HeadingRx().Match(t);
                if (h.Success) { var lv = h.Groups[1].Value.Length; body.Append(Para(Strip(h.Groups[2].Value), bold: true, size: lv == 1 ? 32 : lv == 2 ? 26 : 24, spacingBefore: 200)); continue; }
                if (t.TrimStart().StartsWith("|")) { if (!TableSepRx().IsMatch(t)) { if (body.LastChild is Table tbl) tbl.Append(WordRow(Cells(t).Select(Strip).ToArray(), false)); else body.Append(WordTable(Cells(t).Select(Strip).ToArray())); } continue; }
                var ul = UlRx().Match(t); if (ul.Success) { body.Append(Para("•  " + Strip(ul.Groups[1].Value), indent: 360)); continue; }
                var ol = OlRx().Match(t); if (ol.Success) { body.Append(Para(ol.Groups[1].Value + ".  " + Strip(ol.Groups[2].Value), indent: 360)); continue; }
                body.Append(RichPara(t));
            }
            if (d.Signers.Count > 0)
            {
                body.Append(Para("Firmas", bold: true, size: 26, spacingBefore: 300));
                foreach (var sg in d.Signers)
                    body.Append(Para(sg.IsSigned ? $"✔ {sg.SignedName ?? sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)} · firmado el {sg.SignedAt!.Value.ToLocalTime():dd/MM/yyyy HH:mm}{(sg.Ip is null ? "" : " · IP " + sg.Ip)}" : $"☐ {sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)} · pendiente de firma", size: 20));
            }
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Table WordTable(string[] header)
    {
        var tbl = new Table(new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 }, new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        tbl.Append(WordRow(header, true));
        return tbl;
    }
    private static TableRow WordRow(string[] cells, bool header)
    {
        var row = new TableRow();
        foreach (var c in cells) row.Append(new TableCell(new TableCellProperties(new TableCellMargin(new LeftMargin { Width = "80", Type = TableWidthUnitValues.Dxa }, new RightMargin { Width = "80", Type = TableWidthUnitValues.Dxa })), Para(c, bold: header, size: 20)));
        return row;
    }

    private static Paragraph Para(string text, bool bold = false, int size = 22, string? color = null, int indent = 0, int spacingBefore = 0)
    {
        var rp = new RunProperties();
        if (bold) rp.Append(new Bold());
        rp.Append(new FontSize { Val = size.ToString() });
        if (color is not null) rp.Append(new Color { Val = color });
        var pp = new ParagraphProperties();
        if (indent > 0) pp.Append(new Indentation { Left = indent.ToString() });
        pp.Append(new SpacingBetweenLines { Before = spacingBefore.ToString(), After = "120" });
        return new Paragraph(pp, new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>Párrafo con **negritas** en línea.</summary>
    private static Paragraph RichPara(string text)
    {
        var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));
        var parts = BoldRx().Split(text);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var rp = new RunProperties(new FontSize { Val = "22" });
            if (i % 2 == 1) rp.Append(new Bold());
            p.Append(new Run(rp, new Text(Strip(parts[i])) { Space = SpaceProcessingModeValues.Preserve }));
        }
        return p;
    }
    private static string Strip(string s) => s.Replace("**", "").Replace("`", "");

    // ---------- lectura de adjuntos ----------
    /// <summary>Extrae texto de un adjunto (txt, md, csv, html, docx, pdf, xlsx). Devuelve como máximo maxChars caracteres.</summary>
    public async Task<string> ExtractTextAsync(Attachment a, int maxChars = 12000)
    {
        var path = _attachments.PathFor(a);
        if (!File.Exists(path)) throw new FileNotFoundException("El archivo no está en el almacenamiento");
        var ext = Path.GetExtension(a.FileName).ToLowerInvariant();
        string text;
        switch (ext)
        {
            case ".txt" or ".md" or ".csv" or ".json" or ".xml": text = await File.ReadAllTextAsync(path); break;
            case ".html" or ".htm": text = Regex.Replace(Regex.Replace(await File.ReadAllTextAsync(path), @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase), "<[^>]+>", " "); break;
            case ".docx": text = ReadDocx(path); break;
            case ".pdf": text = ReadPdf(path); break;
            case ".xlsx": text = ReadXlsx(path); break;
            default: throw new InvalidOperationException($"No se puede leer el formato {ext}. Formatos admitidos: txt, md, csv, html, docx, pdf, xlsx.");
        }
        text = Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        return text.Length > maxChars ? text[..maxChars] + "\n…(recortado)" : text;
    }

    public static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var sb = new StringBuilder();
        foreach (var p in doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>()) sb.AppendLine(p.InnerText);
        return sb.ToString();
    }
    public static string ReadDocx(byte[] bytes) { using var ms = new MemoryStream(bytes); using var doc = WordprocessingDocument.Open(ms, false); return string.Join("\n", doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().Select(p => p.InnerText)); }

    private static string ReadPdf(string path)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages()) { sb.AppendLine(page.Text); sb.AppendLine(); if (sb.Length > 60000) break; }
        return sb.ToString();
    }

    private static string ReadXlsx(string path)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        var sb = new StringBuilder();
        foreach (var ws in wb.Worksheets)
        {
            sb.AppendLine("## " + ws.Name);
            var used = ws.RangeUsed();
            if (used is null) continue;
            foreach (var row in used.RowsUsed()) sb.AppendLine(string.Join(" | ", row.CellsUsed().Select(c => c.GetFormattedString())));
            if (sb.Length > 60000) break;
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z_]+)\s*\}\}")] private static partial Regex VarRx();
    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")] private static partial Regex TableSepRx();
    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")] private static partial Regex HeadingRx();
    [GeneratedRegex(@"^\s*[-*•]\s+(.+)$")] private static partial Regex UlRx();
    [GeneratedRegex(@"^\s*(\d+)[.)]\s+(.+)$")] private static partial Regex OlRx();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex BoldRx();
}
