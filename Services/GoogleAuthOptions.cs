namespace Dana.Services;

/// <summary>Sección "Authentication" de la configuración.</summary>
public class AuthOptions
{
    public const string Section = "Authentication";

    /// <summary>Permite que cualquiera cree una cuenta desde /cuenta/registro. El primer usuario siempre puede registrarse.</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>Muestra en el login un botón "Probar en modo demo" que entra como invitado (sin cuenta).
    /// Actívelo solo en instancias de demostración: el invitado comparte los datos de prueba del sistema.</summary>
    public bool AllowDemo { get; set; }

    public GoogleAuthOptions Google { get; set; } = new();
}

public class GoogleAuthOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Dominios de correo permitidos para Google (p. ej. "grupomecsa.net"). Vacío = cualquier cuenta Google.</summary>
    public List<string> AllowedDomains { get; set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
