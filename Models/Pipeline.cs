using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;

namespace Dana.Models;

public enum StageKind
{
    Process,  // etapa intermedia
    Success,  // negociación ganada
    Failure   // negociación perdida
}

/// <summary>Etapa dentro de un embudo (embebida en el documento del Pipeline).</summary>
public class Stage
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3b82f6";
    public int Sort { get; set; }
    public StageKind Kind { get; set; } = StageKind.Process;

    public bool IsFinal => Kind != StageKind.Process;
}

/// <summary>Embudo de ventas (Deal category / Pipeline en Bitrix). Agrupa negociaciones y define sus etapas.</summary>
public class Pipeline : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public int Sort { get; set; }
    public List<Stage> Stages { get; set; } = new();

    public IEnumerable<Stage> OrderedStages => Stages.OrderBy(s => s.Sort);
    public Stage? FirstStage => Stages.Where(s => s.Kind == StageKind.Process).OrderBy(s => s.Sort).FirstOrDefault() ?? Stages.OrderBy(s => s.Sort).FirstOrDefault();
    public Stage? GetStage(string? id) => Stages.FirstOrDefault(s => s.Id == id);

    public static Pipeline CreateDefault(string name = "Ventas", bool isDefault = true) => new()
    {
        Name = name,
        IsDefault = isDefault,
        Stages =
        [
            new Stage { Name = "Nuevo", Color = "#3b82f6", Sort = 10 },
            new Stage { Name = "Contacto inicial", Color = "#06b6d4", Sort = 20 },
            new Stage { Name = "Propuesta enviada", Color = "#f59e0b", Sort = 30 },
            new Stage { Name = "Negociación", Color = "#8b5cf6", Sort = 40 },
            new Stage { Name = "Ganada", Color = "#10b981", Sort = 90, Kind = StageKind.Success },
            new Stage { Name = "Perdida", Color = "#ef4444", Sort = 100, Kind = StageKind.Failure },
        ]
    };
}
