using System.Text.RegularExpressions;
using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public record LinkCard(string Type, string Icon, string Color, string Title, string? Subtitle, string Path);

/// <summary>Resuelve enlaces internos (prospecto, negociación, contacto, compañía, cotización, documento)
/// a una tarjeta con su información, para previsualizarlos en el chat.</summary>
public partial class LinkPreviewService
{
    private readonly LeadService _leads;
    private readonly DealService _deals;
    private readonly QuoteService _quotes;
    private readonly DocumentService _docs;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;

    public LinkPreviewService(LeadService leads, DealService deals, QuoteService quotes, DocumentService docs,
        IRepository<Contact> contacts, IRepository<Company> companies)
    {
        _leads = leads; _deals = deals; _quotes = quotes; _docs = docs; _contacts = contacts; _companies = companies;
    }

    [GeneratedRegex(@"(?:https?://[^/\s]+)?/?(leads|deals|contacts|companies|cotizaciones|documentos)/([0-9a-fA-F]{24})", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    private static readonly Dictionary<string, string> SegmentToType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leads"] = "lead", ["deals"] = "deal", ["contacts"] = "contact",
        ["companies"] = "company", ["cotizaciones"] = "quote", ["documentos"] = "document",
    };

    /// <summary>Extrae referencias a elementos internos de un texto. Path canónico = "segmento/id".</summary>
    public static List<(string Type, string Id, string Path)> ExtractRefs(string? text)
    {
        var result = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(text)) return result;
        var seen = new HashSet<string>();
        foreach (Match m in LinkRegex().Matches(text))
        {
            var seg = m.Groups[1].Value.ToLowerInvariant();
            var id = m.Groups[2].Value;
            var path = $"{seg}/{id}";
            if (seen.Add(path) && SegmentToType.TryGetValue(seg, out var type))
                result.Add((type, id, path));
        }
        return result;
    }

    public async Task<LinkCard?> ResolveAsync(string type, string id, string path)
    {
        try
        {
            switch (type)
            {
                case "lead":
                    var l = await _leads.GetAsync(id);
                    return l is null ? null : new("Prospecto", "◎", "#a25ddc", l.Title, LeadStatuses.Get(l.Status).Name, path);
                case "deal":
                    var d = await _deals.GetAsync(id);
                    return d is null ? null : new("Negociación", "◆", "#0073ea", d.Title, $"{d.Amount:N0} {d.Currency}", path);
                case "contact":
                    var c = await _contacts.GetByIdAsync(id);
                    if (c is null) return null;
                    var co = string.IsNullOrEmpty(c.CompanyId) ? null : await _companies.GetByIdAsync(c.CompanyId);
                    return new("Contacto", "●", "#00c875", c.FullName, co?.Title ?? c.Position, path);
                case "company":
                    var k = await _companies.GetByIdAsync(id);
                    return k is null ? null : new("Compañía", "■", "#fdab3d", k.Title, k.Industry, path);
                case "quote":
                    var q = await _quotes.GetAsync(id);
                    return q is null ? null : new("Cotización", "🧾", "#579bfc", $"{q.Number} · {q.Title}".TrimEnd(' ', '·'), $"{q.Total:N0} {q.Currency}", path);
                case "document":
                    var doc = await _docs.GetAsync(id);
                    return doc is null ? null : new("Documento", DocumentKinds.Get(doc.Kind).Emoji, "#7e3b8a", doc.Title, $"{DocumentKinds.Get(doc.Kind).Label} · {DocumentStatuses.Get(doc.Status).Label}", path);
                default:
                    return null;
            }
        }
        catch { return null; }
    }
}
