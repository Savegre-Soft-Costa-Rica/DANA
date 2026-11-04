using Dana.Data;
using Dana.Models;
using Dana.Services.Integrations;

namespace Dana.Services.Invoicing;

/// <summary>Planes de pago de ventas únicas: contado, tractos (una factura por tracto) o crédito (neto X días).
/// Genera, por cada cuota, su factura electrónica (borrador) y su enlace de cobro.</summary>
public class PaymentPlanService
{
    private readonly IRepository<PaymentPlan> _plans;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly InvoiceService _invoices;
    private readonly TilopayService _tilopay;

    public PaymentPlanService(IRepository<PaymentPlan> plans, IRepository<Contact> contacts, IRepository<Company> companies, InvoiceService invoices, TilopayService tilopay)
    {
        _plans = plans; _contacts = contacts; _companies = companies; _invoices = invoices; _tilopay = tilopay;
    }

    public Task<List<PaymentPlan>> GetAllAsync() => _plans.GetAllAsync();
    public Task<PaymentPlan?> GetAsync(string? id) => _plans.GetByIdAsync(id);
    public Task DeleteAsync(string id, string? by) => _plans.DeleteAsync(id, by);

    public async Task<(bool Ok, string? Error)> SaveAsync(PaymentPlan plan, string? by)
    {
        if (string.IsNullOrWhiteSpace(plan.Title)) return (false, "El plan necesita un título.");
        if (plan.Installments.Count == 0) return (false, "El plan no tiene cuotas.");
        plan.UpdatedBy = by;
        if (plan.Id is null) await _plans.InsertAsync(plan);
        else await _plans.UpdateAsync(plan);
        return (true, null);
    }

    /// <summary>Arma las cuotas según la condición de pago. Los tractos se reparten en partes iguales (la última
    /// absorbe el redondeo) con vencimiento mensual; contado vence hoy y crédito a los <paramref name="netDays"/> días.</summary>
    public static List<Installment> BuildInstallments(PaymentTerm term, decimal total, int? netDays, int tractos)
    {
        var today = DateTime.UtcNow.Date;
        switch (term)
        {
            case PaymentTerm.Contado:
                return [new Installment { Label = "Pago de contado", DueDate = today, Amount = total }];

            case PaymentTerm.Credito:
                var days = netDays is > 0 ? netDays.Value : 30;
                return [new Installment { Label = $"Crédito {days} días", DueDate = today.AddDays(days), Amount = total }];

            case PaymentTerm.Tractos:
                var n = Math.Max(2, tractos);
                var each = Math.Round(total / n, 2);
                var list = new List<Installment>();
                for (var i = 0; i < n; i++)
                {
                    var amount = i == n - 1 ? total - each * (n - 1) : each;   // la última cuota cuadra el total
                    list.Add(new Installment { Label = $"Tracto {i + 1}/{n}", DueDate = today.AddMonths(i), Amount = amount });
                }
                return list;

            default:
                return [];
        }
    }

    /// <summary>Crea un plan a partir de una cotización (hereda cliente, total, moneda y referencias).</summary>
    public async Task<PaymentPlan> CreateFromQuoteAsync(Quote q, PaymentTerm term, int? netDays, int tractos, string? by)
    {
        var plan = new PaymentPlan
        {
            Title = q.Title, Term = term, TotalAmount = q.Total, Currency = q.Currency, NetDays = netDays,
            DealId = q.DealId, QuoteId = q.Id, ContactId = q.ContactId, CompanyId = q.CompanyId,
            Installments = BuildInstallments(term, q.Total, netDays, tractos),
        };
        await SaveAsync(plan, by);
        return plan;
    }

