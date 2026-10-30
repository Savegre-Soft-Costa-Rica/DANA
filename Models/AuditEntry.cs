namespace Dana.Models;

public class AuditChange
{
    public string Field { get; set; } = "";
    public string? Old { get; set; }
    public string? New { get; set; }
}

/// <summary>Registro de auditoría: quién cambió qué y cuándo.</summary>
public class AuditEntry : Entity
{
    public string User { get; set; } = "";
    public string Action { get; set; } = "";       // create | update | delete | restore | purge
    public string Collection { get; set; } = "";   // leads | deals | contacts | …
    public string EntityId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<AuditChange> Changes { get; set; } = new();
}
