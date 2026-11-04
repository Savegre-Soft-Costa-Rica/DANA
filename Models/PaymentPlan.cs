using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

/// <summary>Forma de pago de una venta única: de contado, en tractos (cuotas/hitos) o a crédito (neto X días).</summary>
public enum PaymentTerm { Contado, Tractos, Credito }

public static class PaymentTerms
{
    public static readonly (PaymentTerm Term, string Label, string Color)[] All =
    [
        (PaymentTerm.Contado, "Contado", "#00c875"),
        (PaymentTerm.Tractos, "Tractos", "#0073ea"),
        (PaymentTerm.Credito, "Crédito", "#fdab3d"),
    ];
    public static (PaymentTerm Term, string Label, string Color) Get(PaymentTerm t) => All.FirstOrDefault(x => x.Term == t, All[0]);
    public static string Label(PaymentTerm t) => Get(t).Label;
}

public static class InstallmentStatuses
{
    public const string Pending = "pending";     // programado, aún sin facturar
    public const string Invoiced = "invoiced";   // factura emitida, pendiente de cobro
    public const string Paid = "paid";
    public const string Overdue = "overdue";

    public static readonly (string Id, string Label, string Color)[] All =
    [
        (Pending, "Pendiente", "#9ca3af"),
        (Invoiced, "Facturado", "#0073ea"),
        (Paid, "Pagado", "#00c875"),
        (Overdue, "Vencido", "#e2445c"),
    ];
    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

/// <summary>Cuota o tracto dentro de un plan de pago. En Costa Rica cada tracto genera su propia factura.</summary>
public class Installment
{
    public string Label { get; set; } = "";       // "Adelanto 50%", "Entrega", "Cuota 1/6"
    public DateTime DueDate { get; set; } = DateTime.UtcNow.Date;
    public decimal Amount { get; set; }
    public string Status { get; set; } = InstallmentStatuses.Pending;

    /// <summary>Factura electrónica que generó este tracto.</summary>
    public string? InvoiceId { get; set; }
    /// <summary>Enlace de pago (Tilopay) de este tracto.</summary>
    public string? PaymentLinkId { get; set; }
    public DateTime? PaidAt { get; set; }

    [BsonIgnore] public bool IsOverdue => Status != InstallmentStatuses.Paid && DueDate.Date < DateTime.UtcNow.Date;
}

/// <summary>Plan de pago de una venta única: define cómo se cobra el monto (contado, tractos o crédito)
/// y rastrea cada cuota con su factura y cobro. Se vincula a la negociación, cotización y cliente.</summary>
public class PaymentPlan : Entity
{
    public string Title { get; set; } = "";
    public PaymentTerm Term { get; set; } = PaymentTerm.Contado;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "CRC";
    /// <summary>Días de crédito cuando Term = Credito (30 / 60 / 90).</summary>
    public int? NetDays { get; set; }

    public string? DealId { get; set; }
    public string? QuoteId { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }

    public List<Installment> Installments { get; set; } = new();

    [BsonIgnore] public decimal Scheduled => Installments.Sum(i => i.Amount);
    [BsonIgnore] public decimal Paid => Installments.Where(i => i.Status == InstallmentStatuses.Paid).Sum(i => i.Amount);
    [BsonIgnore] public decimal Outstanding => Scheduled - Paid;
}
