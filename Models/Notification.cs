namespace Dana.Models;

public static class NotificationKinds
{
    public const string Mention = "mention";
    public const string Assigned = "assigned";
    public const string Due = "due";
    public const string System = "system";
}

public class Notification : Entity
{
    public string Recipient { get; set; } = "";   // nombre del usuario
    public string Kind { get; set; } = NotificationKinds.System;
    public string Text { get; set; } = "";
    public string? Link { get; set; }
    public string? Actor { get; set; }
    public bool IsRead { get; set; }
}
