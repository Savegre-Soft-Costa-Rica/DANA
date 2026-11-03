using System.Linq.Expressions;
using Dana.Models;
using Dana.Services;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Dana.Data;

public interface IRepository<T> where T : Entity
{
    IMongoCollection<T> Collection { get; }
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(string? id);
    Task<T?> GetByIdIncludingDeletedAsync(string? id);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null);
    Task<T> InsertAsync(T entity);
    Task UpdateAsync(T entity);
    /// <summary>Borrado lógico (papelera).</summary>
    Task DeleteAsync(string id, string? by = null);
    Task RestoreAsync(string id, string? by = null);
    /// <summary>Borrado físico definitivo.</summary>
    Task PurgeAsync(string id, string? by = null);
    Task<List<T>> GetDeletedAsync();
}

public class MongoRepository<T> : IRepository<T> where T : Entity
{
    private static readonly string[] IgnoredFields = ["updatedAt", "updatedBy", "createdAt", "createdBy", "history", "_id", "lastLoginAt", "runCount", "lastRunAt", "receivedCount", "lastReceivedAt", "lastPayload", "submitCount", "lastSubmitAt", "passwordHash", "nextQuoteNumber"];
    private static readonly HashSet<string> Audited = ["leads", "deals", "contacts", "companies", "pipelines", "users", "automations", "apichannels", "leadforms", "emailtemplates", "products", "quotes", "settings", "teams", "documents", "aiagents"];

    private readonly IMongoCollection<AuditEntry> _audit;
    private readonly ChangeNotifier _notifier;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _http;
    private readonly string _name;

    public MongoRepository(MongoContext context, ChangeNotifier notifier, Microsoft.AspNetCore.Http.IHttpContextAccessor http)
    {
        Collection = context.Collection<T>();
        _audit = context.Collection<AuditEntry>();
        _notifier = notifier;
        _http = http;
        _name = MongoContext.CollectionName<T>();
    }

