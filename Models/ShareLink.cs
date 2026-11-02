using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public static class ShareKinds
{
    public const string Catalog = "catalog";    // vista pública del catálogo de productos y servicios
    public const string Document = "document";   // visor público de solo lectura de un documento
}

/// <summary>Enlace público compartible con expiración opcional. Sirve para la vista pública del
/// catálogo (/catalogo/{token}) y para el visor de solo lectura de documentos (/doc/{token}).</summary>
public class ShareLink : Entity
{
    /// <summary>Token aleatorio que forma parte de la URL pública.</summary>
    public string Token { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Tipo de recurso compartido (ver <see cref="ShareKinds"/>).</summary>
    public string Kind { get; set; } = ShareKinds.Catalog;

    /// <summary>Id del objeto compartido (p. ej. el documento). Null = catálogo completo.</summary>
    public string? TargetId { get; set; }

    [Required(ErrorMessage = "Indique un nombre para identificar el enlace")]
    public string Name { get; set; } = "";

    /// <summary>Título visible para el público.</summary>
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }

    /// <summary>Solo catálogo: si se indica, el enlace muestra únicamente esa categoría.</summary>
    public string? Category { get; set; }

    public string AccentColor { get; set; } = "#0073ea";

    /// <summary>Pausado manualmente (se puede detener sin revocar).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Fecha/hora de expiración en UTC. Null = sin expiración.</summary>
    public DateTime? ExpiresAt { get; set; }

    public int ViewCount { get; set; }
    public DateTime? LastViewAt { get; set; }

    [BsonIgnore] public bool IsExpired => ExpiresAt is not null && ExpiresAt <= DateTime.UtcNow;
    /// <summary>El enlace es utilizable: habilitado, no expirado y no revocado.</summary>
    [BsonIgnore] public bool IsLive => IsEnabled && !IsExpired && DeletedAt == null;
}
