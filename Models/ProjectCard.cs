using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

/// <summary>Etiquetas de color disponibles para las tarjetas de proyecto.</summary>
public static class CardLabels
{
    public static readonly (string Id, string Name, string Color)[] All =
    [
        ("green", "Verde", "#00c875"),
        ("blue", "Azul", "#0073ea"),
        ("purple", "Morado", "#a25ddc"),
        ("orange", "Naranja", "#fdab3d"),
        ("red", "Rojo", "#e2445c"),
    ];

    public static string ColorOf(string? id) => All.FirstOrDefault(l => l.Id == id).Color ?? "";
    public static string NameOf(string? id) => All.FirstOrDefault(l => l.Id == id).Name ?? "";
}

/// <summary>Niveles de prioridad de una tarjeta.</summary>
public static class CardPriorities
{
    public static readonly (string Id, string Name, string Color)[] All =
    [
        ("none", "Sin prioridad", "#c3c6d4"),
        ("low", "Baja", "#00c875"),
        ("medium", "Media", "#fdab3d"),
        ("high", "Alta", "#ff9900"),
        ("urgent", "Urgente", "#e2445c"),
    ];

    public static (string Id, string Name, string Color) Get(string? id) => All.FirstOrDefault(p => p.Id == id, All[0]);
    public static string ColorOf(string? id) => Get(id).Color;
    public static string NameOf(string? id) => Get(id).Name;
}

/// <summary>Elemento de una lista de verificación (checklist) de una tarjeta.</summary>
public class ChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>Responsable asignado a este ítem de la lista de verificación.</summary>
    public string? AssignedTo { get; set; }
}

/// <summary>Comentario en una tarjeta.</summary>
public class CardComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Tarjeta de un tablero de proyectos (pertenece a un tablero y a una lista/columna).</summary>
public class ProjectCard : Entity
{
    public string BoardId { get; set; } = "";
    public string ListId { get; set; } = "";

    [Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? DueAt { get; set; }
    public string? Label { get; set; }
    public string Priority { get; set; } = "none";
    public bool IsDone { get; set; }
    public int Sort { get; set; }

    public List<ChecklistItem> Checklist { get; set; } = new();
    public List<CardComment> Comments { get; set; } = new();

    public bool IsOverdue => !IsDone && DueAt is not null && DueAt < DateTime.UtcNow;

    public int ChecklistDone => Checklist.Count(i => i.Done);
    public bool HasChecklist => Checklist.Count > 0;
}
