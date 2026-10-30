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
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new FontResolverInfo(bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");
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

    /// <summary>Genera el PDF del documento (membrete, Markdown básico, tablas y bloque de firmas).</summary>
    public async Task<byte[]> ToPdfAsync(CrmDocument d, string webRoot)
    {
        EnsureFonts(webRoot);
        var s = await _settings.GetAsync();
        var pdf = new PdfDocument(); pdf.Info.Title = d.Title; pdf.Info.Author = s.CompanyName;
        var w = new PdfWriterCtx(pdf, s.CompanyName, d.Title);
        w.Text(s.CompanyName, 14, true, XColor.FromArgb(0, 115, 234));
        w.Text(string.Join(" · ", new[] { s.Address, s.Phone, s.Email, s.Web }.Where(x => !string.IsNullOrWhiteSpace(x))!), 8, false, XColors.Gray);
        w.Rule(); w.Space(8);
        var lines = d.Content.Replace("\r", "").Split('\n'); var i = 0;
        while (i < lines.Length)
        {
            var t = lines[i].TrimEnd();
            if (t.Length == 0) { w.Space(5); i++; continue; }
            var h = HeadingRx().Match(t);
            if (h.Success) { var lv = h.Groups[1].Value.Length; w.Space(lv == 1 ? 6 : 8); w.Text(Strip(h.Groups[2].Value), lv == 1 ? 16 : lv == 2 ? 12.5 : 11, true, null, lv == 1 ? XStringAlignment.Center : XStringAlignment.Near); w.Space(3); i++; continue; }
            if (t.TrimStart().StartsWith("|") && i + 1 < lines.Length && TableSepRx().IsMatch(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(t).Select(Strip).ToArray() }; i += 2;
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|")) { rows.Add(Cells(lines[i]).Select(Strip).ToArray()); i++; }
                w.Table(rows); continue;
            }
            var ul = UlRx().Match(t); if (ul.Success) { w.Text("•  " + Strip(ul.Groups[1].Value), 10.5, false, null, XStringAlignment.Near, 18); i++; continue; }
            var ol = OlRx().Match(t); if (ol.Success) { w.Text(ol.Groups[1].Value + ".  " + Strip(ol.Groups[2].Value), 10.5, false, null, XStringAlignment.Near, 18); i++; continue; }
            w.Text(Strip(t), 10.5); i++;
        }
        if (d.Signers.Count > 0)
        {
            w.Space(14); w.Rule(); w.Text("Firmas", 12.5, true); w.Space(4);
            foreach (var sg in d.Signers)
            {
                if (sg.IsSigned && sg.SignatureImage is not null && sg.SignatureImage.StartsWith("data:image/png;base64,"))
                {
                    try { w.Image(Convert.FromBase64String(sg.SignatureImage["data:image/png;base64,".Length..]), 150, 45); } catch { }
                }
                w.Text(sg.IsSigned ? $"{sg.SignedName ?? sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)}" : $"{sg.Name}{(sg.Role is null ? "" : " · " + sg.Role)} · pendiente de firma", 10, sg.IsSigned);
                if (sg.IsSigned) w.Text($"Firmado el {sg.SignedAt!.Value.ToLocalTime():dd/MM/yyyy HH:mm}{(sg.Ip is null ? "" : " · IP " + sg.Ip)}", 8, false, XColors.Gray);
                w.Space(8);
            }
        }
        w.Finish();
        using var ms = new MemoryStream(); pdf.Save(ms, false); return ms.ToArray();
    }

    /// <summary>Motor de maquetación mínimo: párrafos con ajuste de línea, tablas, imágenes, salto de página y pie con numeración.</summary>
    private sealed class PdfWriterCtx
    {
        private readonly PdfDocument _pdf; private readonly string _company, _title;
        private PdfPage _page = null!; private XGraphics _gfx = null!; private double _y; private int _pageNo;
        private const double Margin = 56, Width = 595 - 2 * Margin, Bottom = 842 - 60;
        public PdfWriterCtx(PdfDocument pdf, string company, string title) { _pdf = pdf; _company = company; _title = title; NewPage(); }
        private void NewPage()
        {
            if (_gfx is not null) Footer();
            _page = _pdf.AddPage(); _page.Size = PdfSharp.PageSize.A4; _gfx = XGraphics.FromPdfPage(_page); _y = Margin; _pageNo++;
        }
        private void Footer()
        {
            var f = new XFont("DejaVuSans", 7.5);
            _gfx.DrawString($"{_company} · {_title} · página {_pageNo}", f, XBrushes.Gray, new XRect(Margin, 842 - 36, Width, 12), XStringFormats.Center);
        }
        public void Finish() => Footer();
        private void Need(double h) { if (_y + h > Bottom) NewPage(); }
        public void Space(double h) { _y += h; }
        public void Rule() { Need(6); _gfx.DrawLine(new XPen(XColor.FromArgb(0, 115, 234), 1.5), Margin, _y, Margin + Width, _y); _y += 6; }
        public void Text(string text, double size = 10.5, bool bold = false, XColor? color = null, XStringAlignment align = XStringAlignment.Near, double indent = 0)
        {
            var font = new XFont("DejaVuSans", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            var brush = new XSolidBrush(color ?? XColors.Black);
            var lineH = size * 1.45; var maxW = Width - indent;
            foreach (var line in Wrap(text, font, maxW))
            {
                Need(lineH);
                var fmt = new XStringFormat { Alignment = align, LineAlignment = XLineAlignment.Near };
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
        public void Table(List<string[]> rows)
        {
            var cols = rows.Max(r => r.Length); if (cols == 0) return;
            var colW = Width / cols; var font = new XFont("DejaVuSans", 9); var bold = new XFont("DejaVuSans", 9, XFontStyleEx.Bold);
            for (var r = 0; r < rows.Count; r++)
            {
                var cells = rows[r]; var f = r == 0 ? bold : font;
                var wrapped = Enumerable.Range(0, cols).Select(c => Wrap(c < cells.Length ? cells[c] : "", f, colW - 8).ToList()).ToList();
                var rowH = wrapped.Max(l => l.Count) * 12.5 + 8;
                Need(rowH);
                if (r == 0) _gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(241, 245, 249)), Margin, _y, Width, rowH);
                for (var c = 0; c < cols; c++)
                {
                    _gfx.DrawRectangle(new XPen(XColor.FromArgb(203, 213, 225), 0.6), Margin + c * colW, _y, colW, rowH);
                    for (var l = 0; l < wrapped[c].Count; l++) _gfx.DrawString(wrapped[c][l], f, XBrushes.Black, new XRect(Margin + c * colW + 4, _y + 4 + l * 12.5, colW - 8, 12.5), XStringFormats.TopLeft);
                }
                _y += rowH;
            }
            _y += 6;
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
