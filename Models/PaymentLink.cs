using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public static class PaymentStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
    public static readonly (string Id, string Label, string Color)[] All =
    [
        (Pending, "Pendiente", "#fdab3d"), (Paid, "Pagado", "#00c875"), (Cancelled, "Cancelado", "#9ca3af"),
    ];
    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

/// <summary>Enlace de pago (Tilopay). En modo Sandbox se simula con una página interna.</summary>
public class PaymentLink : Entity
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CRC";
    public string Description { get; set; } = "";

    public string Status { get; set; } = PaymentStatuses.Pending;
    public string Provider { get; set; } = "sandbox";
    /// <summary>URL del proveedor (Tilopay) o interna (sandbox) donde se paga.</summary>
    public string? Url { get; set; }
    public string? ExternalId { get; set; }
    public string? LastError { get; set; }

    public string? DealId { get; set; }
    public string? QuoteId { get; set; }
    public string? InvoiceId { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }

    public string? PayerName { get; set; }
    public string? PayerEmail { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    [BsonIgnore] public bool IsExpired => ExpiresAt is not null && ExpiresAt < DateTime.UtcNow;
    [BsonIgnore] public bool IsPayable => Status == PaymentStatuses.Pending && !IsExpired;
}
