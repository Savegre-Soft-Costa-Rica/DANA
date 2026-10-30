namespace Dana.Services.Telegram;

/// <summary>Mantiene el bot de Telegram escuchando mensajes. No hace nada si no hay token configurado.</summary>
public class TelegramPollingWorker : BackgroundService
{
    private readonly TelegramService _telegram;
    private readonly ILogger<TelegramPollingWorker> _logger;

    public TelegramPollingWorker(TelegramService telegram, ILogger<TelegramPollingWorker> logger)
    {
        _telegram = telegram;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_telegram.IsConfigured)
        {
            _logger.LogInformation("Telegram no configurado (Telegram:BotToken); el bot no se inicia.");
            return;
        }

        _logger.LogInformation("Bot de Telegram escuchando mensajes.");
        await _telegram.RunPollingAsync(ct);
    }
}
