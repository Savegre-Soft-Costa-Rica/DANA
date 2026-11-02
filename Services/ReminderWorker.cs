using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>
/// Tarea en segundo plano (cada minuto): recordatorios de actividades próximas, avisos de actividades vencidas
/// y disparo de "fecha de cierre vencida" en negociaciones.
/// </summary>
public class ReminderWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ReminderWorker> _logger;

    public ReminderWorker(IServiceProvider sp, ILogger<ReminderWorker> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct); // dejar arrancar la app
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Error en el worker de recordatorios"); }
            try { if (DateTime.UtcNow.Hour == 14 && DateTime.UtcNow.Minute == 0) { using var scope = _sp.CreateScope(); await scope.ServiceProvider.GetRequiredService<DocumentService>().SendSignatureRemindersAsync(scope.ServiceProvider.GetRequiredService<EmailService>(), scope.ServiceProvider.GetRequiredService<IConfiguration>()["PublicBaseUrl"] ?? "http://localhost:5025"); } }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Error en el worker de recordatorios"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tasks = _sp.GetRequiredService<IRepository<TaskItem>>();
        var notifications = _sp.GetRequiredService<NotificationService>();

        // 1. Actividades que empiezan en los próximos 15 minutos
        var soon = await tasks.Collection.Find(t => !t.IsDone && t.AssignedTo != null && t.ReminderSentAt == null && t.DueAt != null && t.DueAt <= now.AddMinutes(15) && t.DueAt > now.AddMinutes(-5)).ToListAsync(ct);
        foreach (var t in soon)
        {
            var k = TaskKinds.Get(t.Kind);
            await notifications.NotifyAsync(t.AssignedTo!, NotificationKinds.Due, $"⏰ {k.Name} en breve: {t.Title} ({t.DueAt!.Value.ToLocalTime():HH:mm})", t.EntityId is null ? "mi-trabajo" : CrmEntities.LinkOf(t.EntityType!, t.EntityId), "Recordatorio");
            t.ReminderSentAt = now;
            await tasks.UpdateAsync(t);
        }

        // 2. Actividades vencidas (aviso único)
        var overdue = await tasks.Collection.Find(t => !t.IsDone && t.AssignedTo != null && t.OverdueNotifiedAt == null && t.DueAt != null && t.DueAt < now.AddMinutes(-5)).ToListAsync(ct);
        foreach (var t in overdue)
        {
            var k = TaskKinds.Get(t.Kind);
            await notifications.NotifyAsync(t.AssignedTo!, NotificationKinds.Due, $"⚠ {k.Name} vencida: {t.Title}", "mi-trabajo", "Recordatorio");
            t.OverdueNotifiedAt = now;
            await tasks.UpdateAsync(t);
        }

        // 3. Negociaciones con fecha de cierre vencida y aún abiertas
        var deals = _sp.GetRequiredService<IRepository<Deal>>();
        var today = DateTime.UtcNow.Date;
        var late = await deals.Collection.Find(d => !d.IsArchived && d.ClosedAt == null && d.OverdueTriggeredAt == null && d.ExpectedCloseDate != null && d.ExpectedCloseDate < today).ToListAsync(ct);
        var automations = _sp.GetRequiredService<AutomationService>();
        foreach (var d in late)
        {
            d.OverdueTriggeredAt = now;
            await deals.UpdateAsync(d);
            if (!string.IsNullOrEmpty(d.AssignedTo))
                await notifications.NotifyAsync(d.AssignedTo, NotificationKinds.Due, $"📅 Cierre previsto vencido: {d.Title}", $"deals/{d.Id}", "Recordatorio");
            await automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.OverdueClose, d, "Recordatorio"));
        }

        // 4. Inactividad: prospectos/negociaciones sin actividad en N días (solo si hay automatizaciones "inactive")
        var inactiveFired = 0;
        var rules = (await automations.GetAllAsync()).Where(a => a.IsEnabled && a.Trigger.Type == TriggerTypes.Inactive).ToList();
        if (rules.Count > 0)
        {
            // Prospectos
            var leadDays = rules.Where(a => a.BoardKey == "leads").Select(a => int.TryParse(a.Trigger.Get("days"), out var d) ? d : 7).DefaultIfEmpty().ToList();
            if (leadDays.Any(d => d > 0))
            {
                var cut = now.AddDays(-leadDays.Where(d => d > 0).Min());
                var leadsRepo = _sp.GetRequiredService<IRepository<Lead>>();
                var stale = (await leadsRepo.Collection.Find(l => l.DeletedAt == null && !l.IsArchived && l.Status != LeadStatuses.Converted && l.Status != LeadStatuses.Junk && l.UpdatedAt < cut).Limit(200).ToListAsync(ct))
                    .Where(l => l.InactiveTriggeredAt == null || l.InactiveTriggeredAt < l.UpdatedAt).ToList();
                foreach (var l in stale)
                {
                    l.InactiveTriggeredAt = now; await leadsRepo.UpdateAsync(l);
                    await automations.TriggerAsync(new AutomationEvent("leads", TriggerTypes.Inactive, l, "Recordatorio", new() { ["days"] = ((int)(now - l.UpdatedAt).TotalDays).ToString() }));
                    inactiveFired++;
                }
            }
            // Negociaciones
            var dealDays = rules.Where(a => a.BoardKey == "deals").Select(a => int.TryParse(a.Trigger.Get("days"), out var d) ? d : 7).DefaultIfEmpty().ToList();
            if (dealDays.Any(d => d > 0))
            {
                var cut = now.AddDays(-dealDays.Where(d => d > 0).Min());
                var stale = (await deals.Collection.Find(d => d.DeletedAt == null && !d.IsArchived && d.ClosedAt == null && d.UpdatedAt < cut).Limit(200).ToListAsync(ct))
                    .Where(d => d.InactiveTriggeredAt == null || d.InactiveTriggeredAt < d.UpdatedAt).ToList();
                foreach (var d in stale)
                {
                    d.InactiveTriggeredAt = now; await deals.UpdateAsync(d);
                    await automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.Inactive, d, "Recordatorio", new() { ["days"] = ((int)(now - d.UpdatedAt).TotalDays).ToString() }));
                    inactiveFired++;
                }
            }
        }

        if (soon.Count + overdue.Count + late.Count + inactiveFired > 0)
            _logger.LogInformation("Recordatorios: {Soon} próximas, {Overdue} vencidas, {Late} cierres vencidos, {Inactive} inactivos", soon.Count, overdue.Count, late.Count, inactiveFired);
    }
}
