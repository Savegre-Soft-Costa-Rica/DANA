namespace Dana.Models;

/// <summary>Listas fijas de referencia (equivalentes a los "campos de lista" de Bitrix).</summary>
public static class Catalogs
{
    public static readonly string[] Sources =
        ["Llamada", "Sitio web", "Carrito", "Recomendación", "Redes sociales", "Correo", "Feria / evento", "Publicidad", "Otro"];

    /// <summary>Origen de los prospectos generados desde el carrito del catálogo público.</summary>
    public const string CartSource = "Carrito";

    public static readonly string[] ContactTypes = ["Cliente", "Proveedor", "Socio", "Prospecto", "Otro"];

    public static readonly string[] CompanyTypes = ["Cliente", "Proveedor", "Competidor", "Socio", "Otro"];

    public static readonly string[] Industries =
        ["Tecnología", "Manufactura", "Construcción", "Comercio", "Servicios", "Salud", "Educación", "Gobierno", "Finanzas", "Otro"];

    public static readonly string[] DealTypes = ["Venta", "Servicio", "Venta recurrente", "Soporte", "Otro"];

    public static readonly string[] Currencies = ["USD", "CRC", "EUR", "MXN"];
}

public record LeadStatus(string Id, string Name, string Color, bool IsFinal);

public static class LeadStatuses
{
    public const string New = "NEW";
    public const string InProcess = "IN_PROCESS";
    public const string Qualified = "QUALIFIED";
    public const string Junk = "JUNK";
    public const string Converted = "CONVERTED";

    public static readonly LeadStatus[] All =
    [
        new(New, "Nuevo", "#3b82f6", false),
        new(InProcess, "En proceso", "#f59e0b", false),
        new(Qualified, "Calificado", "#8b5cf6", false),
        new(Junk, "No calificado", "#ef4444", true),
        new(Converted, "Convertido", "#10b981", true),
    ];

    public static LeadStatus Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}
