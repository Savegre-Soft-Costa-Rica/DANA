using System.Text;
using System.Text.RegularExpressions;
using Dana.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Dana.Services;

/// <summary>Exportación a PDF (PDFsharp) e importación desde Word/PDF a Markdown.</summary>
public partial class DocumentService
{
    // ---------- fuentes embebidas (funcionan también en Docker sin fuentes del sistema) ----------
    private sealed class EmbeddedFontResolver : IFontResolver
    {
        private readonly string _dir;
        public EmbeddedFontResolver(string dir) { _dir = dir; }
        public byte[]? GetFont(string faceName) { var f = Path.Combine(_dir, faceName); return File.Exists(f) ? File.ReadAllBytes(f) : null; }
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var fam = familyName.ToLowerInvariant();
            if (fam.Contains("cormorant"))
            {
                var cormorant = Path.Combine(_dir, "CormorantGaramond-Regular.ttf");
                if (File.Exists(cormorant))
                {
                    if (italic) return new FontResolverInfo("CormorantGaramond-Italic.ttf");
                    if (fam.Contains("semibold")) return new FontResolverInfo("CormorantGaramond-SemiBold.ttf");
                    return new FontResolverInfo(bold ? "CormorantGaramond-Bold.ttf" : "CormorantGaramond-Regular.ttf");
                }
                // sin la fuente serif disponible: recae en la sans embebida
            }
            return new FontResolverInfo(bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");
        }
    }
    private static readonly object FontLock = new();
    private static bool _fontsReady;
    private static void EnsureFonts(string webRoot)
    {
        if (_fontsReady) return;
        lock (FontLock)
        {
            if (_fontsReady) return;
            var dir = Path.Combine(webRoot, "fonts");
            if (!File.Exists(Path.Combine(dir, "DejaVuSans.ttf"))) dir = "/usr/share/fonts/truetype/dejavu";
            GlobalFontSettings.FontResolver = new EmbeddedFontResolver(dir);
            _fontsReady = true;
        }
    }

    // ---------- Paleta y tipografía de marca (estilo Savegre: serif, mucho blanco, acento azul) ----------
    private static readonly XColor Ink = XColors.Black;
    private static readonly XColor Zinc = XColor.FromArgb(0x71, 0x71, 0x7A);
    private static readonly XColor Accent = XColor.FromArgb(0x3B, 0x82, 0xF6);
    private static readonly XColor Hair = XColor.FromArgb(0xE4, 0xE4, 0xE7);
    private static readonly string[] MesesEs = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
    private static string FechaEs(DateTime dt) => $"{dt.Day:D2} de {MesesEs[dt.Month - 1]} de {dt.Year}";

