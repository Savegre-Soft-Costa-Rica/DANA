namespace Dana.Models;

/// <summary>Registro de una acción (herramienta de escritura) ejecutada por un agente, para auditar
/// qué crearon, actualizaron, enviaron o cobraron los agentes.</summary>
public class AgentAction : Entity
{
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    /// <summary>Nombre de la herramienta (p. ej. crear, enviar_correo, emitir_factura).</summary>
    public string Tool { get; set; } = "";
    public string ToolTitle { get; set; } = "";
    /// <summary>Resumen legible del resultado.</summary>
    public string Summary { get; set; } = "";
    /// <summary>Contexto: usuario/chat/rutina/automatización que originó la acción.</summary>
    public string? Source { get; set; }
    public bool Success { get; set; } = true;
}
