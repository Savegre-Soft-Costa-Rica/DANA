using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public static class QuoteStatuses
{
    public static readonly (string Id, string Label, string Color)[] All =
    [
        ("draft", "Borrador", "#9ca3af"), ("sent", "Enviada", "#0073ea"), ("accepted", "Aceptada", "#00c875"), ("rejected", "Rechazada", "#e2445c"), ("expired", "Vencida", "#fdab3d"),
    ];
    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

public class QuoteLine
{
    public string? ProductId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "unidad";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxRate { get; set; } = 13m;

    public decimal Subtotal => Math.Round(Quantity * UnitPrice * (1 - DiscountPercent / 100m), 2);
    public decimal Tax => Math.Round(Subtotal * TaxRate / 100m, 2);
    public decimal Total => Subtotal + Tax;
}

/// <summary>Cotización ligada a una negociación (y a su contacto/compañía).</summary>
public class Quote : Entity
{
    public string Number { get; set; } = "";
    [Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string? DealId { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public DateTime? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public string? Terms { get; set; }
    public string? PreparedBy { get; set; }
    public List<QuoteLine> Lines { get; set; } = new();

    public decimal Subtotal => Lines.Sum(l => l.Subtotal);
    public decimal Tax => Lines.Sum(l => l.Tax);
    public decimal Total => Subtotal + Tax;
}
