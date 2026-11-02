using System.Net.Http.Json;
using System.Text.Json;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services.Integrations;

/// <summary>Enlaces de pago con Tilopay. En modo Sandbox (o sin credenciales) se simulan con una
/// página interna /pagar/{token}; con Tilopay configurado se crea el enlace vía su API.</summary>
public class TilopayService
{
    private readonly IRepository<PaymentLink> _links;
    private readonly IntegrationService _integrations;
    private readonly IHttpClientFactory _http;
    private readonly IServiceProvider _sp;
    private readonly ILogger<TilopayService> _logger;

    public TilopayService(IRepository<PaymentLink> links, IntegrationService integrations, IHttpClientFactory http, IServiceProvider sp, ILogger<TilopayService> logger)
    {
        _links = links; _integrations = integrations; _http = http; _sp = sp; _logger = logger;
    }

    public bool Sandbox => _integrations.Sandbox || !_integrations.IsTilopayConfigured;

    public Task<List<PaymentLink>> GetAllAsync() => _links.GetAllAsync();
    public Task<PaymentLink?> GetAsync(string? id) => _links.GetByIdAsync(id);
    public Task<PaymentLink?> FindByTokenAsync(string token) => _links.Collection.Find(l => l.Token == token && l.DeletedAt == null).FirstOrDefaultAsync();

    /// <summary>Crea un enlace de pago. baseUrl = URL pública del sistema (p. ej. https://miempresa.com/).</summary>
    public async Task<PaymentLink> CreateAsync(PaymentLink link, string baseUrl, string by)
    {
        link.CreatedBy = by;
        baseUrl = baseUrl.TrimEnd('/');
        await _links.InsertAsync(link);

        if (Sandbox)
        {
            link.Provider = "sandbox";
            link.Url = $"{baseUrl}/pagar/{link.Token}";
        }
        else
        {
            link.Provider = "tilopay";
            try
            {
                var (url, extId) = await CreateTilopayAsync(link, baseUrl);
                link.Url = url; link.ExternalId = extId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tilopay: no se pudo crear el enlace");
                link.LastError = "No se pudo crear el enlace en Tilopay: " + ex.Message + ". Puede activar el modo Sandbox para probar.";
            }
        }
        await _links.UpdateAsync(link);
        return link;
    }

    /// <summary>Llamada real a la API de Tilopay (login → crear pago). Ajuste los endpoints a su cuenta.</summary>
    private async Task<(string Url, string? ExternalId)> CreateTilopayAsync(PaymentLink link, string baseUrl)
    {
        var cfg = _integrations.Current.Tilopay;
        var api = (cfg.BaseUrl ?? "https://app.tilopay.com/api/v1").TrimEnd('/');
        var client = _http.CreateClient("integrations");

        // 1) Login
        using var loginRes = await client.PostAsJsonAsync($"{api}/login", new
        {
            apiuser = _integrations.TilopayUser,
            password = _integrations.TilopayPassword,
            key = _integrations.TilopayKey
        });
        loginRes.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString()
                  : loginDoc.RootElement.TryGetProperty("token", out var t2) ? t2.GetString() : null;
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Tilopay no devolvió un token de acceso.");

        // 2) Crear el pago / enlace
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{api}/processPayment");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            key = _integrations.TilopayKey,
            amount = link.Amount,
            currency = link.Currency,
            orderNumber = link.Token,
            description = link.Description,
            billToEmail = link.PayerEmail,
            billToName = link.PayerName,
            returnUrl = $"{baseUrl}/pagar/{link.Token}?ret=1",
            webhook = $"{baseUrl}/api/tilopay/webhook"
        });
        using var payRes = await client.SendAsync(req);
        payRes.EnsureSuccessStatusCode();
        using var payDoc = JsonDocument.Parse(await payRes.Content.ReadAsStringAsync());
        var url = payDoc.RootElement.TryGetProperty("url", out var u) ? u.GetString()
                : payDoc.RootElement.TryGetProperty("paymentURL", out var u2) ? u2.GetString() : null;
        var extId = payDoc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Tilopay no devolvió la URL del pago.");
        return (url, extId);
    }

    /// <summary>Marca el enlace como pagado y aplica efectos (nota en la negociación, cotización aceptada, aviso).</summary>
    public async Task MarkPaidAsync(PaymentLink link, string? payerName, string? payerEmail, string by = "Pago")
    {
        if (link.Status == PaymentStatuses.Paid) return;
        link.Status = PaymentStatuses.Paid;
        link.PaidAt = DateTime.UtcNow;
        link.PayerName ??= payerName;
        link.PayerEmail ??= payerEmail;
        await _links.UpdateAsync(link);

        try
        {
            var activities = _sp.GetRequiredService<ActivityService>();
            if (link.DealId is not null)
            {
                var deal = await _sp.GetRequiredService<IRepository<Deal>>().GetByIdAsync(link.DealId);
                if (deal is not null)
                    await activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.System, $"💳 Pago recibido: {link.Amount:N2} {link.Currency}" + (string.IsNullOrEmpty(payerName) ? "" : $" ({payerName})"), by);
            }
            if (link.QuoteId is not null)
            {
                var quote = await _sp.GetRequiredService<QuoteService>().GetAsync(link.QuoteId);
                if (quote is not null && quote.Status != "accepted")
                    await _sp.GetRequiredService<QuoteService>().SetStatusAsync(quote, "accepted", by);
            }
            // Avisar al responsable de la negociación, si lo hay
            var notify = _sp.GetRequiredService<NotificationService>();
            if (link.DealId is not null)
            {
                var deal = await _sp.GetRequiredService<IRepository<Deal>>().GetByIdAsync(link.DealId);
                if (!string.IsNullOrEmpty(deal?.AssignedTo))
                    await notify.NotifyAsync(deal.AssignedTo, NotificationKinds.System, $"💳 Pago recibido ({link.Amount:N2} {link.Currency}) de {link.Description}", $"deals/{deal.Id}", "Pagos");
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Tilopay: efectos del pago fallaron"); }
    }
}
