using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public class StageChange
{
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? FromStageId { get; set; }
    public string? FromStageName { get; set; }
    public string ToStageId { get; set; } = "";
    public string ToStageName { get; set; } = "";
}

/// <summary>Negociación (Deal en Bitrix). Siempre pertenece a un embudo y está en una etapa de ese embudo.</summary>
public class Deal : Entity
{
    [Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Seleccione un embudo")]
    public string PipelineId { get; set; } = "";
    [Required(ErrorMessage = "Seleccione una etapa")]
    public string StageId { get; set; } = "";

    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string? LeadId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Debe ser mayor o igual a 0")]
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Monto de venta única (setup / proyecto), separado del recurrente para el pronóstico.</summary>
    public decimal OneTimeAmount { get; set; }
    /// <summary>Monto recurrente por ciclo (MRR si la frecuencia es mensual).</summary>
    public decimal RecurringAmount { get; set; }
    /// <summary>Frecuencia del monto recurrente (weekly/monthly/yearly). Ver <see cref="RecurFrequencies"/>.</summary>
    public string? RecurringFrequency { get; set; }

    /// <summary>Valor del contrato estimado a 12 meses: venta única + recurrente anualizado.</summary>
    [BsonIgnore]
    public decimal AnnualContractValue => OneTimeAmount + RecurringAmount * RecurringFrequency switch
    {
        RecurFrequencies.Weekly => 52,
        RecurFrequencies.Yearly => 1,
        _ => 12,
    };
    [Range(0, 100, ErrorMessage = "Entre 0 y 100")]
    public int Probability { get; set; } = 50;
    public DateTime? ExpectedCloseDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? OverdueTriggeredAt { get; set; }
    /// <summary>Última vez que se disparó la automatización de inactividad (dedupe por racha).</summary>
    public DateTime? InactiveTriggeredAt { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }
    public string? AssignedTo { get; set; }
    public string? Comments { get; set; }

    public List<StageChange> History { get; set; } = new();
}
