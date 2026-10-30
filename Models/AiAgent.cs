namespace Dana.Models;

public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public static readonly (string Id, string Name)[] All = [(Anthropic, "Claude (Anthropic)"), (OpenAi, "ChatGPT (OpenAI)")];
}

/// <summary>Claves y modelos por defecto de los proveedores de IA. Las claves se guardan cifradas (Data Protection).</summary>
public class AiSettings
{
    public string? AnthropicKeyProtected { get; set; }
    public string? OpenAiKeyProtected { get; set; }
    /// <summary>Clave opcional de Brave Search API para la herramienta de búsqueda web (si falta se usa la búsqueda de OpenAI o Anthropic).</summary>
    public string? BraveKeyProtected { get; set; }
    public string AnthropicModel { get; set; } = "claude-opus-5-5";
    public string OpenAiModel { get; set; } = "gpt-4o";
    public string DefaultProvider { get; set; } = AiProviders.Anthropic;
    /// <summary>Máximo de llamadas a herramientas por respuesta.</summary>
    public int MaxToolCalls { get; set; } = 12;
    /// <summary>Zona horaria para las rutinas programadas (IANA, p. ej. America/Costa_Rica).</summary>
    public string TimeZoneId { get; set; } = "America/Costa_Rica";
}

public static class RoutineKinds
{
    public const string Hours = "hours";     // cada N horas
    public const string Daily = "daily";     // todos los días a una hora
    public const string Weekly = "weekly";   // un día de la semana a una hora
    public const string Manual = "manual";   // solo con "Ejecutar ahora"
    public static readonly (string Id, string Label)[] All = [(Hours, "Cada N horas"), (Daily, "Todos los días"), (Weekly, "Una vez por semana"), (Manual, "Solo manual")];
}

/// <summary>Rutina proactiva: el agente ejecuta un encargo por sí solo según un horario y entrega el resultado.</summary>
public class AgentRoutine
{
    public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Kind { get; set; } = RoutineKinds.Daily;
    public int EveryHours { get; set; } = 4;
    /// <summary>Hora local HH:mm para daily/weekly.</summary>
    public string TimeOfDay { get; set; } = "08:00";
    /// <summary>0 = domingo … 6 = sábado (weekly).</summary>
    public int Weekday { get; set; } = 1;
    public bool WeekdaysOnly { get; set; } = true;
    /// <summary>Usuarios destinatarios (nombres). Vacío = todos los usuarios activos.</summary>
    public List<string> Recipients { get; set; } = new();
    /// <summary>Enviar también el informe por correo a los destinatarios (requiere SMTP).</summary>
    public bool SendEmail { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastOutput { get; set; }
    public string? LastError { get; set; }
    public int RunCount { get; set; }
}

/// <summary>Agente (asistente de IA) con un rol dentro de la organización. Vive en la oficina virtual.</summary>
public class AiAgent : Entity
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Asistente";
    public string? Description { get; set; }
    /// <summary>anthropic | openai | "" (usar el proveedor por defecto)</summary>
    public string Provider { get; set; } = "";
    /// <summary>Modelo concreto; vacío = modelo por defecto del proveedor.</summary>
    public string? Model { get; set; }
    public string Instructions { get; set; } = "";
    /// <summary>Nombres de herramientas habilitadas (ver AgentTools.Catalog).</summary>
    public List<string> Tools { get; set; } = new();
    public bool AllowWrites { get; set; }
    /// <summary>low | medium | high | "" (predeterminado del modelo). Solo Claude.</summary>
    public string Effort { get; set; } = "";
    public string Color { get; set; } = "#0073ea";
    /// <summary>Estilo del avatar (0-7).</summary>
    public int Avatar { get; set; }
    /// <summary>Posición del escritorio en la oficina (casillas).</summary>
    public int X { get; set; } = 4;
    public int Y { get; set; } = 4;
    public bool IsActive { get; set; } = true;
    public int Sort { get; set; }
    public long MessageCount { get; set; }
    public List<AgentRoutine> Routines { get; set; } = new();
    /// <summary>Puede consultar a otros agentes (herramienta consultar_agente).</summary>
    public bool CanCollaborate { get; set; } = true;
    /// <summary>Ids de agentes a los que puede consultar; vacío = todos.</summary>
    public List<string> Collaborators { get; set; } = new();
    /// <summary>Estado de ánimo 0-100 (70 = neutral-contento). Sube con aciertos y feedback positivo, baja con errores.</summary>
    public int Mood { get; set; } = 70;
    public string? MoodNote { get; set; }
    public DateTime MoodUpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Plantilla de la que se creó (informativo).</summary>
    public string? Template { get; set; }
    /// <summary>Jefe del equipo de agentes (solo uno): coordina, puede consultar a todos y recibe las escalaciones.</summary>
    public bool IsBoss { get; set; }
}

public static class AgentMood
{
    public static (string Emoji, string Label, string Color) Describe(int mood) => mood switch
    {
        >= 85 => ("🤩", "Entusiasmado", "#00c875"),
        >= 65 => ("😊", "Contento", "#9cd326"),
        >= 45 => ("😐", "Neutral", "#fdab3d"),
        >= 25 => ("😓", "Cansado", "#ff642e"),
        _ => ("😤", "Frustrado", "#e2445c"),
    };
    public static int Clamp(int v) => Math.Clamp(v, 0, 100);
}

/// <summary>Acción que requiere confirmación del usuario antes de ejecutarse (correo, mensaje…).</summary>
public class PendingAction
{
    public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string Tool { get; set; } = "";
    public string InputJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    /// <summary>pending | done | cancelled | failed</summary>
    public string Status { get; set; } = "pending";
    public string? Result { get; set; }
}

/// <summary>Hecho o preferencia que el agente recuerda entre conversaciones.</summary>
public class AgentMemory : Entity
{
    public string AgentId { get; set; } = "";
    /// <summary>Usuario al que pertenece el recuerdo; null = compartido por todo el equipo.</summary>
    public string? UserName { get; set; }
    public string Text { get; set; } = "";
    public string? Source { get; set; }
    public int Uses { get; set; }
}

public class AgentMessage
{
    public string Role { get; set; } = "user";   // user | assistant | tool | error
    public string Text { get; set; } = "";
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    /// <summary>Origen de un mensaje proactivo (nombre de la rutina o automatización); null = conversación normal.</summary>
    public string? Source { get; set; }
    /// <summary>Valoración del usuario: 1 = 👍, -1 = 👎, 0 = sin valorar.</summary>
    public int Feedback { get; set; }
    /// <summary>Acción pendiente de confirmación (mensajes con Role = "action").</summary>
    public PendingAction? Action { get; set; }
}

/// <summary>Conversación de un usuario con un agente.</summary>
public class AgentConversation : Entity
{
    public string AgentId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "Conversación";
    public List<AgentMessage> Messages { get; set; } = new();
}
