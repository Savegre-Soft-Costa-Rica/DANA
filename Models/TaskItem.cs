using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public static class TaskKinds
{
    public const string Call = "call";
    public const string Meeting = "meeting";
    public const string Email = "email";
    public const string Task = "task";

    public static readonly (string Id, string Name, string Icon, string Color)[] All =
    [
        (Call, "Llamada", "📞", "#00c875"),
        (Meeting, "Reunión", "📅", "#0073ea"),
        (Email, "Correo", "✉", "#a25ddc"),
        (Task, "Tarea", "☑", "#fdab3d"),
    ];

    public static (string Id, string Name, string Icon, string Color) Get(string id) => All.FirstOrDefault(k => k.Id == id, All[3]);
}

/// <summary>Actividad planificada (llamada, reunión, correo, tarea) ligada opcionalmente a un elemento del CRM.</summary>
public class TaskItem : Entity
{
    [Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";
    public string Kind { get; set; } = TaskKinds.Task;
    public bool IsDone { get; set; }
    public DateTime? DueAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? AssignedTo { get; set; }
    public string? Notes { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? OverdueNotifiedAt { get; set; }

    public string? EntityType { get; set; }   // lead | contact | company | deal
    public string? EntityId { get; set; }
    public string? EntityTitle { get; set; }

    public bool IsOverdue => !IsDone && DueAt is not null && DueAt < DateTime.UtcNow;
}
