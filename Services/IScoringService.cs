using Dana.Models;

namespace Dana.Services;

/// <summary>Puntuación/priorización de registros. Dos implementaciones intercambiables por DI:
/// <see cref="HeuristicScoringService"/> (reglas) y <see cref="MlScoringService"/> (modelo ML.NET).</summary>
public interface IScoringService
{
    ScoreResult ScoreLead(Lead lead);
    ScoreResult ScoreDeal(Deal deal, Pipeline? pipeline);
}

/// <summary>Implementación por defecto: reglas transparentes (sin ML). Ver <see cref="Scoring"/>.</summary>
public class HeuristicScoringService : IScoringService
{
    public ScoreResult ScoreLead(Lead lead) => Scoring.ForLead(lead);
    public ScoreResult ScoreDeal(Deal deal, Pipeline? pipeline) => Scoring.ForDeal(deal, pipeline);
}
