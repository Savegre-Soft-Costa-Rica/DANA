using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "Usuario";
    /// <summary>Solo lectura: puede ver todo pero no crear, modificar ni eliminar nada.</summary>
    public const string Viewer = "Solo lectura";
    public static readonly string[] All = [Admin, User, Viewer];
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

    /// <summary>Correo SMTP personal del empleado para enviar desde el CRM.</summary>
    public UserEmailSettings? EmailSettings { get; set; }
}

/// <summary>Configuración SMTP personal de un usuario (la contraseña se guarda cifrada).</summary>
public class UserEmailSettings
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    /// <summary>Contraseña/clave de aplicación cifrada (Data Protection). Nunca se expone en texto plano.</summary>
    public string? PasswordEnc { get; set; }
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
    public bool UseStartTls { get; set; } = true;

    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(FromEmail)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(PasswordEnc);
}
