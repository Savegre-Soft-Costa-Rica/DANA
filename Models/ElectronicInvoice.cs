namespace Dana.Models;

public static class InvoiceStatuses
{
    public static readonly (string Id, string Label, string Color)[] All =
    [
        ("draft", "Borrador", "#9ca3af"), ("submitted", "Enviada a Hacienda", "#0073ea"), ("accepted", "Aceptada", "#00c875"), ("rejected", "Rechazada", "#e2445c"), ("cancelled", "Anulada", "#fdab3d"),
    ];
    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

/// <summary>Línea de factura. Hacienda exige el código CABYS (13 dígitos) en cada línea.</summary>
public class InvoiceLine
{
    public string Cabys { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "Unid";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxRate { get; set; } = 13m;

    public decimal Subtotal => Math.Round(Quantity * UnitPrice * (1 - DiscountPercent / 100m), 2);
    public decimal Tax => Math.Round(Subtotal * TaxRate / 100m, 2);
    public decimal Total => Subtotal + Tax;
}

/// <summary>Factura electrónica emitida a través de un proveedor autorizado por Hacienda.</summary>
public class ElectronicInvoice : Entity
{
    public string Status { get; set; } = "draft";
    /// <summary>invoice = factura; credit = nota de crédito (referencia a una factura).</summary>
    public string Kind { get; set; } = "invoice";
    /// <summary>Id de la factura original (para notas de crédito).</summary>
    public string? ReferenceId { get; set; }

    /// <summary>Clave numérica de 50 dígitos que asigna el proveedor al enviar el comprobante.</summary>
    public string? Clave { get; set; }
    public string? ExternalId { get; set; }
    public string Provider { get; set; } = "";
    public string? LastError { get; set; }

    public string? DealId { get; set; }
    public string? QuoteId { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }

    public string ReceiverName { get; set; } = "";
    /// <summary>Cédula física, jurídica, DIMEX o NITE del receptor.</summary>
    public string ReceiverId { get; set; } = "";
    public string? ReceiverEmail { get; set; }

    public string Currency { get; set; } = "CRC";
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public string? Notes { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();

    public decimal Subtotal => Lines.Sum(l => l.Subtotal);
    public decimal Tax => Lines.Sum(l => l.Tax);
    public decimal Total => Subtotal + Tax;
}
