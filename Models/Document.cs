namespace Dana.Models;

public static class DocumentKinds
{
    public static readonly (string Id, string Label, string Emoji)[] All =
    [
        ("nda", "Acuerdo de confidencialidad", "🔒"), ("contrato", "Contrato de servicios", "📜"), ("propuesta", "Propuesta comercial", "📑"),
        ("carta", "Carta / comunicado", "✉️"), ("acta", "Acta de reunión", "📝"), ("otro", "Otro documento", "📄"),
    ];
    public static (string Id, string Label, string Emoji) Get(string id) => All.FirstOrDefault(k => k.Id == id, All[^1]);
}

public static class DocumentStatuses
{
    public static readonly (string Id, string Label, string Color)[] All =
    [
        ("draft", "Borrador", "#9ca3af"), ("review", "En revisión", "#fdab3d"), ("final", "Final", "#0073ea"), ("signed", "Firmado", "#00c875"),
    ];
    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

public class DocumentVersion
{
    public int Number { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? By { get; set; }
    public string? Note { get; set; }
    public string Content { get; set; } = "";
}

/// <summary>Plantilla de documento creada por el usuario (además de las predefinidas).</summary>
public class DocumentTemplateEntity : Entity
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "otro";
    public string? Description { get; set; }
    public string Body { get; set; } = "";
}

/// <summary>Firmante de un documento (firma electrónica simple: nombre, aceptación, trazo y evidencia de fecha/IP).</summary>
public class DocumentSigner
{
    public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Role { get; set; }
    public string Token { get; set; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string? RequestedBy { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignedName { get; set; }
    public string? Ip { get; set; }
    /// <summary>Imagen PNG de la firma (data URL).</summary>
    public string? SignatureImage { get; set; }
    public bool IsSigned => SignedAt is not null;
    public DateTime? LastReminderAt { get; set; }
}

/// <summary>Documento (legal, comercial…) escrito en Markdown, ligado a un registro del CRM, con versiones y exportación a Word.</summary>
public class CrmDocument : Entity
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "El título es obligatorio")]
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "otro";
    public string Status { get; set; } = "draft";
    public string? EntityType { get; set; }   // lead | contact | company | deal
    public string? EntityId { get; set; }
    public string? EntityTitle { get; set; }
    /// <summary>Contenido en Markdown (títulos #, listas, **negrita**, tablas simples).</summary>
    public string Content { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<DocumentVersion> Versions { get; set; } = new();
    public string? Author { get; set; }
    public string? Notes { get; set; }
    public List<DocumentSigner> Signers { get; set; } = new();
    public bool AllSigned => Signers.Count > 0 && Signers.All(x => x.IsSigned);
}
