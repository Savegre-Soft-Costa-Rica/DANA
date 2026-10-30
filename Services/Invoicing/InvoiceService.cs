using System.Text.RegularExpressions;
using Dana.Data;
using Dana.Models;

namespace Dana.Services.Invoicing;

public class InvoiceService
{
    private static readonly Regex Cabys = new(@"^\d{13}$", RegexOptions.Compiled);

    private readonly IRepository<ElectronicInvoice> _invoices;
    private readonly IElectronicInvoiceProvider _provider;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IRepository<ElectronicInvoice> invoices, IElectronicInvoiceProvider provider, ILogger<InvoiceService> logger)
    {
        _invoices = invoices;
        _provider = provider;
        _logger = logger;
    }

    public bool IsProviderConfigured => _provider.IsConfigured;
    public string ProviderName => _provider.Name;

    public Task<List<ElectronicInvoice>> GetAllAsync() => _invoices.GetAllAsync();
    public Task<ElectronicInvoice?> GetAsync(string? id) => _invoices.GetByIdAsync(id);

    /// <summary>Guarda un borrador. Las facturas ya enviadas no se pueden editar.</summary>
    public async Task<(bool Ok, string? Error)> SaveDraftAsync(ElectronicInvoice invoice)
    {
        if (invoice.Id is not null)
        {
            var current = await _invoices.GetByIdAsync(invoice.Id);
            if (current is not null && current.Status is not ("draft" or "rejected"))
                return (false, "Solo se pueden editar facturas en borrador o rechazadas.");
        }

        invoice.Provider = _provider.Name;
        if (invoice.Id is null) await _invoices.InsertAsync(invoice);
        else await _invoices.UpdateAsync(invoice);
        return (true, null);
    }

    /// <summary>Envía la factura al proveedor. Si falla, queda en borrador con el error registrado.</summary>
    public async Task<(bool Ok, string? Error)> SubmitAsync(string id)
    {
        var invoice = await _invoices.GetByIdAsync(id);
        if (invoice is null) return (false, "Factura no encontrada.");
        if (invoice.Status is not ("draft" or "rejected")) return (false, "Solo se pueden enviar facturas en borrador o rechazadas.");

        var validation = Validate(invoice);
        if (validation is not null) return Fail(invoice, validation);
        if (!_provider.IsConfigured) return Fail(invoice, "No hay proveedor de facturación electrónica configurado.");

        var result = await _provider.SubmitAsync(invoice);
        if (!result.Ok) return Fail(invoice, result.Error ?? "El proveedor rechazó la solicitud.");

        invoice.Status = "submitted";
        invoice.Clave = result.Clave;
        invoice.ExternalId = result.ExternalId;
        invoice.LastError = null;
        invoice.Provider = _provider.Name;
        await _invoices.UpdateAsync(invoice);
        return (true, null);
    }

    /// <summary>Consulta al proveedor el estado de una factura enviada (aceptada o rechazada por Hacienda).</summary>
    public async Task<(bool Ok, string? Error)> RefreshStatusAsync(string id)
    {
        var invoice = await _invoices.GetByIdAsync(id);
        if (invoice is null) return (false, "Factura no encontrada.");
        if (invoice.Status != "submitted") return (false, "Solo se consulta el estado de facturas enviadas.");

        var result = await _provider.GetStatusAsync(invoice);
        if (result.Status is null) return (false, result.Error ?? "No se pudo consultar el estado.");

        invoice.Status = result.Status;
        invoice.LastError = result.Status == "rejected" ? result.Error : null;
        await _invoices.UpdateAsync(invoice);
        return (true, null);
    }

    private (bool Ok, string? Error) Fail(ElectronicInvoice invoice, string error)
    {
        _logger.LogWarning("Factura {Id} no enviada: {Error}", invoice.Id, error);
        invoice.LastError = error;
        return (false, error);
    }

    /// <summary>Requisitos mínimos antes de enviar a Hacienda. Devuelve null si la factura es válida.</summary>
    public static string? Validate(ElectronicInvoice invoice)
    {
        if (invoice.Lines.Count == 0) return "La factura no tiene líneas.";
        if (string.IsNullOrWhiteSpace(invoice.ReceiverName)) return "Falta el nombre del receptor.";
        if (string.IsNullOrWhiteSpace(invoice.ReceiverId)) return "Falta la identificación del receptor (cédula, DIMEX o NITE).";
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            var line = invoice.Lines[i];
            if (!Cabys.IsMatch(line.Cabys)) return $"La línea {i + 1} necesita un código CABYS de 13 dígitos.";
            if (line.Quantity <= 0) return $"La línea {i + 1} debe tener cantidad mayor a cero.";
        }
        return null;
    }
}
