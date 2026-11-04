using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public static class RecurFrequencies
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public static readonly (string Id, string Label)[] All = [(Weekly, "Semanal"), (Monthly, "Mensual"), (Yearly, "Anual")];
    public static string Label(string id) => All.FirstOrDefault(f => f.Id == id).Label ?? id;
}

/// <summary>Estado del ciclo de vida de una suscripción.</summary>
public enum SubscriptionStatus { Active, Trialing, Paused, Cancelled, Ended }

public static class SubscriptionStatuses
{
    public static readonly (SubscriptionStatus Status, string Label, string Color)[] All =
    [
        (SubscriptionStatus.Active, "Activa", "#00c875"),
        (SubscriptionStatus.Trialing, "En prueba", "#0073ea"),
        (SubscriptionStatus.Paused, "Pausada", "#fdab3d"),
        (SubscriptionStatus.Cancelled, "Cancelada", "#e2445c"),
        (SubscriptionStatus.Ended, "Finalizada", "#9ca3af"),
    ];
    public static (SubscriptionStatus Status, string Label, string Color) Get(SubscriptionStatus s) => All.FirstOrDefault(x => x.Status == s, All[0]);
}

/// <summary>Suscripción / facturación recurrente: genera facturas por periodo (y opcionalmente las emite y cobra).
/// Representa la relación recurrente con el cliente (SaaS); su ciclo de vida la distingue de una simple plantilla.</summary>
public class RecurringInvoice : Entity
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Estado del ciclo de vida. Solo las activas (o en prueba) generan facturas.</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    /// <summary>Fecha programada de cancelación (al final del periodo) y fecha efectiva de cancelación.</summary>
    public DateTime? CancelAt { get; set; }
    public DateTime? CancelledAt { get; set; }

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

    /// <summary>Ingreso recurrente mensual normalizado (MRR), a partir del total por ciclo y la frecuencia.</summary>
    [BsonIgnore]
    public decimal Mrr => Frequency switch
    {
        RecurFrequencies.Weekly => Math.Round(Total * 52 / 12m, 2),
        RecurFrequencies.Yearly => Math.Round(Total / 12m, 2),
        _ => Total,
    };

    public DateTime ComputeNext(DateTime from) => Frequency switch
    {
        RecurFrequencies.Weekly => from.AddDays(7),
        RecurFrequencies.Yearly => from.AddYears(1),
        _ => from.AddMonths(1),
    };
}
