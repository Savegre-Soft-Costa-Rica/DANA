using Dana.Models;

namespace Dana.Services.Invoicing;

public record InvoiceSubmitResult(bool Ok, string? Clave, string? ExternalId, string? Error);

public record InvoiceStatusResult(string? Status, string? Error);

/// <summary>
/// Adaptador a un proveedor de facturación electrónica autorizado por Hacienda (Senda, GTI, etc.).
/// Cada proveedor implementa esta interfaz; el resto del CRM no depende de su API.
/// </summary>
public interface IElectronicInvoiceProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<InvoiceSubmitResult> SubmitAsync(ElectronicInvoice invoice, CancellationToken ct = default);
    Task<InvoiceStatusResult> GetStatusAsync(ElectronicInvoice invoice, CancellationToken ct = default);
}

/// <summary>Proveedor por defecto mientras no haya un adaptador configurado. Permite crear borradores sin emitir.</summary>
public class NotConfiguredInvoiceProvider : IElectronicInvoiceProvider
{
    private const string Message = "No hay proveedor de facturación electrónica configurado.";

    public string Name => "ninguno";
    public bool IsConfigured => false;

    public Task<InvoiceSubmitResult> SubmitAsync(ElectronicInvoice invoice, CancellationToken ct = default) =>
        Task.FromResult(new InvoiceSubmitResult(false, null, null, Message));

    public Task<InvoiceStatusResult> GetStatusAsync(ElectronicInvoice invoice, CancellationToken ct = default) =>
        Task.FromResult(new InvoiceStatusResult(null, Message));
}
