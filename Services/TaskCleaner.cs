using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>Borrado de actividades planificadas de un elemento (evita dependencias circulares entre servicios).</summary>
public class TaskCleaner
{
    private readonly IRepository<TaskItem> _tasks;
    public TaskCleaner(IRepository<TaskItem> tasks) { _tasks = tasks; }
    public Task DeleteForAsync(string entityType, string entityId) => _tasks.Collection.DeleteManyAsync(t => t.EntityType == entityType && t.EntityId == entityId);
}
