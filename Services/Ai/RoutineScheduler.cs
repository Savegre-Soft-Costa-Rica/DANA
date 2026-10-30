using Dana.Models;

namespace Dana.Services.Ai;

/// <summary>Cálculo puro de "¿toca ejecutar esta rutina?" (probado por unidad).</summary>
public static class RoutineScheduler
{
    public static TimeZoneInfo Zone(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "America/Costa_Rica" : id); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static bool IsDue(AgentRoutine r, DateTime nowUtc, TimeZoneInfo tz)
    {
        if (!r.Enabled || r.Kind == RoutineKinds.Manual) return false;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var lastLocal = r.LastRunAt is null ? (DateTime?)null : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.LastRunAt.Value, DateTimeKind.Utc), tz);
        switch (r.Kind)
        {
            case RoutineKinds.Hours:
                if (r.WeekdaysOnly && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
                return r.LastRunAt is null || nowUtc - r.LastRunAt.Value >= TimeSpan.FromHours(Math.Max(1, r.EveryHours));
            case RoutineKinds.Daily:
                if (r.WeekdaysOnly && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
                return local.TimeOfDay >= Time(r.TimeOfDay) && (lastLocal is null || lastLocal.Value.Date < local.Date);
            case RoutineKinds.Weekly:
                return (int)local.DayOfWeek == r.Weekday && local.TimeOfDay >= Time(r.TimeOfDay) && (lastLocal is null || lastLocal.Value.Date < local.Date);
            default: return false;
        }
    }

    public static string Describe(AgentRoutine r) => r.Kind switch
    {
        RoutineKinds.Hours => $"cada {Math.Max(1, r.EveryHours)} h" + (r.WeekdaysOnly ? " (lun-vie)" : ""),
        RoutineKinds.Daily => $"diario a las {r.TimeOfDay}" + (r.WeekdaysOnly ? " (lun-vie)" : ""),
        RoutineKinds.Weekly => $"{Days[Math.Clamp(r.Weekday, 0, 6)]} a las {r.TimeOfDay}",
        _ => "manual"
    };

    public static readonly string[] Days = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static TimeSpan Time(string hhmm) => TimeSpan.TryParse(hhmm, out var t) ? t : TimeSpan.FromHours(8);
}
