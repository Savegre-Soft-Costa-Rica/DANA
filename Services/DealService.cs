using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public class DealService
{
    private readonly IRepository<Deal> _deals;
    private readonly PipelineService _pipelines;
    private readonly ActivityService _activities;
    private readonly IRepository<TaskItem> _taskRepo;
    private readonly AutomationService _automations;
    private TaskCleaner _tasks => new(_taskRepo);

    public DealService(IRepository<Deal> deals, PipelineService pipelines, ActivityService activities, IRepository<TaskItem> taskRepo, AutomationService automations)
    {
        _deals = deals;
        _pipelines = pipelines;
        _activities = activities;
        _taskRepo = taskRepo;
        _automations = automations;
    }

    public Task<List<Deal>> GetAllAsync() => _deals.GetAllAsync();
    public Task<Deal?> GetAsync(string? id) => _deals.GetByIdAsync(id);
    public Task<List<Deal>> GetByPipelineAsync(string pipelineId) => _deals.FindAsync(d => d.PipelineId == pipelineId);
    public Task<List<Deal>> GetByContactAsync(string contactId) => _deals.FindAsync(d => d.ContactId == contactId);
    public Task<List<Deal>> GetByCompanyAsync(string companyId) => _deals.FindAsync(d => d.CompanyId == companyId);
    public Task DeleteAsync(string id, string? by = null) => _deals.DeleteAsync(id, by);

    /// <summary>Borrado definitivo (papelera): elimina también actividades y tareas.</summary>
    public async Task PurgeAsync(string id, string? by = null)
    {
        await _deals.PurgeAsync(id, by);
        await _activities.DeleteForAsync("deal", id);
        await _tasks.DeleteForAsync("deal", id);
    }

    public async Task<Deal> SaveAsync(Deal deal, string? author = null)
    {
        deal.UpdatedBy = author ?? deal.UpdatedBy;
        var pipeline = await _pipelines.GetAsync(deal.PipelineId)
                       ?? throw new InvalidOperationException("El embudo no existe.");

        // Si la etapa no pertenece al embudo (p. ej. se cambió el embudo), usar la primera etapa.
        var stage = pipeline.GetStage(deal.StageId) ?? pipeline.FirstStage
                    ?? throw new InvalidOperationException("El embudo no tiene etapas.");
        deal.StageId = stage.Id;

        if (deal.Id is null)
        {
            deal.History.Add(new StageChange { ToStageId = stage.Id, ToStageName = stage.Name });
            ApplyClosure(deal, stage);
            await _deals.InsertAsync(deal);
            await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.Created, $"Creó la negociación en la etapa \"{stage.Name}\"", author);
            await _automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.Created, deal, author));
            return deal;
        }

        var previous = await _deals.GetByIdAsync(deal.Id);
        if (previous is not null && previous.StageId != deal.StageId)
        {
            var prevPipeline = previous.PipelineId == pipeline.Id ? pipeline : await _pipelines.GetAsync(previous.PipelineId);
            deal.History.Add(new StageChange
            {
                FromStageId = previous.StageId,
                FromStageName = prevPipeline?.GetStage(previous.StageId)?.Name,
                ToStageId = stage.Id,
                ToStageName = stage.Name
            });
            ApplyClosure(deal, stage);
            await _deals.UpdateAsync(deal);
            await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.Stage,
                $"Movió la negociación de \"{deal.History[^1].FromStageName}\" a \"{stage.Name}\"", author);
            await _automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.StageChanged, deal, author, new() { ["stageId"] = stage.Id, ["stageName"] = stage.Name }));
            if (previous.AssignedTo != deal.AssignedTo) await _automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.AssigneeChanged, deal, author));
            return deal;
        }

        await _deals.UpdateAsync(deal);
        if (previous is not null && previous.AssignedTo != deal.AssignedTo)
            await _automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.AssigneeChanged, deal, author));
        return deal;
    }

    /// <summary>Mueve la negociación a otra etapa del mismo embudo (usado por el Kanban).</summary>
    public async Task<Deal?> MoveToStageAsync(string dealId, string stageId, string? author = null)
    {
        var deal = await _deals.GetByIdAsync(dealId);
        if (deal is null || deal.StageId == stageId) return deal;
        deal.UpdatedBy = author ?? deal.UpdatedBy;

        var pipeline = await _pipelines.GetAsync(deal.PipelineId);
        var stage = pipeline?.GetStage(stageId);
        if (stage is null) return deal;

        deal.History.Add(new StageChange
        {
            FromStageId = deal.StageId,
            FromStageName = pipeline!.GetStage(deal.StageId)?.Name,
            ToStageId = stage.Id,
            ToStageName = stage.Name
        });
        var fromName = deal.History[^1].FromStageName;
        deal.StageId = stage.Id;
        ApplyClosure(deal, stage);
        await _deals.UpdateAsync(deal);
        await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.Stage, $"Movió la negociación de \"{fromName}\" a \"{stage.Name}\"", author);
        await _automations.TriggerAsync(new AutomationEvent("deals", TriggerTypes.StageChanged, deal, author, new() { ["stageId"] = stage.Id, ["stageName"] = stage.Name }));
        return deal;
    }

    private static void ApplyClosure(Deal deal, Stage stage)
    {
        if (stage.Kind == StageKind.Success)
        {
            deal.ClosedAt = DateTime.UtcNow;
            deal.Probability = 100;
        }
        else if (stage.Kind == StageKind.Failure)
        {
            deal.ClosedAt = DateTime.UtcNow;
            deal.Probability = 0;
        }
        else
        {
            deal.ClosedAt = null;
        }
    }
}