    /// <summary>Crea la factura (borrador) de un tracto. El usuario completa CABYS/impuesto y la emite en Facturas.</summary>
    public async Task<(bool Ok, string? Error)> InvoiceInstallmentAsync(string planId, int index, string? by)
    {
        var plan = await _plans.GetByIdAsync(planId);
        if (plan is null) return (false, "Plan no encontrado.");
        if (index < 0 || index >= plan.Installments.Count) return (false, "Cuota inválida.");
        var inst = plan.Installments[index];
        if (inst.InvoiceId is not null) return (false, "Esta cuota ya tiene factura.");

        var (name, taxId, email) = await ReceiverAsync(plan);
        var inv = new ElectronicInvoice
        {
            ReceiverName = name, ReceiverId = taxId, ReceiverEmail = email, Currency = plan.Currency,
            DealId = plan.DealId, QuoteId = plan.QuoteId, ContactId = plan.ContactId, CompanyId = plan.CompanyId,
            Notes = $"Plan de pago: {plan.Title} · {inst.Label}",
            // Una línea con el monto del tracto, impuesto 0: el usuario ajusta CABYS/impuesto antes de emitir.
            Lines = [new InvoiceLine { Description = $"{plan.Title} · {inst.Label}", Quantity = 1, UnitPrice = inst.Amount, TaxRate = 0 }],
        };
        var (ok, error) = await _invoices.SaveDraftAsync(inv);
        if (!ok) return (false, error);

        inst.InvoiceId = inv.Id;
        inst.Status = InstallmentStatuses.Invoiced;
        await _plans.UpdateAsync(plan);
        return (true, null);
    }

    /// <summary>Genera el enlace de cobro (Tilopay) de un tracto.</summary>
    public async Task<(bool Ok, string? Error)> ChargeInstallmentAsync(string planId, int index, string baseUrl, string by)
    {
        var plan = await _plans.GetByIdAsync(planId);
        if (plan is null) return (false, "Plan no encontrado.");
        if (index < 0 || index >= plan.Installments.Count) return (false, "Cuota inválida.");
        var inst = plan.Installments[index];

        var (name, _, email) = await ReceiverAsync(plan);
        var link = await _tilopay.CreateAsync(new PaymentLink
        {
            Amount = inst.Amount, Currency = plan.Currency, Description = $"{plan.Title} · {inst.Label}",
            InvoiceId = inst.InvoiceId, DealId = plan.DealId, QuoteId = plan.QuoteId, ContactId = plan.ContactId, CompanyId = plan.CompanyId,
            PayerName = name, PayerEmail = email,
        }, baseUrl, by);

        if (link.Url is null) return (false, link.LastError ?? "No se pudo crear el enlace de pago.");
        inst.PaymentLinkId = link.Id;
        if (inst.Status == InstallmentStatuses.Pending) inst.Status = InstallmentStatuses.Invoiced;
        await _plans.UpdateAsync(plan);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> MarkPaidAsync(string planId, int index, bool paid)
    {
        var plan = await _plans.GetByIdAsync(planId);
        if (plan is null) return (false, "Plan no encontrado.");
        if (index < 0 || index >= plan.Installments.Count) return (false, "Cuota inválida.");
        var inst = plan.Installments[index];
        inst.Status = paid ? InstallmentStatuses.Paid : (inst.InvoiceId is not null ? InstallmentStatuses.Invoiced : InstallmentStatuses.Pending);
        inst.PaidAt = paid ? DateTime.UtcNow : null;
        await _plans.UpdateAsync(plan);
        return (true, null);
    }

    private async Task<(string Name, string TaxId, string? Email)> ReceiverAsync(PaymentPlan plan)
    {
        var company = string.IsNullOrEmpty(plan.CompanyId) ? null : await _companies.GetByIdAsync(plan.CompanyId);
        var contact = string.IsNullOrEmpty(plan.ContactId) ? null : await _contacts.GetByIdAsync(plan.ContactId);
        return (company?.Title ?? contact?.FullName ?? plan.Title, company?.TaxId ?? "", contact?.Email ?? company?.Email);
    }
}
