using Dana.Models;

namespace Dana.Services.Invoicing;

/// <summary>Proveedor de facturación de PRUEBAS: no envía nada a Hacienda. Simula una emisión aceptada
/// al instante (asigna una clave numérica ficticia). Se usa cuando el modo Sandbox está activo.</summary>
public class SandboxInvoiceProvider : IElectronicInvoiceProvider
{
    public string Name => "sandbox";
    public bool IsConfigured => true;

    public Task<InvoiceSubmitResult> SubmitAsync(ElectronicInvoice invoice, CancellationToken ct = default)
    {
        // Clave numérica de 50 dígitos (formato ficticio, solo para pruebas).
        var rnd = Random.Shared;
        var clave = "506" + DateTime.UtcNow.ToString("yyMMddHHmmss");
        while (clave.Length < 50) clave += rnd.Next(0, 10);
        return Task.FromResult(new InvoiceSubmitResult(true, clave[..50], "SBX-" + Guid.NewGuid().ToString("N")[..10], null));
    }

    // En sandbox la factura se acepta de inmediato.
    public Task<InvoiceStatusResult> GetStatusAsync(ElectronicInvoice invoice, CancellationToken ct = default) =>
        Task.FromResult(new InvoiceStatusResult("accepted", null));
}
