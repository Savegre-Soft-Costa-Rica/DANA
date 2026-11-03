namespace Dana.Models;

public static class RecurFrequencies
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public static readonly (string Id, string Label)[] All = [(Weekly, "Semanal"), (Monthly, "Mensual"), (Yearly, "Anual")];
    public static string Label(string id) => All.FirstOrDefault(f => f.Id == id).Label ?? id;
}

/// <summary>Plantilla de facturación recurrente: genera facturas periódicamente (y opcionalmente las emite y cobra).</summary>
public class RecurringInvoice : Entity
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // Receptor
    public string ReceiverName { get; set; } = "";
    public string ReceiverId { get; set; } = "";
    public string? ReceiverEmail { get; set; }
    public string Currency { get; set; } = "CRC";
    public List<InvoiceLine> Lines { get; set; } = new();

    // Periodicidad
    public string Frequency { get; set; } = RecurFrequencies.Monthly;
    public DateTime NextRunAt { get; set; } = DateTime.UtcNow.Date.AddDays(1);

    public bool AutoEmit { get; set; } = true;     // emitir al generar (si no, deja borrador)
    public bool AutoCharge { get; set; }            // generar enlace de pago al emitir

    public string? DealId { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }

    public DateTime? LastRunAt { get; set; }
    public int RunCount { get; set; }
    public string? LastError { get; set; }

    public decimal Total => Lines.Sum(l => l.Total);

    public DateTime ComputeNext(DateTime from) => Frequency switch
    {
        RecurFrequencies.Weekly => from.AddDays(7),
        RecurFrequencies.Yearly => from.AddYears(1),
        _ => from.AddMonths(1),
    };
}
