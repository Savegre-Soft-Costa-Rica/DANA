using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public class Contact : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string? LastName { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Correo inválido")]
    public string? Email { get; set; }
    public string? CompanyId { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }
    public string? Address { get; set; }
    public string? AssignedTo { get; set; }
    public string? Comments { get; set; }

    public string FullName => string.Join(" ", new[] { Name, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