    /// <summary>Genera el PDF del documento con el estilo de marca Savegre: portada, cabecera/pie y Markdown en serif.</summary>
    public async Task<byte[]> ToPdfAsync(CrmDocument d, string webRoot)
    {
        EnsureFonts(webRoot);
        var s = await _settings.GetAsync();
        var pdf = new PdfDocument(); pdf.Info.Title = d.Title; pdf.Info.Author = s.CompanyName;
        var kind = DocumentKinds.Get(d.Kind).Label;
        var headerRight = $"{kind} · {d.CreatedAt.ToLocalTime():yyyy}".ToUpperInvariant();
        var footer = $"{s.CompanyName} · Documento confidencial".ToUpperInvariant();
        var w = new PdfWriterCtx(pdf, footer, s.CompanyName, headerRight);

        // ----- Portada -----
        w.Space(64);
        w.Text(s.CompanyName + ".", 30, semibold: true);
        w.Text("Construimos software que escala.", 13.5, color: Zinc, italic: true);
        w.Space(28);
        w.Text(d.Title, 26, semibold: true);
        w.Space(28);
        w.Meta("Cliente", string.IsNullOrWhiteSpace(d.EntityTitle) ? "—" : d.EntityTitle!);
        w.Meta("Fecha", FechaEs(d.CreatedAt.ToLocalTime()));
        w.Meta("Preparado por", d.Author ?? d.CreatedBy ?? s.CompanyName);
        w.Meta("Versión", d.Version.ToString());
        w.Space(20);
        var loc = string.Join(" · ", new[] { s.Address, "Documento confidencial" }.Where(x => !string.IsNullOrWhiteSpace(x))!);
        w.LabelRule(loc.ToUpperInvariant());

        // ----- Cuerpo (en página nueva, con cabecera corrida) -----
        w.StartBody();
        var lines = d.Content.Replace("\r", "").Split('\n'); var i = 0;
        while (i < lines.Length)
        {
            var t = lines[i].TrimEnd();
            if (t.Length == 0) { w.Space(5); i++; continue; }
            var h = HeadingRx().Match(t);
            if (h.Success) { var lv = h.Groups[1].Value.Length; w.Space(lv == 1 ? 12 : 8); w.Text(Strip(h.Groups[2].Value), lv == 1 ? 19 : lv == 2 ? 15 : 12.5, semibold: true); w.Space(3); i++; continue; }
            if (t.TrimStart().StartsWith("|") && i + 1 < lines.Length && TableSepRx().IsMatch(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(t).Select(Strip).ToArray() }; i += 2;
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|")) { rows.Add(Cells(lines[i]).Select(Strip).ToArray()); i++; }
                w.Table(rows); continue;
            }
            var ul = UlRx().Match(t); if (ul.Success) { w.Text("•  " + Strip(ul.Groups[1].Value), 12, indent: 18); i++; continue; }
            var ol = OlRx().Match(t); if (ol.Success) { w.Text(ol.Groups[1].Value + ".  " + Strip(ol.Groups[2].Value), 12, indent: 18); i++; continue; }
            w.Text(Strip(t), 12); i++;
        }
        if (d.Signers.Count > 0)
        {
            w.Space(16); w.Text("Firmas", 15, semibold: true); w.Space(6);
            foreach (var sg in d.Signers)
            {
                if (sg.IsSigned && sg.SignatureImage is not null && sg.SignatureImage.StartsWith("data:image/png;base64,"))
                {
                    try { w.Image(Convert.FromBase64String(sg.SignatureImage["data:image/png;base64,".Length..]), 150, 45); } catch { }
                }
                w.Text(sg.IsSigned ? $"{sg.SignedName ?? sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)}" : $"{sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)} · pendiente de firma", 12, semibold: sg.IsSigned);
                if (sg.IsSigned) w.Text($"Firmado el {sg.SignedAt!.Value.ToLocalTime():dd/MM/yyyy HH:mm}{(sg.Ip is null ? "" : " · IP " + sg.Ip)}", 8.5, color: Zinc);
                w.Space(10);
            }
        }
        w.Finish();
        using var ms = new MemoryStream(); pdf.Save(ms, false); return ms.ToArray();
    }

    /// <summary>Motor de maquetación con estilo Savegre: portada, cabecera/pie corridos, serif, tablas minimalistas.</summary>
    private sealed class PdfWriterCtx
    {
        private readonly PdfDocument _pdf; private readonly string _footer, _company, _headerRight;
        private PdfPage _page = null!; private XGraphics _gfx = null!; private double _y; private int _pageNo;
        private bool _headerOn;
        private const string Serif = "Cormorant Garamond", SerifSemi = "Cormorant Garamond SemiBold";
        private const double PageW = 595, PageH = 842, Margin = 64, Width = PageW - 2 * Margin, Bottom = PageH - 78;
        private static readonly XStringFormat Left = new() { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near };
        private static readonly XStringFormat Right = new() { Alignment = XStringAlignment.Far, LineAlignment = XLineAlignment.Near };

        public PdfWriterCtx(PdfDocument pdf, string footer, string company, string headerRight)
        { _pdf = pdf; _footer = footer; _company = company; _headerRight = headerRight; NewPage(); }

        /// <summary>Cierra la portada y empieza el cuerpo en una página nueva con cabecera corrida.</summary>
        public void StartBody() { _headerOn = true; NewPage(); }

        private void NewPage()
        {
            if (_gfx is not null) Footer();
            _page = _pdf.AddPage(); _page.Size = PdfSharp.PageSize.A4; _gfx = XGraphics.FromPdfPage(_page); _pageNo++;
            _y = Margin;
            if (_headerOn) { Header(); _y = Margin + 18; }
        }
        private void Header()
        {
            _gfx.DrawString(_company + ".", new XFont(SerifSemi, 11), new XSolidBrush(Ink), new XRect(Margin, Margin - 24, Width, 14), Left);
            _gfx.DrawString(_headerRight, new XFont(Serif, 7.8), new XSolidBrush(Zinc), new XRect(Margin, Margin - 23, Width, 14), Right);
            _gfx.DrawLine(new XPen(Accent, 1.2), Margin, Margin - 6, Margin + Width, Margin - 6);
        }
        private void Footer()
        {
            var y = PageH - 44; var f = new XFont(Serif, 7.8); var br = new XSolidBrush(Zinc);
            _gfx.DrawLine(new XPen(Hair, 0.8), Margin, y - 6, Margin + Width, y - 6);
            _gfx.DrawString(_footer, f, br, new XRect(Margin, y, Width, 12), Left);
            _gfx.DrawString(_pageNo.ToString(), f, br, new XRect(Margin, y, Width, 12), Right);
        }
        public void Finish() => Footer();

