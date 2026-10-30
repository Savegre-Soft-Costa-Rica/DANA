using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class AttachmentService
{
    public const long MaxSize = 15 * 1024 * 1024;
    private readonly IRepository<Attachment> _attachments;
    private readonly string _root;

    public AttachmentService(IRepository<Attachment> attachments, IHostEnvironment env)
    {
        _attachments = attachments;
        _root = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(_root);
    }

    public string PathFor(Attachment a) => Path.Combine(_root, a.StoredName);

    public Task<List<Attachment>> GetForAsync(string entityType, string entityId) =>
        _attachments.Collection.Find(a => a.EntityType == entityType && a.EntityId == entityId).SortByDescending(a => a.CreatedAt).ToListAsync();

    public Task<Attachment?> GetAsync(string? id) => _attachments.GetByIdAsync(id);

    public async Task<Attachment> SaveAsync(Stream content, string fileName, string contentType, long size, string entityType, string entityId, string? activityId, string? user)
    {
        var safeName = Path.GetFileName(fileName);
        var stored = $"{Guid.NewGuid():N}{Path.GetExtension(safeName)}";
        await using (var fs = File.Create(Path.Combine(_root, stored)))
            await content.CopyToAsync(fs);
        return await _attachments.InsertAsync(new Attachment
        {
            EntityType = entityType, EntityId = entityId, ActivityId = activityId,
            FileName = safeName, ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
            Size = size, StoredName = stored, UploadedBy = user
        });
    }

    public Task UpdateAsync(Attachment a) => _attachments.UpdateAsync(a);

    public async Task DeleteAsync(Attachment a)
    {
        try { File.Delete(PathFor(a)); } catch { }
        await _attachments.PurgeAsync(a.Id!);
    }

    public async Task DeleteForAsync(string entityType, string entityId)
    {
        foreach (var a in await GetForAsync(entityType, entityId)) await DeleteAsync(a);
    }
}
