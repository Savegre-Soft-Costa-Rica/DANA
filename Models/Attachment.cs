namespace Dana.Models;

public class Attachment : Entity
{
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? ActivityId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string StoredName { get; set; } = "";
    public string? UploadedBy { get; set; }

    public bool IsImage => ContentType.StartsWith("image/");
    public string SizeText => Size < 1024 ? $"{Size} B" : Size < 1024 * 1024 ? $"{Size / 1024.0:N0} KB" : $"{Size / 1024.0 / 1024.0:N1} MB";
}
