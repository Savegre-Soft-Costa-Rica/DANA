using System.Text.RegularExpressions;
using Dana.Data;
using Dana.Models;
using Dana.Services.Integrations;

namespace Dana.Services.Invoicing;

public class InvoiceService
{
    private static readonly Regex Cabys = new(@"^\d{13}$", RegexOptions.Compiled);

    private readonly IRepository<ElectronicInvoice> _invoices;
    private readonly IElectronicInvoiceProvider _real;
    private readonly SandboxInvoiceProvider _sandbox;
    private readonly IntegrationService _integrations;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IRepository<ElectronicInvoice> invoices, IElectronicInvoiceProvider real, SandboxInvoiceProvider sandbox, IntegrationService integrations, ILogger<InvoiceService> logger)
    {
        _invoices = invoices;
        _real = real;
        _sandbox = sandbox;
        _integrations = integrations;
        _logger = logger;
    }

    /// <summary>Proveedor activo: el de pruebas si el modo Sandbox está encendido, si no el real.</summary>
    private IElectronicInvoiceProvider _provider => _integrations.Sandbox ? _sandbox : _real;

    public bool Sandbox => _integrations.Sandbox;
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

    /// <summary>Anula una factura (no genera otra factura; marca el estado como anulada).</summary>
    public async Task<(bool Ok, string? Error)> CancelAsync(string id)
    {
        var invoice = await _invoices.GetByIdAsync(id);
        if (invoice is null) return (false, "Factura no encontrada.");
        if (invoice.Status == "cancelled") return (false, "La factura ya está anulada.");
        invoice.Status = "cancelled";
        await _invoices.UpdateAsync(invoice);
        return (true, null);
    }

    /// <summary>Crea (en borrador) una nota de crédito que revierte una factura aceptada.</summary>
    public async Task<(ElectronicInvoice? Note, string? Error)> CreateCreditNoteAsync(string id)
    {
        var inv = await _invoices.GetByIdAsync(id);
        if (inv is null) return (null, "Factura no encontrada.");
        if (inv.Kind == "credit") return (null, "No se puede acreditar una nota de crédito.");
        if (inv.Status != "accepted") return (null, "Solo se emiten notas de crédito de facturas aceptadas.");
        var note = new ElectronicInvoice
        {
            Kind = "credit", ReferenceId = inv.Id, Provider = _provider.Name,
            ReceiverName = inv.ReceiverName, ReceiverId = inv.ReceiverId, ReceiverEmail = inv.ReceiverEmail,
            Currency = inv.Currency, DealId = inv.DealId, QuoteId = inv.QuoteId, ContactId = inv.ContactId, CompanyId = inv.CompanyId,
            Notes = $"Nota de crédito de la factura {inv.Clave ?? inv.Id}",
            Lines = inv.Lines.Select(l => new InvoiceLine { Cabys = l.Cabys, Description = "NC: " + l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, TaxRate = l.TaxRate }).ToList(),
        };
        await _invoices.InsertAsync(note);
        return (note, null);
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
