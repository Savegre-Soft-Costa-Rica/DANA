namespace Dana.Models;

/// <summary>Vinculación de un usuario de Dana con un chat de Telegram y con el agente que atiende ese chat.</summary>
public class TelegramLink : Entity
{
    public string UserName { get; set; } = "";
    public long? ChatId { get; set; }

    /// <summary>Código de vinculación pendiente (de un solo uso, vence en unos minutos).</summary>
    public string? PairingCode { get; set; }
    public DateTime? PairingExpiresAt { get; set; }

    /// <summary>Agente seleccionado para este chat; vacío = el primer agente activo.</summary>
    public string? AgentId { get; set; }
    /// <summary>Conversación activa con ese agente.</summary>
    public string? ConversationId { get; set; }
}
