namespace Dana.Models;

public static class ActivityKinds
{
    public const string Note = "note";        // actualización escrita por un usuario
    public const string Created = "created";
    public const string Stage = "stage";
    public const string Converted = "converted";
    public const string System = "system";
    public const string Task = "task";
    public const string Email = "email";
    public const string Sms = "sms";
    public const string WhatsApp = "whatsapp";
}

/// <summary>Actualización / evento en la línea de tiempo de un elemento (como los "Updates" de monday).</summary>
public class Activity : Entity
{
    public string EntityType { get; set; } = "";   // lead | contact | company | deal
    public string EntityId { get; set; } = "";
    public string EntityTitle { get; set; } = "";
    public string Kind { get; set; } = ActivityKinds.Note;
    public string Text { get; set; } = "";
    public string Author { get; set; } = "Sistema";
}
