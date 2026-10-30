using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public static class TriggerTypes
{
    public const string Created = "created";
    public const string StageChanged = "stage_changed";     // negociaciones
    public const string StatusChanged = "status_changed";   // prospectos
    public const string AssigneeChanged = "assignee_changed";
    public const string OverdueClose = "overdue_close";     // negociaciones (programado)
    public const string TaskCompleted = "task_completed";

    public static readonly (string Id, string Label, string[] Boards)[] All =
    [
        (Created, "Cuando se crea un elemento", ["leads", "deals", "contacts", "companies"]),
        (StageChanged, "Cuando una negociación cambia a una etapa", ["deals"]),
        (StatusChanged, "Cuando un prospecto cambia a un estado", ["leads"]),
        (AssigneeChanged, "Cuando cambia el responsable", ["leads", "deals", "contacts", "companies"]),
        (OverdueClose, "Cuando vence la fecha de cierre prevista", ["deals"]),
        (TaskCompleted, "Cuando se completa una actividad del elemento", ["leads", "deals", "contacts", "companies"]),
    ];
}

public static class ActionTypes
{
    public const string Notify = "notify";
    public const string Assign = "assign";
    public const string SetStage = "set_stage";
    public const string SetStatus = "set_status";
    public const string CreateTask = "create_task";
    public const string SendEmail = "send_email";
    public const string Webhook = "webhook";
    public const string Archive = "archive";
    public const string AskAgent = "ask_agent";

    public static readonly (string Id, string Label, string[] Boards)[] All =
    [
        (AskAgent, "Consultar a un agente de IA", ["leads", "deals", "contacts", "companies"]),
        (Notify, "Notificar a una persona", ["leads", "deals", "contacts", "companies"]),
        (Assign, "Asignar responsable", ["leads", "deals", "contacts", "companies"]),
        (SetStage, "Mover a una etapa", ["deals"]),
        (SetStatus, "Cambiar el estado", ["leads"]),
        (CreateTask, "Crear una actividad", ["leads", "deals", "contacts", "companies"]),
        (SendEmail, "Enviar correo con plantilla", ["leads", "deals", "contacts", "companies"]),
        (Webhook, "Llamar a un webhook (POST)", ["leads", "deals", "contacts", "companies"]),
        (Archive, "Archivar el elemento", ["leads", "deals", "contacts", "companies"]),
    ];
}

public class AutomationStep
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new();
    public string Get(string key, string fallback = "") => Params.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}

/// <summary>Receta "cuando X → entonces Y" sobre un tablero (como las automatizaciones de monday).</summary>
public class Automation : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string BoardKey { get; set; } = "deals";
    public bool IsEnabled { get; set; } = true;
    public AutomationStep Trigger { get; set; } = new() { Type = TriggerTypes.Created };
    public List<AutomationStep> Actions { get; set; } = new();
    public int RunCount { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Evento que puede disparar automatizaciones.</summary>
public record AutomationEvent(string BoardKey, string Type, Entity Item, string? Actor, Dictionary<string, string>? Data = null);
