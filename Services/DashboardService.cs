using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public record StageSummary(Stage Stage, int Count, decimal Amount);
public record PipelineSummary(Pipeline Pipeline, List<StageSummary> Stages, int OpenCount, decimal OpenAmount, int WonCount, decimal WonAmount, int LostCount);

public class DashboardData
{
    public long Leads { get; set; }
    public long ActiveLeads { get; set; }
    public long Contacts { get; set; }
    public long Companies { get; set; }
    public long Deals { get; set; }
    public List<PipelineSummary> Pipelines { get; set; } = new();
    public Dictionary<string, int> LeadsByStatus { get; set; } = new();
    public List<Deal> RecentDeals { get; set; } = new();
    public string BaseCurrency { get; set; } = "USD";
    public List<Deal> UpcomingDeals { get; set; } = new();
    public List<Activity> RecentActivity { get; set; } = new();
    public Dictionary<string, Pipeline> PipelineMap { get; set; } = new();
    public decimal OpenAmount => Pipelines.Sum(p => p.OpenAmount);
    public decimal WonAmount => Pipelines.Sum(p => p.WonAmount);
}

public class DashboardService
{
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly IRepository<Deal> _deals;
    private readonly PipelineService _pipelines;
    private readonly ActivityService _activities;
    private readonly SettingsService _settings;

    public DashboardService(IRepository<Lead> leads, IRepository<Contact> contacts, IRepository<Company> companies,
        IRepository<Deal> deals, PipelineService pipelines, ActivityService activities, SettingsService settings)
    {
        _leads = leads;
        _contacts = contacts;
        _companies = companies;
        _deals = deals;
        _pipelines = pipelines;
        _activities = activities;
        _settings = settings;
    }

    public async Task<DashboardData> GetAsync()
    {
        var data = new DashboardData
        {
            Contacts = await _contacts.CountAsync(c => !c.IsArchived),
            Companies = await _companies.CountAsync(c => !c.IsArchived),
        };

        var leads = (await _leads.GetAllAsync()).Where(l => !l.IsArchived).ToList();
        data.Leads = leads.Count;
        data.ActiveLeads = leads.Count(l => !LeadStatuses.Get(l.Status).IsFinal);
        data.LeadsByStatus = leads.GroupBy(l => l.Status).ToDictionary(g => g.Key, g => g.Count());

        var deals = (await _deals.GetAllAsync()).Where(d => !d.IsArchived).ToList();
        data.Deals = deals.Count;
        data.RecentDeals = deals.Take(8).ToList();

        data.BaseCurrency = (await _settings.GetAsync()).BaseCurrency;
        data.RecentActivity = await _activities.GetRecentAsync(12);
        var allPipelines = await _pipelines.GetAllAsync();
        data.PipelineMap = allPipelines.ToDictionary(p => p.Id!);
        data.UpcomingDeals = deals
            .Where(d => d.ExpectedCloseDate is not null && data.PipelineMap.GetValueOrDefault(d.PipelineId)?.GetStage(d.StageId)?.Kind == StageKind.Process)
            .OrderBy(d => d.ExpectedCloseDate).Take(8).ToList();

        foreach (var p in allPipelines)
        {
            var inPipeline = deals.Where(d => d.PipelineId == p.Id).ToList();
            var stages = p.OrderedStages.Select(s =>
            {
                var inStage = inPipeline.Where(d => d.StageId == s.Id).ToList();
                return new StageSummary(s, inStage.Count, inStage.Sum(d => _settings.Current.ToBase(d.Amount, d.Currency)));
            }).ToList();

            var open = stages.Where(s => s.Stage.Kind == StageKind.Process).ToList();
            var won = stages.Where(s => s.Stage.Kind == StageKind.Success).ToList();
            var lost = stages.Where(s => s.Stage.Kind == StageKind.Failure).ToList();

            data.Pipelines.Add(new PipelineSummary(p, stages,
                open.Sum(s => s.Count), open.Sum(s => s.Amount),
                won.Sum(s => s.Count), won.Sum(s => s.Amount),
                lost.Sum(s => s.Count)));
        }

        return data;
    }
}
