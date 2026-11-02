using MongoDB.Bson.Serialization.Attributes;

namespace Dana.Models;

public static class ChatKinds
{
    public const string Dm = "dm";        // conversación directa entre dos personas
    public const string Group = "group";  // grupo con varios miembros
}

/// <summary>Canal de chat interno: directo (2 personas) o grupo. Los miembros se identifican por nombre de usuario.</summary>
public class ChatChannel : Entity
{
    public string Kind { get; set; } = ChatKinds.Dm;
    /// <summary>Nombre del grupo (los directos muestran el nombre de la otra persona).</summary>
    public string? Name { get; set; }
    public string Color { get; set; } = "#0073ea";
    public List<string> Members { get; set; } = new();

    public string? LastText { get; set; }
    public string? LastAuthor { get; set; }
    public DateTime? LastAt { get; set; }

    /// <summary>Última lectura por usuario (para contar no leídos).</summary>
    public Dictionary<string, DateTime> LastRead { get; set; } = new();

    public bool IsMember(string user) => Members.Contains(user, StringComparer.OrdinalIgnoreCase);
    public string TitleFor(string user) =>
        Kind == ChatKinds.Group ? (Name ?? "Grupo")
        : Members.FirstOrDefault(m => !string.Equals(m, user, StringComparison.OrdinalIgnoreCase)) ?? "Directo";
}

public class ChatMessage : Entity
{
    public string ChannelId { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    [BsonIgnore] public DateTime At => CreatedAt;
}
