using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

/// <summary>Formulario público de captación de prospectos (/formulario/{slug}).</summary>
public class LeadForm : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Slug { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "Contáctenos";
    public string? Description { get; set; } = "Déjenos sus datos y le contactaremos a la brevedad.";
    public string SuccessMessage { get; set; } = "¡Gracias! Hemos recibido su solicitud.";
    public string ButtonText { get; set; } = "Enviar";
    public bool IsEnabled { get; set; } = true;
    public string Source { get; set; } = "Sitio web";
    public string? AssignTo { get; set; }
    public string AccentColor { get; set; } = "#0073ea";

    public bool AskLastName { get; set; } = true;
    public bool AskCompany { get; set; } = true;
    public bool AskPhone { get; set; } = true;
    public bool AskEmail { get; set; } = true;
    public bool AskMessage { get; set; } = true;
    public bool RequirePhoneOrEmail { get; set; } = true;

    public int SubmitCount { get; set; }
    public DateTime? LastSubmitAt { get; set; }
}