        private void Need(double h) { if (_y + h > Bottom) NewPage(); }
        public void Space(double h) { _y += h; }

        /// <summary>Etiqueta de metadato (mayúsculas, zinc) + valor en serif.</summary>
        public void Meta(string label, string value)
        {
            Space(2);
            Text(label.ToUpperInvariant(), 8.5, color: Zinc);
            _y -= 3;
            Text(value, 14, semibold: true);
            Space(5);
        }
        /// <summary>Línea fina superior + texto en mayúsculas zinc (p. ej. la línea de confidencialidad).</summary>
        public void LabelRule(string text)
        {
            Need(22);
            _gfx.DrawLine(new XPen(Hair, 0.8), Margin, _y, Margin + Width, _y);
            _y += 8;
            Text(text, 7.8, color: Zinc);
        }

        public void Text(string text, double size = 12, bool bold = false, XColor? color = null, XStringAlignment align = XStringAlignment.Near, double indent = 0, bool italic = false, bool semibold = false)
        {
            var fam = italic ? Serif : semibold ? SerifSemi : Serif;
            var style = bold ? XFontStyleEx.Bold : italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
            var font = new XFont(fam, size, style);
            var brush = new XSolidBrush(color ?? Ink);
            var lineH = size * 1.4; var maxW = Width - indent;
            var fmt = align == XStringAlignment.Near ? Left : new XStringFormat { Alignment = align, LineAlignment = XLineAlignment.Near };
            foreach (var line in Wrap(text, font, maxW))
            {
                Need(lineH);
                _gfx.DrawString(line, font, brush, new XRect(Margin + indent, _y, maxW, lineH), fmt);
                _y += lineH;
            }
        }
        private IEnumerable<string> Wrap(string text, XFont font, double maxW)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries); var cur = new StringBuilder();
            foreach (var wd in words)
            {
                var test = cur.Length == 0 ? wd : cur + " " + wd;
                if (_gfx.MeasureString(test, font).Width > maxW && cur.Length > 0) { yield return cur.ToString(); cur.Clear(); cur.Append(wd); }
                else { cur.Clear(); cur.Append(test); }
            }
            if (cur.Length > 0) yield return cur.ToString();
            if (words.Length == 0) yield return "";
        }
        /// <summary>Tabla minimalista: sin recuadros, solo líneas finas bajo cada fila; encabezado en zinc.</summary>
        public void Table(List<string[]> rows)
        {
            var cols = rows.Max(r => r.Length); if (cols == 0) return;
            var colW = Width / cols; var font = new XFont(Serif, 11); var head = new XFont(SerifSemi, 10);
            Space(2);
            for (var r = 0; r < rows.Count; r++)
            {
                var cells = rows[r]; var isHead = r == 0; var f = isHead ? head : font;
                var brush = new XSolidBrush(isHead ? Zinc : Ink);
                var wrapped = Enumerable.Range(0, cols).Select(c => Wrap(c < cells.Length ? cells[c] : "", f, colW - 10).ToList()).ToList();
                var rowH = wrapped.Max(l => l.Count) * 14 + 8;
                Need(rowH);
                for (var c = 0; c < cols; c++)
                    for (var l = 0; l < wrapped[c].Count; l++)
                        _gfx.DrawString(wrapped[c][l], f, brush, new XRect(Margin + c * colW + 2, _y + 4 + l * 14, colW - 10, 14), Left);
                _gfx.DrawLine(new XPen(isHead ? Ink : Hair, isHead ? 0.9 : 0.6), Margin, _y + rowH, Margin + Width, _y + rowH);
                _y += rowH;
            }
            _y += 8;
        }
        public void Image(byte[] png, double w, double h)
        {
            Need(h + 2);
            using var ms = new MemoryStream(png);
            try { using var img = XImage.FromStream(ms); _gfx.DrawImage(img, Margin, _y, w, h); } catch { }
            _y += h + 2;
        }
    }

    // ---------- importación Word / PDF → Markdown ----------
    /// <summary>Convierte un .docx en Markdown: títulos por estilo, negritas, listas y tablas.</summary>
    public static string DocxToMarkdown(Stream stream)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var sb = new StringBuilder();
        foreach (var el in body.ChildElements)
        {
            switch (el)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Paragraph p: sb.AppendLine(ParagraphToMd(p)); break;
                case DocumentFormat.OpenXml.Wordprocessing.Table t:
                {
                    var rows = t.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Select(r => r.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Select(c => string.Join(" ", c.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(ParagraphToMdInline)).Replace("|", "/").Trim()).ToArray()).ToList();
                    if (rows.Count == 0) break;
                    var cols = rows.Max(r => r.Length);
                    sb.AppendLine(); sb.AppendLine("| " + string.Join(" | ", Pad(rows[0], cols).Select(c => c.Replace("**", ""))) + " |"); sb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", cols)) + " |");
                    foreach (var r in rows.Skip(1)) sb.AppendLine("| " + string.Join(" | ", Pad(r, cols)) + " |");
                    sb.AppendLine(); break;
                }
            }
        }
        return Regex.Replace(sb.ToString().Trim(), @"\n{3,}", "\n\n");
    }
    private static string[] Pad(string[] r, int n) => r.Concat(Enumerable.Repeat("", Math.Max(0, n - r.Length))).Take(n).ToArray();

    private static string ParagraphToMd(DocumentFormat.OpenXml.Wordprocessing.Paragraph p)
    {
        var text = ParagraphToMdInline(p);
        if (text.Length == 0) return "";
        var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var m = Regex.Match(style, @"(?i)(?:heading|t[ií]tulo)\s*(\d)");
        if (m.Success) return new string('#', Math.Clamp(int.Parse(m.Groups[1].Value), 1, 4)) + " " + text + "\n";
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase)) return "# " + text + "\n";
        if (p.ParagraphProperties?.NumberingProperties is not null) return "- " + text;
        // párrafos cortos, todos en negrita y con tamaño grande → títulos (así exporta Dana y muchos documentos sin estilos)
        var runs = p.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>().Where(r => r.InnerText.Trim().Length > 0).ToList();
        if (runs.Count > 0 && runs.All(r => r.RunProperties?.Bold is not null) && text.Length < 120)
        {
            var size = runs.Select(r => int.TryParse(r.RunProperties?.FontSize?.Val?.Value, out var v) ? v : 22).Max();
            if (size >= 32) return "# " + text.Replace("**", "") + "\n";
            if (size >= 26) return "## " + text.Replace("**", "") + "\n";
            if (size >= 24) return "### " + text.Replace("**", "") + "\n";
        }
        return text + "\n";
    }
    private static string ParagraphToMdInline(DocumentFormat.OpenXml.Wordprocessing.Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var run in p.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>())
        {
            var t = string.Concat(run.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(x => x.Text)) + (run.Elements<DocumentFormat.OpenXml.Wordprocessing.Break>().Any() ? "\n" : "");
            if (t.Trim().Length == 0) { sb.Append(t); continue; }
            var bold = run.RunProperties?.Bold is not null && (run.RunProperties.Bold.Val is null || run.RunProperties.Bold.Val.Value);
            sb.Append(bold ? "**" + t.Trim() + "**" + (t.EndsWith(' ') ? " " : "") : t);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Convierte un PDF en Markdown aproximado (texto por páginas; líneas cortas en mayúsculas como títulos).</summary>
    public static string PdfToMarkdown(Stream stream)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) { sb.AppendLine(); continue; }
                if (line.Length < 80 && line.Length > 3 && line == line.ToUpperInvariant() && line.Any(char.IsLetter)) sb.AppendLine("## " + line);
                else sb.AppendLine(line);
            }
            sb.AppendLine();
        }
        return Regex.Replace(sb.ToString().Trim(), @"\n{3,}", "\n\n");
    }

    /// <summary>Importa un archivo (docx, pdf, md, txt) como contenido Markdown.</summary>
    public static string ImportToMarkdown(string fileName, Stream stream)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".docx" => DocxToMarkdown(stream),
            ".pdf" => PdfToMarkdown(stream),
            ".md" or ".txt" => new StreamReader(stream).ReadToEnd(),
            _ => throw new InvalidOperationException("Formato no admitido para importar: use .docx, .pdf, .md o .txt")
        };
    }
}
