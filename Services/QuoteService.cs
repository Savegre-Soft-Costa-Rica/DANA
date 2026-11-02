using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public class QuoteService
{
    private readonly IRepository<Quote> _quotes;
    private readonly IRepository<Product> _products;
    private readonly IRepository<ProductCategory> _categories;
    private readonly IRepository<Deal> _deals;
    private readonly SettingsService _settings;
    private readonly ActivityService _activities;

    public QuoteService(IRepository<Quote> quotes, IRepository<Product> products, IRepository<ProductCategory> categories, IRepository<Deal> deals, SettingsService settings, ActivityService activities)
    {
        _quotes = quotes; _products = products; _categories = categories; _deals = deals; _settings = settings; _activities = activities;
    }

    // ----- categorías -----
    public async Task<List<ProductCategory>> GetCategoriesAsync() => (await _categories.GetAllAsync()).OrderBy(c => c.Sort).ThenBy(c => c.Name).ToList();
    public async Task SaveCategoryAsync(ProductCategory c, string? by)
    {
        c.UpdatedBy = by;
        var previous = c.Id is null ? null : await _categories.GetByIdAsync(c.Id);
        if (c.Id is null) await _categories.InsertAsync(c); else await _categories.UpdateAsync(c);
        if (previous is not null && previous.Name != c.Name)
            foreach (var p in await _products.FindAsync(x => x.Category == previous.Name)) { p.Category = c.Name; await _products.UpdateAsync(p); }
    }
    public async Task DeleteCategoryAsync(ProductCategory c, string? by)
    {
        foreach (var p in await _products.FindAsync(x => x.Category == c.Name)) { p.Category = null; await _products.UpdateAsync(p); }
        await _categories.PurgeAsync(c.Id!, by);
    }

    public Task<Product?> GetProductAsync(string? id) => _products.GetByIdAsync(id);

    /// <summary>Catálogo público: productos activos y publicados, con filtros opcionales.</summary>
    public async Task<List<Product>> GetCatalogAsync(string? category = null, string? tag = null, string? q = null)
    {
        var items = (await _products.GetAllAsync()).Where(p => p.IsActive && p.IsPublic && !p.IsArchived);
        if (!string.IsNullOrWhiteSpace(category)) items = items.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(tag)) items = items.Where(p => p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(q)) items = items.Where(p => $"{p.Name} {p.Sku} {p.Description} {p.Category} {string.Join(' ', p.Tags)}".Contains(q, StringComparison.OrdinalIgnoreCase));
        return items.OrderBy(p => p.Category).ThenBy(p => p.Name).ToList();
    }

    public static object ToPublic(Product p, string baseUrl) => new
    {
        id = p.Id, name = p.Name, sku = p.Sku, cabys = p.Cabys, description = p.Description, category = p.Category, tags = p.Tags,
        unit = p.Unit, price = p.Price, currency = p.Currency, taxRate = p.TaxRate, stock = p.Stock < 0 ? (int?)null : p.Stock,
        image = p.MainImageId is null ? null : $"{baseUrl}/catalog/images/{p.MainImageId}",
        images = p.ImageIds.Select(i => $"{baseUrl}/catalog/images/{i}").ToList(),
        updatedAt = p.UpdatedAt
    };

    public Task<List<Product>> GetProductsAsync() => _products.GetAllAsync();
    public async Task SaveProductAsync(Product p, string? by) { p.UpdatedBy = by; if (p.Id is null) await _products.InsertAsync(p); else await _products.UpdateAsync(p); }
    public Task DeleteProductAsync(string id, string? by) => _products.DeleteAsync(id, by);

    public Task<List<Quote>> GetAllAsync() => _quotes.GetAllAsync();
    public Task<Quote?> GetAsync(string? id) => _quotes.GetByIdAsync(id);
    public Task<List<Quote>> GetForDealAsync(string dealId) => _quotes.FindAsync(q => q.DealId == dealId);

    public async Task<Quote> CreateForDealAsync(Deal deal, string author)
    {
        var s = await _settings.GetAsync();
        var q = new Quote
        {
            Number = s.QuotePrefix + (await _settings.NextQuoteNumberAsync()).ToString("D4"),
            Title = deal.Title, DealId = deal.Id, ContactId = deal.ContactId, CompanyId = deal.CompanyId, Currency = deal.Currency,
            ValidUntil = DateTime.UtcNow.Date.AddDays(s.QuoteValidDays), Terms = s.QuoteTerms, PreparedBy = author, CreatedBy = author
        };
        await _quotes.InsertAsync(q);
        await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.System, $"📄 Creó la cotización {q.Number}", author);
        return q;
    }

    public async Task SaveAsync(Quote q, string? by)
    {
        q.UpdatedBy = by;
        await _quotes.UpdateAsync(q);
    }

    public async Task SetStatusAsync(Quote q, string status, string author)
    {
        q.Status = status; q.UpdatedBy = author;
        await _quotes.UpdateAsync(q);
        if (q.DealId is not null)
        {
            var deal = await _deals.GetByIdAsync(q.DealId);
            if (deal is not null)
            {
                await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.System, $"📄 Cotización {q.Number}: {QuoteStatuses.Get(status).Label}" + (status == "accepted" ? $" · importe {q.Total:N2} {q.Currency}" : ""), author);
                if (status == "accepted" && deal.Amount != q.Total) { deal.Amount = q.Total; deal.Currency = q.Currency; deal.UpdatedBy = author; await _deals.UpdateAsync(deal); }
            }
        }
    }

    public Task DeleteAsync(string id, string? by) => _quotes.DeleteAsync(id, by);
}
