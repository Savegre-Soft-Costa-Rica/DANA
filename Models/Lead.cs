using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

/// <summary>Prospecto (Lead en Bitrix): contacto potencial aún no calificado.</summary>
public class Lead : Entity
{
    [Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";

    public string? Name { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Correo inválido")]
    public string? Email { get; set; }
    public string? Source { get; set; }
    public string Status { get; set; } = LeadStatuses.New;

    [Range(0, double.MaxValue, ErrorMessage = "Debe ser mayor o igual a 0")]
    public decimal Opportunity { get; set; }
    public string Currency { get; set; } = "USD";
    public string? AssignedTo { get; set; }
    public string? Comments { get; set; }

    /// <summary>Contactos adicionales del prospecto (además del principal de los campos de arriba).</summary>
    public List<LeadContact> Contacts { get; set; } = new();

    // Rellenado al convertir el prospecto
    public string? ConvertedContactId { get; set; }
    public string? ConvertedCompanyId { get; set; }
    public string? ConvertedDealId { get; set; }

    public string FullName => string.Join(" ", new[] { Name, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Un contacto adicional dentro de un prospecto.</summary>
public class LeadContact
{
    public string? Name { get; set; }
    public string? LastName { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Correo inválido")]
    public string? Email { get; set; }

    public string FullName => string.Join(" ", new[] { Name, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(LastName)
        && string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Position);
}
