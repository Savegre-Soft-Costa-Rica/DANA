using Dana.Data;
using Dana.Models;
using Dana.Services.Integrations;

namespace Dana.Services.Invoicing;

/// <summary>Facturación recurrente: CRUD de plantillas y generación de las facturas vencidas (lo llama el worker).</summary>
public class RecurringInvoiceService
{
    private readonly IRepository<RecurringInvoice> _repo;
    private readonly InvoiceService _invoices;
    private readonly TilopayService _tilopay;
    private readonly IConfiguration _config;
    private readonly ILogger<RecurringInvoiceService> _logger;

    public RecurringInvoiceService(IRepository<RecurringInvoice> repo, InvoiceService invoices, TilopayService tilopay, IConfiguration config, ILogger<RecurringInvoiceService> logger)
    {
        _repo = repo; _invoices = invoices; _tilopay = tilopay; _config = config; _logger = logger;
    }

    public Task<List<RecurringInvoice>> GetAllAsync() => _repo.GetAllAsync();
    public Task<RecurringInvoice?> GetAsync(string? id) => _repo.GetByIdAsync(id);
    public async Task SaveAsync(RecurringInvoice r, string? by) { r.UpdatedBy = by; if (r.Id is null) await _repo.InsertAsync(r); else await _repo.UpdateAsync(r); }
    public Task DeleteAsync(string id, string? by) => _repo.DeleteAsync(id, by);

    /// <summary>Cambia el estado de la suscripción y mantiene <see cref="RecurringInvoice.Enabled"/> en sincronía con él.</summary>
    public async Task<(bool Ok, string? Error)> SetStatusAsync(string id, SubscriptionStatus status, string? by)
    {
        var r = await _repo.GetByIdAsync(id);
        if (r is null) return (false, "Suscripción no encontrada.");
        r.Status = status;
        r.Enabled = status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;
        if (status == SubscriptionStatus.Cancelled) { r.CancelledAt = DateTime.UtcNow; r.CancelAt = null; }
        else if (status == SubscriptionStatus.Active) { r.CancelledAt = null; r.CancelAt = null; }
        r.UpdatedBy = by;
        await _repo.UpdateAsync(r);
        return (true, null);
    }

    public Task<(bool Ok, string? Error)> PauseAsync(string id, string? by) => SetStatusAsync(id, SubscriptionStatus.Paused, by);
    public Task<(bool Ok, string? Error)> ResumeAsync(string id, string? by) => SetStatusAsync(id, SubscriptionStatus.Active, by);
    public Task<(bool Ok, string? Error)> CancelAsync(string id, string? by) => SetStatusAsync(id, SubscriptionStatus.Cancelled, by);

    /// <summary>Genera las facturas de todas las plantillas vencidas. Devuelve cuántas generó.</summary>
    public async Task<int> GenerateDueAsync()
    {
        var now = DateTime.UtcNow;
        // Solo facturan las suscripciones vigentes (activas o en prueba); las pausadas/canceladas no.
        var vigente = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing };
        var due = (await _repo.GetAllAsync())
            .Where(r => r.Enabled && vigente.Contains(r.Status) && r.Lines.Count > 0 && r.NextRunAt <= now)
            .ToList();
        var count = 0;
        foreach (var r in due)
        {
            try
            {
                await GenerateOneAsync(r, "Recurrente");
                // Saltar al siguiente ciclo futuro (evita backlog si quedó atrasada)
                var next = r.ComputeNext(r.NextRunAt);
                while (next <= now) next = r.ComputeNext(next);
                r.NextRunAt = next;
                r.LastRunAt = now; r.RunCount++; r.LastError = null;
                count++;
            }
            catch (Exception ex) { r.LastError = ex.Message; _logger.LogWarning(ex, "Recurrente {Name} falló", r.Name); }
            await _repo.UpdateAsync(r);
        }
        return count;
    }

    /// <summary>Genera una factura a partir de la plantilla (emite y cobra según su configuración).</summary>
    public async Task<ElectronicInvoice> GenerateOneAsync(RecurringInvoice r, string by)
    {
        var inv = new ElectronicInvoice
        {
            ReceiverName = r.ReceiverName, ReceiverId = r.ReceiverId, ReceiverEmail = r.ReceiverEmail,
            Currency = r.Currency, DealId = r.DealId, ContactId = r.ContactId, CompanyId = r.CompanyId,
            Notes = $"Factura recurrente: {r.Name}",
            Lines = r.Lines.Select(l => new InvoiceLine { Cabys = l.Cabys, Description = l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, TaxRate = l.TaxRate }).ToList(),
        };
        await _invoices.SaveDraftAsync(inv);

        if (r.AutoEmit && InvoiceService.Validate(inv) is null)
        {
            var (ok, _) = await _invoices.SubmitAsync(inv.Id!);
            if (ok) await _invoices.RefreshStatusAsync(inv.Id!);
        }

        if (r.AutoCharge)
        {
            var baseUrl = _config["PublicBaseUrl"] ?? "http://localhost:5025";
            await _tilopay.CreateAsync(new PaymentLink
            {
                Amount = inv.Total, Currency = inv.Currency, Description = $"Factura {inv.Clave ?? inv.Id}",
                InvoiceId = inv.Id, DealId = r.DealId, ContactId = r.ContactId, CompanyId = r.CompanyId,
                PayerName = r.ReceiverName, PayerEmail = r.ReceiverEmail,
            }, baseUrl, by);
        }
        return inv;
    }
}
