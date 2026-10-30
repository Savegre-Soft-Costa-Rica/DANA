using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Dana.Services.Ai;

/// <summary>Conversión mínima y segura de Markdown a HTML para las respuestas de los agentes (todo el texto se codifica primero).</summary>
public static partial class Markdown
{
    public static string ToHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        string? list = null; var inCode = false; var para = new StringBuilder();
        void FlushPara() { if (para.Length > 0) { sb.Append("<p>").Append(Inline(para.ToString())).Append("</p>"); para.Clear(); } }
        void CloseList() { if (list is not null) { sb.Append(list == "ul" ? "</ul>" : "</ol>"); list = null; } }
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```")) { FlushPara(); CloseList(); if (inCode) { sb.Append("</code></pre>"); inCode = false; } else { sb.Append("<pre><code>"); inCode = true; } continue; }
            if (inCode) { sb.Append(WebUtility.HtmlEncode(raw)).Append('\n'); continue; }
            if (line.Length == 0) { FlushPara(); CloseList(); continue; }
            var h = HeadingRx().Match(line);
            if (h.Success) { FlushPara(); CloseList(); var lv = Math.Min(h.Groups[1].Value.Length + 2, 6); sb.Append($"<h{lv}>").Append(Inline(h.Groups[2].Value)).Append($"</h{lv}>"); continue; }
            var ul = UlRx().Match(line);
            if (ul.Success) { FlushPara(); if (list != "ul") { CloseList(); sb.Append("<ul>"); list = "ul"; } sb.Append("<li>").Append(Inline(ul.Groups[1].Value)).Append("</li>"); continue; }
            var ol = OlRx().Match(line);
            if (ol.Success) { FlushPara(); if (list != "ol") { CloseList(); sb.Append("<ol>"); list = "ol"; } sb.Append("<li>").Append(Inline(ol.Groups[1].Value)).Append("</li>"); continue; }
            if (list is not null && line.StartsWith("  ")) { sb.Append(' ').Append(Inline(line.Trim())); continue; }
            CloseList();
            if (para.Length > 0) para.Append("<br>");
            para.Append(line.Trim());
        }
        FlushPara(); CloseList();
        if (inCode) sb.Append("</code></pre>");
        return sb.ToString();
    }

    private static string Inline(string s)
    {
        // El texto llega sin codificar (salvo los <br> que insertamos nosotros): codificamos por fragmentos.
        var parts = s.Split("<br>");
        return string.Join("<br>", parts.Select(InlineEncoded));
    }

    private static string InlineEncoded(string s)
    {
        var e = WebUtility.HtmlEncode(s);
        e = CodeRx().Replace(e, "<code>$1</code>");
        e = BoldRx().Replace(e, "<strong>$1</strong>");
        e = ItalicRx().Replace(e, "<em>$1</em>");
        e = LinkRx().Replace(e, m =>
        {
            var href = m.Groups[2].Value;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value;
            var ext = href.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            return $"<a href=\"{href}\"{(ext ? " target=\"_blank\" rel=\"noopener\"" : "")}>{m.Groups[1].Value}</a>";
        });
        return e;
    }

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")] private static partial Regex HeadingRx();
    [GeneratedRegex(@"^\s*[-*•]\s+(.+)$")] private static partial Regex UlRx();
    [GeneratedRegex(@"^\s*\d+[.)]\s+(.+)$")] private static partial Regex OlRx();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex CodeRx();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex BoldRx();
    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?!\w)")] private static partial Regex ItalicRx();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex LinkRx();
}
