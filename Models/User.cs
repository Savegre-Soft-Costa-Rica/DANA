using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "Usuario";
    public static readonly string[] All = [Admin, User];
}

public class User : Entity
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Picture { get; set; }

    /// <summary>Hash de contraseña (null si solo entra con Google).</summary>
    public string? PasswordHash { get; set; }
    public bool HasGoogle { get; set; }

    public string Role { get; set; } = Roles.User;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Estado de ánimo (0-100) y nota breve, igual que los agentes. Lo fija cada persona.</summary>
    public int Mood { get; set; } = 70;
    public string? MoodNote { get; set; }

    /// <summary>Acceso por tablero (leads/deals/contacts/companies → AccessLevels). Ausente = editar todo.</summary>
    public Dictionary<string, string> Permissions { get; set; } = new();

    /// <summary>Personalización del menú lateral.</summary>
    public MenuConfig? Menu { get; set; }
}
