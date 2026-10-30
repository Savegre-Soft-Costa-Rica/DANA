using Dana.Models;

namespace Dana.Services.Ai;

/// <summary>Cada minuto ejecuta las rutinas proactivas de los agentes que estén pendientes.</summary>
public class AgentRoutineWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AgentRoutineWorker> _logger;
    public AgentRoutineWorker(IServiceProvider sp, ILogger<AgentRoutineWorker> logger) { _sp = sp; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Error en el worker de rutinas de agentes"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var agents = _sp.GetRequiredService<AgentService>();
        try { await agents.DecayMoodsAsync(); } catch { }
        var settings = await _sp.GetRequiredService<SettingsService>().GetAsync();
        if (!agents.AnyConfigured(settings.Ai)) return;
        var tz = RoutineScheduler.Zone(settings.Ai.TimeZoneId);
        foreach (var agent in (await agents.GetAllAsync()).Where(a => a.IsActive))
        {
            foreach (var r in agent.Routines.Where(r => RoutineScheduler.IsDue(r, DateTime.UtcNow, tz)))
            {
                if (agents.IsBusy(agent.Id!)) continue;
                _logger.LogInformation("Ejecutando rutina {Routine} del agente {Agent}", r.Name, agent.Name);
                try { await agents.RunRoutineAsync(agent, r, "programada", ct); }
                catch (Exception ex) { _logger.LogError(ex, "Rutina {Routine} falló", r.Name); }
            }
        }
    }
}
