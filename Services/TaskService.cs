using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class TaskService
{
    private readonly IRepository<TaskItem> _tasks;
    private readonly ActivityService _activities;
    private readonly NotificationService _notifications;
    private readonly IServiceProvider _sp;

    public TaskService(IRepository<TaskItem> tasks, ActivityService activities, NotificationService notifications, IServiceProvider sp)
    {
        _tasks = tasks;
        _activities = activities;
        _notifications = notifications;
        _sp = sp;
    }

    public Task<List<TaskItem>> GetAllAsync() => _tasks.GetAllAsync();
    public Task<TaskItem?> GetAsync(string? id) => _tasks.GetByIdAsync(id);

    public Task<List<TaskItem>> GetForEntityAsync(string entityType, string entityId) =>
        _tasks.Collection.Find(t => t.EntityType == entityType && t.EntityId == entityId && t.DeletedAt == null)
            .SortBy(t => t.IsDone).ThenBy(t => t.DueAt).ToListAsync();

    public Task<List<TaskItem>> GetForUserAsync(string? user, bool includeDone) =>
        _tasks.Collection.Find(t => (user == null || t.AssignedTo == user) && (includeDone || !t.IsDone) && t.DeletedAt == null)
            .SortBy(t => t.DueAt).ToListAsync();

    public Task<long> CountPendingAsync(string user) => _tasks.CountAsync(t => t.AssignedTo == user && !t.IsDone);

    public async Task<TaskItem> SaveAsync(TaskItem task, string? author)
    {
        var isNew = task.Id is null;
        var previous = isNew ? null : await _tasks.GetByIdAsync(task.Id);
        if (isNew)
        {
            task.CreatedBy = author;
            await _tasks.InsertAsync(task);
        }
        else await _tasks.UpdateAsync(task);

        var kind = TaskKinds.Get(task.Kind);
        if (task.EntityType is not null && task.EntityId is not null)
        {
            if (isNew)
                await _activities.LogAsync(task.EntityType, task.EntityId, task.EntityTitle ?? "", ActivityKinds.Task,
                    $"{kind.Icon} Programó {kind.Name.ToLowerInvariant()}: {task.Title}{(task.DueAt is null ? "" : $" · {task.DueAt.Value.ToLocalTime():g}")}", author);
        }

        if (!string.IsNullOrEmpty(task.AssignedTo) && task.AssignedTo != author && (isNew || previous?.AssignedTo != task.AssignedTo))
            await _notifications.NotifyAsync(task.AssignedTo, NotificationKinds.Assigned,
                $"te asignó {kind.Name.ToLowerInvariant()}: {task.Title}", "mi-trabajo", author);

        return task;
    }

    public async Task<TaskItem?> ToggleDoneAsync(string id, string? author)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null) return null;
        task.IsDone = !task.IsDone;
        task.CompletedAt = task.IsDone ? DateTime.UtcNow : null;
        await _tasks.UpdateAsync(task);
        if (task.IsDone && task.EntityType is not null && task.EntityId is not null)
        {
            var kind = TaskKinds.Get(task.Kind);
            await _activities.LogAsync(task.EntityType, task.EntityId, task.EntityTitle ?? "", ActivityKinds.Task, $"✓ Completó {kind.Name.ToLowerInvariant()}: {task.Title}", author);
            var entity = await CrmEntities.LoadAsync(_sp, task.EntityType, task.EntityId);
            if (entity is not null)
                await _sp.GetRequiredService<AutomationService>().TriggerAsync(new AutomationEvent(CrmEntities.BoardOfType(task.EntityType), TriggerTypes.TaskCompleted, entity, author, new() { ["taskTitle"] = task.Title, ["taskKind"] = task.Kind }));
        }
        return task;
    }

    /// <summary>Pospone (aplaza) una actividad a una nueva fecha/hora. Reinicia los avisos de recordatorio y
    /// vencimiento para que puedan volver a dispararse, y lo anota en la línea de tiempo del elemento ligado.</summary>
    public async Task<TaskItem?> PostponeAsync(string id, DateTime newDueUtc, string? author)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null || task.IsDone) return task;
        task.DueAt = newDueUtc;
        task.ReminderSentAt = null;
        task.OverdueNotifiedAt = null;
        await _tasks.UpdateAsync(task);
        if (task.EntityType is not null && task.EntityId is not null)
        {
            var kind = TaskKinds.Get(task.Kind);
            await _activities.LogAsync(task.EntityType, task.EntityId, task.EntityTitle ?? "", ActivityKinds.Task,
                $"⏰ Pospuso {kind.Name.ToLowerInvariant()}: {task.Title} → {newDueUtc.ToLocalTime():g}", author);
        }
        return task;
    }

    public Task DeleteAsync(string id) => _tasks.PurgeAsync(id);
    public Task DeleteForAsync(string entityType, string entityId) => _tasks.Collection.DeleteManyAsync(t => t.EntityType == entityType && t.EntityId == entityId);
}
