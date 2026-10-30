using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace Dana.Models;

/// <summary>Canal de entrada de prospectos por API (sitio web, landing, n8n…). Cada canal tiene su clave y su configuración.</summary>
public class ApiChannel : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Key { get; set; } = NewKey();
    public bool IsEnabled { get; set; } = true;
    /// <summary>Permite consultar el catálogo de productos con la clave de este canal.</summary>
    public bool AllowCatalog { get; set; } = true;

    // Cómo se crea el prospecto
    public string Source { get; set; } = "Sitio web";
    public string Status { get; set; } = LeadStatuses.New;
    /// <summary>Nombre de usuario, vacío (sin asignar) o "@roundrobin" (rotación entre usuarios activos).</summary>
    public string? AssignTo { get; set; }
    public string TitleTemplate { get; set; } = "Web: {{nombre}} {{apellido}}";
    public List<string> NotifyUsers { get; set; } = new();
    public bool NotifyAssignee { get; set; } = true;

    /// <summary>Orígenes permitidos (cabecera Origin). Vacío = cualquiera.</summary>
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>Mapeo campo del prospecto → nombres aceptados en el JSON (separados por coma).</summary>
    public Dictionary<string, string> FieldMap { get; set; } = DefaultFieldMap();

    public int ReceivedCount { get; set; }
    public DateTime? LastReceivedAt { get; set; }
    public string? LastPayload { get; set; }
    public string? LastError { get; set; }

    public static string NewKey() => "dana_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    public static readonly (string Field, string Label)[] LeadFields =
    [
        ("name", "Nombre"), ("lastName", "Apellido"), ("email", "Correo"), ("phone", "Teléfono"), ("company", "Compañía"),
        ("message", "Mensaje / comentarios"), ("title", "Título del prospecto"), ("opportunity", "Monto de oportunidad"), ("source", "Fuente"),
    ];

    public static Dictionary<string, string> DefaultFieldMap() => new()
    {
        ["name"] = "name, nombre, firstName, first_name, fullName, full_name",
        ["lastName"] = "lastName, last_name, apellido, apellidos, surname",
        ["email"] = "email, correo, mail, e-mail",
        ["phone"] = "phone, telefono, teléfono, tel, celular, mobile, whatsapp",
        ["company"] = "company, empresa, compañia, compañía, organization, organizacion",
        ["message"] = "message, mensaje, comentarios, comments, body, consulta, description",
        ["title"] = "title, titulo, asunto, subject",
        ["opportunity"] = "opportunity, monto, amount, presupuesto, budget",
        ["source"] = "source, fuente, origen, utm_source",
    };
}
