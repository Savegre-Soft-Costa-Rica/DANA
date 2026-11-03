using Dana.Models;

namespace Dana.Services;

/// <summary>Resultado de la priorización de un registro: puntuación 0-100, nivel y siguiente mejor acción.</summary>
public record ScoreResult(int Score, string Tier, string Color, string NextAction);

/// <summary>Priorización heurística de prospectos y negociaciones (el lado "predictivo"/ARM del CRM):
/// combina contactabilidad, estado/etapa, importe, actividad reciente y fecha de cierre para dar una
/// puntuación y recomendar la siguiente acción. Sin ML; es transparente y rápido (sirve para tableros).</summary>
public static class Scoring
{
    public static (string Tier, string Color) Level(int s) =>
        s >= 70 ? ("Alta", "#e2445c") : s >= 40 ? ("Media", "#fdab3d") : ("Baja", "#8b8fa0");

    /// <summary>Siguiente mejor acción recomendada (reutilizable por el modelo ML, que solo cambia la puntuación).</summary>
    public static string NextActionForLead(Lead l) => NextLead(l, Math.Max(0, (DateTime.UtcNow - l.UpdatedAt).TotalDays));
    public static string NextActionForDeal(Deal d) => NextDeal(d, Math.Max(0, (DateTime.UtcNow - d.UpdatedAt).TotalDays), DateTime.UtcNow.Date);

    // ---------- Prospectos ----------
    public static ScoreResult ForLead(Lead l)
    {
        if (l.Status == LeadStatuses.Junk) return new(0, "Descartado", "#9ca3af", "No calificado: archívelo o elimínelo.");
        if (l.Status == LeadStatuses.Converted) return new(100, "Convertido", "#10b981", "Ya convertido.");

        var days = Math.Max(0, (DateTime.UtcNow - l.UpdatedAt).TotalDays);
        var s = 0;
        if (!string.IsNullOrWhiteSpace(l.Email)) s += 12;
        if (!string.IsNullOrWhiteSpace(l.Phone)) s += 12;
        if (!string.IsNullOrWhiteSpace(l.CompanyName)) s += 8;
        s += l.Status switch { LeadStatuses.Qualified => 30, LeadStatuses.InProcess => 18, _ => 8 };
        s += l.Opportunity <= 0 ? 0 : l.Opportunity < 100_000 ? 6 : l.Opportunity < 1_000_000 ? 12 : 18;
        s += days < 2 ? 20 : days < 7 ? 14 : days < 14 ? 8 : days < 30 ? 3 : 0;
        s = Math.Clamp(s, 0, 100);

        var (tier, color) = Level(s);
        return new(s, tier, color, NextLead(l, days));
    }

    private static string NextLead(Lead l, double days)
    {
        if (string.IsNullOrWhiteSpace(l.Email) && string.IsNullOrWhiteSpace(l.Phone)) return "Falta correo o teléfono: consiga datos de contacto.";
        if (days >= 14) return $"Sin actividad hace {(int)days} días: programe un seguimiento.";
        return l.Status switch
        {
            LeadStatuses.Qualified => "Calificado: conviértalo en negociación.",
            LeadStatuses.New => "Nuevo: haga el primer contacto y califíquelo.",
            _ => "En proceso: dé seguimiento y avance a calificado.",
        };
    }

    // ---------- Negociaciones ----------
    public static ScoreResult ForDeal(Deal d, Pipeline? pipe)
    {
        var stage = pipe?.GetStage(d.StageId);
        if (stage?.Kind == StageKind.Success) return new(100, "Ganada", "#10b981", "Cerrada ganada.");
        if (stage?.Kind == StageKind.Failure) return new(0, "Perdida", "#9ca3af", "Cerrada perdida.");

        var days = Math.Max(0, (DateTime.UtcNow - d.UpdatedAt).TotalDays);
        var today = DateTime.UtcNow.Date;
        var s = 0;

        if (pipe is not null)
        {
            var open = pipe.OrderedStages.Where(x => x.Kind == StageKind.Process).ToList();
            var idx = open.FindIndex(x => x.Id == d.StageId);
            s += idx >= 0 && open.Count > 1 ? (int)Math.Round(30.0 * idx / (open.Count - 1)) : 10;
        }
        s += d.Probability / 5;   // 0-20
        s += d.Amount <= 0 ? 0 : d.Amount < 500_000 ? 8 : d.Amount < 5_000_000 ? 14 : 18;
        s += days < 3 ? 20 : days < 10 ? 13 : days < 21 ? 6 : 0;
        if (d.ExpectedCloseDate is { } ecd) { if (ecd.Date < today) s -= 10; else if (ecd.Date <= today.AddDays(7)) s += 8; }
        s = Math.Clamp(s, 0, 100);

        var (tier, color) = Level(s);
        return new(s, tier, color, NextDeal(d, days, today));
    }

    private static string NextDeal(Deal d, double days, DateTime today)
    {
        if (d.ExpectedCloseDate is { } ecd && ecd.Date < today) return "Cierre previsto vencido: actualice la fecha o cierre la negociación.";
        if (days >= 14) return $"Estancada hace {(int)days} días: contacte y muévala de etapa.";
        if (d.Amount <= 0) return "Defina el importe de la negociación.";
        return "Avance a la siguiente etapa o envíe una cotización.";
    }
}
