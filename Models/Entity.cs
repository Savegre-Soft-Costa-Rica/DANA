using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public abstract class Entity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Borrado lógico (papelera). Null = activo.</summary>
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    /// <summary>Elemento archivado: no aparece en los tableros salvo que se pida verlos.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Valores de columnas personalizadas (clave = id de la columna, valor serializado como texto).</summary>
    public Dictionary<string, string?> Custom { get; set; } = new();

    /// <summary>Copia profunda vía BSON, sin Id ni fechas (para "Duplicar").</summary>
    public static T Clone<T>(T source) where T : Entity
    {
        var doc = source.ToBsonDocument();
        doc.Remove("_id");
        var copy = BsonSerializer.Deserialize<T>(doc);
        copy.Id = null;
        copy.CreatedAt = copy.UpdatedAt = DateTime.UtcNow;
        copy.DeletedAt = null; copy.DeletedBy = null;
        return copy;
    }
}
