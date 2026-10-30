using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

/// <summary>Plantilla de correo con marcadores: {{nombre}}, {{apellido}}, {{compañía}}, {{correo}}, {{usuario}}, {{titulo}}.</summary>
public class EmailTemplate : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "El asunto es obligatorio")]
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
}
