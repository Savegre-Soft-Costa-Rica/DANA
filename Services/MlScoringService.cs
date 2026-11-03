using Dana.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace Dana.Services;

/// <summary>
/// Puntuación con ML.NET. Si existen modelos entrenados (App_Data/ml/lead.zip, deal.zip) los usa para
/// predecir la probabilidad de cierre; si no, delega en la heurística. La "siguiente mejor acción" sigue
/// saliendo de las reglas (el modelo solo afina la puntuación).
///
/// Para activarlo: en Program.cs registra IScoringService con MlScoringService en vez de HeuristicScoringService,
/// y entrena los modelos llamando a TrainLeads/TrainDeals con tu historial (prospectos convertidos vs. basura,
/// negociaciones ganadas vs. perdidas). Mientras no haya modelo, funciona igual que la heurística.
/// </summary>
public class MlScoringService : IScoringService
{
    private readonly HeuristicScoringService _heuristic = new();
    private readonly MLContext _ml = new(seed: 1);
    private readonly object _lock = new();
    private readonly string _dir;

    private PredictionEngine<LeadRow, ScorePrediction>? _leadEngine;
    private PredictionEngine<DealRow, ScorePrediction>? _dealEngine;

    public MlScoringService(IHostEnvironment env)
    {
        _dir = Path.Combine(env.ContentRootPath, "App_Data", "ml");
        TryLoad("lead.zip", ref _leadEngine);
        TryLoad("deal.zip", ref _dealEngine);
    }

    public ScoreResult ScoreLead(Lead lead)
    {
        var engine = _leadEngine;
        if (engine is null) return _heuristic.ScoreLead(lead);
        float prob;
        lock (_lock) prob = engine.Predict(LeadRow.From(lead)).Probability;
        return Build(prob, Scoring.NextActionForLead(lead));
    }

    public ScoreResult ScoreDeal(Deal deal, Pipeline? pipeline)
    {
        var engine = _dealEngine;
        if (engine is null) return _heuristic.ScoreDeal(deal, pipeline);
        float prob;
        lock (_lock) prob = engine.Predict(DealRow.From(deal, pipeline)).Probability;
        return Build(prob, Scoring.NextActionForDeal(deal));
    }

    private static ScoreResult Build(float probability, string nextAction)
    {
        var score = Math.Clamp((int)Math.Round(probability * 100), 0, 100);
        var (tier, color) = Scoring.Level(score);
        return new ScoreResult(score, tier, color, nextAction);
    }

    // ---------- entrenamiento (llamar con el historial) ----------
    public void TrainLeads(IEnumerable<LeadRow> rows) => TrainAndSave("lead.zip", rows, ref _leadEngine);
    public void TrainDeals(IEnumerable<DealRow> rows) => TrainAndSave("deal.zip", rows, ref _dealEngine);

    private void TrainAndSave<T>(string file, IEnumerable<T> rows, ref PredictionEngine<T, ScorePrediction>? engine) where T : class, new()
    {
        var data = _ml.Data.LoadFromEnumerable(rows);
        var features = typeof(T).GetProperties().Where(p => p.PropertyType == typeof(float)).Select(p => p.Name).ToArray();
        var pipeline = _ml.Transforms.Concatenate("Features", features)
            .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: "Label", featureColumnName: "Features"));
        var model = pipeline.Fit(data);
        Directory.CreateDirectory(_dir);
        _ml.Model.Save(model, data.Schema, Path.Combine(_dir, file));
        lock (_lock) engine = _ml.Model.CreatePredictionEngine<T, ScorePrediction>(model);
    }

    private void TryLoad<T>(string file, ref PredictionEngine<T, ScorePrediction>? engine) where T : class, new()
    {
        try
        {
            var path = Path.Combine(_dir, file);
            if (!File.Exists(path)) return;
            var model = _ml.Model.Load(path, out DataViewSchema _);
            engine = _ml.Model.CreatePredictionEngine<T, ScorePrediction>(model);
        }
        catch { /* sin modelo válido → se usa la heurística */ }
    }
}

/// <summary>Predicción del clasificador binario (probabilidad de "éxito": conversión / ganar).</summary>
public class ScorePrediction
{
    [ColumnName("Probability")] public float Probability { get; set; }
}

/// <summary>Fila de características de un prospecto para ML.NET. Label = ¿acabó convertido?</summary>
public class LeadRow
{
    public float HasEmail { get; set; }
    public float HasPhone { get; set; }
    public float HasCompany { get; set; }
    public float Status { get; set; }        // 0 nuevo, 1 en proceso, 2 calificado
    public float Opportunity { get; set; }
    public float DaysSinceActivity { get; set; }
    public bool Label { get; set; }

    public static LeadRow From(Lead l) => new()
    {
        HasEmail = string.IsNullOrWhiteSpace(l.Email) ? 0 : 1,
        HasPhone = string.IsNullOrWhiteSpace(l.Phone) ? 0 : 1,
        HasCompany = string.IsNullOrWhiteSpace(l.CompanyName) ? 0 : 1,
        Status = l.Status switch { LeadStatuses.Qualified => 2, LeadStatuses.InProcess => 1, _ => 0 },
        Opportunity = (float)Math.Min(10_000_000, (double)l.Opportunity),
        DaysSinceActivity = (float)Math.Max(0, (DateTime.UtcNow - l.UpdatedAt).TotalDays),
        Label = l.Status == LeadStatuses.Converted,
    };
}

/// <summary>Fila de características de una negociación para ML.NET. Label = ¿se ganó?</summary>
public class DealRow
{
    public float StageProgress { get; set; }
    public float Probability { get; set; }
    public float Amount { get; set; }
    public float DaysSinceActivity { get; set; }
    public float DaysToClose { get; set; }
    public bool Label { get; set; }

    public static DealRow From(Deal d, Pipeline? pipe)
    {
        var stage = pipe?.GetStage(d.StageId);
        float progress = 0;
        if (pipe is not null)
        {
            var open = pipe.OrderedStages.Where(x => x.Kind == StageKind.Process).ToList();
            var idx = open.FindIndex(x => x.Id == d.StageId);
            progress = idx >= 0 && open.Count > 1 ? (float)idx / (open.Count - 1) : 0.3f;
        }
        return new DealRow
        {
            StageProgress = progress,
            Probability = d.Probability,
            Amount = (float)Math.Min(100_000_000, (double)d.Amount),
            DaysSinceActivity = (float)Math.Max(0, (DateTime.UtcNow - d.UpdatedAt).TotalDays),
            DaysToClose = d.ExpectedCloseDate is { } ecd ? (float)(ecd.Date - DateTime.UtcNow.Date).TotalDays : 0,
            Label = stage?.Kind == StageKind.Success,
        };
    }
}
