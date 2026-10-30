using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class ActivityService
{
    private readonly IRepository<Activity> _activities;

    public ActivityService(IRepository<Activity> activities)
    {
        _activities = activities;
    }

    public Task<Activity> LogAsync(string entityType, string entityId, string entityTitle, string kind, string text, string? author = null) =>
        _activities.InsertAsync(new Activity
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityTitle = entityTitle,
            Kind = kind,
            Text = text,
            Author = string.IsNullOrWhiteSpace(author) ? "Sistema" : author
        });

    public Task<List<Activity>> GetForAsync(string entityType, string entityId) =>
        _activities.Collection.Find(a => a.EntityType == entityType && a.EntityId == entityId)
            .SortByDescending(a => a.CreatedAt).ToListAsync();

    public Task<List<Activity>> GetRecentAsync(int limit = 20) =>
        _activities.Collection.Find(FilterDefinition<Activity>.Empty).SortByDescending(a => a.CreatedAt).Limit(limit).ToListAsync();

    public Task DeleteAsync(string id) => _activities.PurgeAsync(id);

    public Task DeleteForAsync(string entityType, string entityId) =>
        _activities.Collection.DeleteManyAsync(a => a.EntityType == entityType && a.EntityId == entityId);
}
