using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public record FormSubmission(string? Name, string? LastName, string? Email, string? Phone, string? Company, string? Message, string? Website);
public record FormSubmitResult(bool Ok, int Status, string? Error, Lead? Lead);

/// <summary>Formularios públicos: localizar por slug y procesar envíos (página HTML o API JSON).</summary>
public class LeadFormService
{
    private readonly IRepository<LeadForm> _forms;
    private readonly LeadService _leads;
    private readonly NotificationService _notifications;

    public LeadFormService(IRepository<LeadForm> forms, LeadService leads, NotificationService notifications)
    {
        _forms = forms;
        _leads = leads;
        _notifications = notifications;
    }

    public Task<LeadForm?> FindBySlugAsync(string slug) => _forms.Collection.Find(f => f.Slug == slug && f.DeletedAt == null).FirstOrDefaultAsync();

    public async Task<FormSubmitResult> SubmitAsync(LeadForm form, FormSubmission input)
    {
        if (!form.IsEnabled) return new(false, 403, "El formulario está deshabilitado.", null);
        if (!string.IsNullOrEmpty(input.Website)) return new(true, 201, null, null); // honeypot: se responde OK sin crear nada
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return new(false, 400, "Indique su nombre.", null);
        if (form.RequirePhoneOrEmail && string.IsNullOrWhiteSpace(input.Email) && string.IsNullOrWhiteSpace(input.Phone))
            return new(false, 400, "Indique un correo o un teléfono para poder contactarle.", null);

        var lead = new Lead
        {
            Title = $"{form.Name}: {name} {input.LastName?.Trim()}".Trim(),
            Name = name, LastName = input.LastName?.Trim(), Email = input.Email?.Trim(), Phone = input.Phone?.Trim(),
            CompanyName = input.Company?.Trim(), Comments = input.Message?.Trim(), Source = form.Source, AssignedTo = form.AssignTo
        };
        await _leads.SaveAsync(lead, $"Formulario · {form.Name}");
        if (!string.IsNullOrEmpty(form.AssignTo))
            await _notifications.NotifyAsync(form.AssignTo, NotificationKinds.System, $"nuevo prospecto desde el formulario {form.Name}: {lead.Title}", $"leads/{lead.Id}", "Formulario");
        form.SubmitCount++; form.LastSubmitAt = DateTime.UtcNow;
        await _forms.UpdateAsync(form);
        return new(true, 201, null, lead);
    }
}
