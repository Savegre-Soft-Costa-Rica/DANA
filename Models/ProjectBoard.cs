using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

/// <summary>Tablero de proyectos tipo Trello: contiene listas (columnas) embebidas y, por separado, sus tarjetas.</summary>
public class ProjectBoard : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Color { get; set; } = "#0073ea";
    public int Sort { get; set; }

    /// <summary>Columnas del tablero, en orden.</summary>
    public List<BoardList> Lists { get; set; } = new();

    public List<BoardList> OrderedLists => Lists.OrderBy(l => l.Sort).ToList();

    /// <summary>Listas por defecto al crear un tablero.</summary>
    public static List<BoardList> DefaultLists() =>
    [
        new() { Name = "Por hacer", Sort = 10 },
        new() { Name = "En progreso", Sort = 20 },
        new() { Name = "Hecho", Sort = 30 },
    ];
}

/// <summary>Columna (lista) de un tablero de proyectos.</summary>
public class BoardList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public int Sort { get; set; }
}
