namespace Dana.Models;

/// <summary>Datos del emisor exigidos por Hacienda (Costa Rica) para la factura electrónica v4.4.
/// La razón social, identificación fiscal, correo, teléfono y dirección generales se toman de
/// <see cref="AppSettings"/>; aquí van los campos específicos de Hacienda.</summary>
public class HaciendaSettings
{
    /// <summary>Nombre comercial (opcional).</summary>
    public string? NombreComercial { get; set; }

    /// <summary>Tipo de identificación: 01 física, 02 jurídica, 03 DIMEX, 04 NITE, 05 extranjero.</summary>
    public string TipoIdentificacion { get; set; } = "02";

    /// <summary>Número de identificación del emisor (cédula). Si se deja vacío se usa la "Identificación fiscal" de la empresa.</summary>
    public string? Identificacion { get; set; }

    /// <summary>Códigos de actividad económica ante Hacienda (6 dígitos). La 4.4 admite varias.</summary>
    public List<string> ActividadesEconomicas { get; set; } = new();

    // Ubicación (códigos de Hacienda)
    /// <summary>Provincia (1 dígito).</summary>
    public string? Provincia { get; set; }
    /// <summary>Cantón (2 dígitos).</summary>
    public string? Canton { get; set; }
    /// <summary>Distrito (2 dígitos).</summary>
    public string? Distrito { get; set; }
    /// <summary>Barrio (opcional, códigos de Hacienda).</summary>
    public string? Barrio { get; set; }
    public string? OtrasSenas { get; set; }

    // Contacto fiscal
    public string CodigoPaisTel { get; set; } = "506";
    public string? Telefono { get; set; }
    /// <summary>Correo del emisor donde Hacienda/clientes reciben los comprobantes.</summary>
    public string? CorreoFacturas { get; set; }

    /// <summary>Datos mínimos presentes para poder emitir.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(TipoIdentificacion)
        && ActividadesEconomicas.Any(a => !string.IsNullOrWhiteSpace(a))
        && !string.IsNullOrWhiteSpace(Provincia) && !string.IsNullOrWhiteSpace(Canton) && !string.IsNullOrWhiteSpace(Distrito);
}
