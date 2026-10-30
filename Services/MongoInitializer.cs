using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>Al arrancar: crea índices y garantiza que exista un embudo predeterminado.</summary>
public class MongoInitializer : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MongoInitializer> _logger;

    public MongoInitializer(IServiceProvider services, ILogger<MongoInitializer> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var deals = _services.GetRequiredService<IRepository<Deal>>().Collection;
            await deals.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Deal>(Builders<Deal>.IndexKeys.Ascending(d => d.PipelineId).Ascending(d => d.StageId)),
                new CreateIndexModel<Deal>(Builders<Deal>.IndexKeys.Ascending(d => d.ContactId)),
                new CreateIndexModel<Deal>(Builders<Deal>.IndexKeys.Ascending(d => d.CompanyId)),
            ], ct);

            var contacts = _services.GetRequiredService<IRepository<Contact>>().Collection;
            await contacts.Indexes.CreateOneAsync(new CreateIndexModel<Contact>(Builders<Contact>.IndexKeys.Ascending(c => c.CompanyId)), cancellationToken: ct);

            var leads = _services.GetRequiredService<IRepository<Lead>>().Collection;
            await leads.Indexes.CreateOneAsync(new CreateIndexModel<Lead>(Builders<Lead>.IndexKeys.Ascending(l => l.Status)), cancellationToken: ct);

            var users = _services.GetRequiredService<IRepository<User>>().Collection;
            await users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true }), cancellationToken: ct);

            var activities = _services.GetRequiredService<IRepository<Activity>>().Collection;
            await activities.Indexes.CreateOneAsync(new CreateIndexModel<Activity>(
                Builders<Activity>.IndexKeys.Ascending(a => a.EntityType).Ascending(a => a.EntityId).Descending(a => a.CreatedAt)), cancellationToken: ct);

            var tasks = _services.GetRequiredService<IRepository<TaskItem>>().Collection;
            await tasks.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<TaskItem>(Builders<TaskItem>.IndexKeys.Ascending(t => t.AssignedTo).Ascending(t => t.IsDone).Ascending(t => t.DueAt)),
                new CreateIndexModel<TaskItem>(Builders<TaskItem>.IndexKeys.Ascending(t => t.EntityType).Ascending(t => t.EntityId)),
            ], ct);
            var notifications = _services.GetRequiredService<IRepository<Notification>>().Collection;
            await notifications.Indexes.CreateOneAsync(new CreateIndexModel<Notification>(Builders<Notification>.IndexKeys.Ascending(n => n.Recipient).Ascending(n => n.IsRead).Descending(n => n.CreatedAt)), cancellationToken: ct);
            var attachments = _services.GetRequiredService<IRepository<Attachment>>().Collection;
            await attachments.Indexes.CreateOneAsync(new CreateIndexModel<Attachment>(Builders<Attachment>.IndexKeys.Ascending(a => a.EntityType).Ascending(a => a.EntityId)), cancellationToken: ct);

            var audit = _services.GetRequiredService<IRepository<AuditEntry>>().Collection;
            await audit.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Ascending(a => a.Collection).Ascending(a => a.EntityId).Descending(a => a.CreatedAt)),
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Descending(a => a.CreatedAt)),
            ], ct);
            await _services.GetRequiredService<SettingsService>().GetAsync();

            await _services.GetRequiredService<PipelineService>().EnsureDefaultAsync();
            _logger.LogInformation("MongoDB listo: índices y embudo predeterminado verificados.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo inicializar MongoDB. Verifique la cadena de conexión (Mongo:ConnectionString).");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
