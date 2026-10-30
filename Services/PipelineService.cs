using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class PipelineService
{
    private readonly IRepository<Pipeline> _pipelines;
    private readonly IRepository<Deal> _deals;

    public PipelineService(IRepository<Pipeline> pipelines, IRepository<Deal> deals)
    {
        _pipelines = pipelines;
        _deals = deals;
    }

    public async Task<List<Pipeline>> GetAllAsync() =>
        (await _pipelines.GetAllAsync()).OrderBy(p => p.Sort).ThenBy(p => p.Name).ToList();

    public Task<Pipeline?> GetAsync(string? id) => _pipelines.GetByIdAsync(id);

    public async Task<Pipeline> GetDefaultAsync()
    {
        var all = await GetAllAsync();
        var def = all.FirstOrDefault(p => p.IsDefault) ?? all.FirstOrDefault();
        if (def is not null) return def;
        return await _pipelines.InsertAsync(Pipeline.CreateDefault());
    }

    public async Task EnsureDefaultAsync()
    {
        if (await _pipelines.CountAsync() == 0)
            await _pipelines.InsertAsync(Pipeline.CreateDefault());
    }

    public async Task SaveAsync(Pipeline pipeline)
    {
        // Normalizar orden de etapas
        var i = 10;
        foreach (var s in pipeline.Stages.OrderBy(s => s.Sort).ToList()) s.Sort = i += 10;

        if (pipeline.IsDefault)
        {
            // Solo un embudo puede ser predeterminado
            await _pipelines.Collection.UpdateManyAsync(
                p => p.Id != pipeline.Id && p.IsDefault,
                Builders<Pipeline>.Update.Set(p => p.IsDefault, false));
        }

        if (pipeline.Id is null) await _pipelines.InsertAsync(pipeline);
        else await _pipelines.UpdateAsync(pipeline);
    }

    /// <summary>Elimina un embudo si no tiene negociaciones.</summary>
    public async Task<(bool Ok, string? Error)> DeleteAsync(string id)
    {
        var count = await _deals.CountAsync(d => d.PipelineId == id);
        if (count > 0) return (false, $"El embudo tiene {count} negociación(es). Muévalas a otro embudo antes de eliminarlo.");
        if (await _pipelines.CountAsync() <= 1) return (false, "Debe existir al menos un embudo.");
        await _pipelines.DeleteAsync(id);
        return (true, null);
    }

    public Task<long> CountDealsInStageAsync(string pipelineId, string stageId) =>
        _deals.CountAsync(d => d.PipelineId == pipelineId && d.StageId == stageId);
}