    /// <summary>Bloquea las escrituras si el usuario autenticado es de "Solo lectura" (los trabajos en segundo
    /// plano y las acciones del sistema no tienen HttpContext, así que no se ven afectados).</summary>
    private void GuardWrite()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true && user.IsInRole(Roles.Viewer) && !user.IsInRole(Roles.Admin))
            throw new InvalidOperationException("Su cuenta es de solo lectura: no puede crear, modificar ni eliminar datos.");
    }

    public IMongoCollection<T> Collection { get; }
    private static FilterDefinition<T> NotDeleted => Builders<T>.Filter.Eq(x => x.DeletedAt, null);

    public Task<List<T>> GetAllAsync() => Collection.Find(NotDeleted).SortByDescending(x => x.UpdatedAt).ToListAsync();

    public async Task<T?> GetByIdAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return await Collection.Find(Builders<T>.Filter.And(Builders<T>.Filter.Eq(x => x.Id, id), NotDeleted)).FirstOrDefaultAsync();
    }

    public async Task<T?> GetByIdIncludingDeletedAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        Collection.Find(Builders<T>.Filter.And(Builders<T>.Filter.Where(predicate), NotDeleted)).SortByDescending(x => x.UpdatedAt).ToListAsync();

    public Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null) =>
        Collection.CountDocumentsAsync(predicate is null ? NotDeleted : Builders<T>.Filter.And(Builders<T>.Filter.Where(predicate), NotDeleted));

    public Task<List<T>> GetDeletedAsync() =>
        Collection.Find(Builders<T>.Filter.Ne(x => x.DeletedAt, null)).SortByDescending(x => x.DeletedAt).ToListAsync();

    public async Task<T> InsertAsync(T entity)
    {
        GuardWrite();
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = entity.CreatedAt;
        entity.CreatedBy ??= entity.UpdatedBy;
        await Collection.InsertOneAsync(entity);
        await AuditAsync(entity, "create", entity.CreatedBy, null);
        _notifier.Publish(_name, entity.Id);
        return entity;
    }

    public async Task UpdateAsync(T entity)
    {
        GuardWrite();
        entity.UpdatedAt = DateTime.UtcNow;
        List<AuditChange>? changes = null;
        if (Audited.Contains(_name))
        {
            var previous = await Collection.Find(x => x.Id == entity.Id).FirstOrDefaultAsync();
            if (previous is not null) changes = Diff(previous.ToBsonDocument(), entity.ToBsonDocument());
        }
        await Collection.ReplaceOneAsync(x => x.Id == entity.Id, entity);
        if (changes is { Count: > 0 }) await AuditAsync(entity, "update", entity.UpdatedBy, changes);
        _notifier.Publish(_name, entity.Id);
    }

    public async Task DeleteAsync(string id, string? by = null)
    {
        GuardWrite();
        var entity = await GetByIdIncludingDeletedAsync(id);
        if (entity is null) return;
        await Collection.UpdateOneAsync(x => x.Id == id, Builders<T>.Update.Set(x => x.DeletedAt, DateTime.UtcNow).Set(x => x.DeletedBy, by));
        await AuditAsync(entity, "delete", by, null);
        _notifier.Publish(_name, id);
    }

    public async Task RestoreAsync(string id, string? by = null)
    {
        GuardWrite();
        var entity = await GetByIdIncludingDeletedAsync(id);
        if (entity is null) return;
        await Collection.UpdateOneAsync(x => x.Id == id, Builders<T>.Update.Set(x => x.DeletedAt, null).Set(x => x.DeletedBy, null));
        await AuditAsync(entity, "restore", by, null);
        _notifier.Publish(_name, id);
    }

    public async Task PurgeAsync(string id, string? by = null)
    {
        GuardWrite();
        var entity = await GetByIdIncludingDeletedAsync(id);
        await Collection.DeleteOneAsync(x => x.Id == id);
        if (entity is not null) await AuditAsync(entity, "purge", by, null);
        _notifier.Publish(_name, id);
    }

    private async Task AuditAsync(T entity, string action, string? user, List<AuditChange>? changes)
    {
        if (!Audited.Contains(_name)) return;
        try
        {
            await _audit.InsertOneAsync(new AuditEntry
            {
                User = string.IsNullOrWhiteSpace(user) ? "Sistema" : user, Action = action, Collection = _name, EntityId = entity.Id ?? "",
                Title = TitleOf(entity), Changes = changes ?? new()
            });
        }
        catch { /* la auditoría nunca debe romper la operación */ }
    }

    private static string TitleOf(T e) => e switch
    {
        Lead l => l.Title, Deal d => d.Title, Contact c => c.FullName, Company k => k.Title, Pipeline p => p.Name, User u => u.Name,
        Automation a => a.Name, ApiChannel ch => ch.Name, LeadForm f => f.Name, EmailTemplate t => t.Name, _ => e.GetType().Name
    };

    /// <summary>Diferencias de primer nivel entre dos documentos (valores como texto, truncados).</summary>
    public static List<AuditChange> Diff(BsonDocument before, BsonDocument after)
    {
        var changes = new List<AuditChange>();
        foreach (var name in before.Names.Union(after.Names).Where(n => !IgnoredFields.Contains(n)))
        {
            var a = before.Contains(name) ? before[name] : BsonNull.Value;
            var b = after.Contains(name) ? after[name] : BsonNull.Value;
            if (a == b) continue;
            if (name == "custom" && a.IsBsonDocument && b.IsBsonDocument)
            {
                foreach (var k in a.AsBsonDocument.Names.Union(b.AsBsonDocument.Names))
                {
                    var ca = a.AsBsonDocument.Contains(k) ? a.AsBsonDocument[k] : BsonNull.Value;
                    var cb = b.AsBsonDocument.Contains(k) ? b.AsBsonDocument[k] : BsonNull.Value;
                    if (ca != cb) changes.Add(new AuditChange { Field = "custom." + k, Old = Text(ca), New = Text(cb) });
                }
                continue;
            }
            changes.Add(new AuditChange { Field = name, Old = Text(a), New = Text(b) });
        }
        return changes;
    }

    private static string? Text(BsonValue v)
    {
        if (v.IsBsonNull) return null;
        var s = v.IsString ? v.AsString : v.IsBsonDocument || v.IsBsonArray ? v.ToJson() : v.ToString() ?? "";
        return s.Length > 200 ? s[..200] + "…" : s;
    }
}
