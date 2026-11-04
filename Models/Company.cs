using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public class Company : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Title { get; set; } = "";
    public string? Type { get; set; }
    public string? Industry { get; set; }
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Correo inválido")]
    public string? Email { get; set; }
    public string? Web { get; set; }
    public string? Address { get; set; }
    public decimal? AnnualRevenue { get; set; }
    public int? Employees { get; set; }
    public string? AssignedTo { get; set; }
    public string? Comments { get; set; }

    /// <summary>Límite de crédito aprobado para el cliente (0 = sin límite). Se compara contra el saldo por cobrar.</summary>
    public decimal CreditLimit { get; set; }
}
