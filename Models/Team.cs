using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public class Team : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#0073ea";
    public List<string> Members { get; set; } = new();   // nombres de usuario
}

/// <summary>Nivel de acceso de un usuario a un tablero.</summary>
public static class AccessLevels
{
    public const string Edit = "edit";   // ver y editar todo
    public const string Team = "team";   // ver/editar lo suyo y lo de su equipo
    public const string Own = "own";     // solo lo asignado a la persona
    public const string View = "view";   // ver todo, sin editar
    public const string None = "none";   // sin acceso

    public static readonly (string Id, string Label)[] All =
    [
        (Edit, "Editar todo"), (Team, "Su equipo"), (Own, "Solo lo suyo"), (View, "Solo lectura"), (None, "Sin acceso"),
    ];
}
