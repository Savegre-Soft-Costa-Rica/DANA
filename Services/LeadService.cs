using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public record ConvertOptions(bool CreateContact, bool CreateCompany, bool CreateDeal, string? PipelineId);

public class LeadService
{
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly DealService _deals;
    private readonly PipelineService _pipelines;
    private readonly ActivityService _activities;
    private readonly AutomationService _automations;

    public LeadService(IRepository<Lead> leads, IRepository<Contact> contacts, IRepository<Company> companies,
        DealService deals, PipelineService pipelines, ActivityService activities, AutomationService automations)
    {
        _leads = leads;
        _contacts = contacts;
        _companies = companies;
        _deals = deals;
        _pipelines = pipelines;
        _activities = activities;
        _automations = automations;
    }

    public Task<List<Lead>> GetAllAsync() => _leads.GetAllAsync();
    public Task<Lead?> GetAsync(string? id) => _leads.GetByIdAsync(id);
    public Task DeleteAsync(string id, string? by = null) => _leads.DeleteAsync(id, by);

    public async Task PurgeAsync(string id, string? by = null)
    {
        await _leads.PurgeAsync(id, by);
        await _activities.DeleteForAsync("lead", id);
    }

    public async Task SaveAsync(Lead lead, string? author = null)
    {
        lead.UpdatedBy = author ?? lead.UpdatedBy;
        if (lead.Id is null)
        {
            await _leads.InsertAsync(lead);
            await _activities.LogAsync("lead", lead.Id!, lead.Title, ActivityKinds.Created, "Creó el prospecto", author);
            await _automations.TriggerAsync(new AutomationEvent("leads", TriggerTypes.Created, lead, author));
            return;
        }

        var previous = await _leads.GetByIdAsync(lead.Id);
        await _leads.UpdateAsync(lead);
        if (previous is null) return;
        if (previous.Status != lead.Status)
        {
            await _activities.LogAsync("lead", lead.Id!, lead.Title, ActivityKinds.Stage, $"Cambió el estado de \"{LeadStatuses.Get(previous.Status).Name}\" a \"{LeadStatuses.Get(lead.Status).Name}\"", author);
            await _automations.TriggerAsync(new AutomationEvent("leads", TriggerTypes.StatusChanged, lead, author, new() { ["status"] = lead.Status }));
        }
        if (previous.AssignedTo != lead.AssignedTo)
            await _automations.TriggerAsync(new AutomationEvent("leads", TriggerTypes.AssigneeChanged, lead, author));
    }

    /// <summary>
    /// Convierte el prospecto en Contacto / Compañía / Negociación (igual que "Convertir" en Bitrix)
    /// y marca el prospecto como Convertido.
    /// </summary>
    public async Task<Lead> ConvertAsync(Lead lead, ConvertOptions options, string? author = null)
    {
        if (lead.Status == LeadStatuses.Converted)
            throw new InvalidOperationException("El prospecto ya fue convertido.");

        Company? company = null;
        if (options.CreateCompany && !string.IsNullOrWhiteSpace(lead.CompanyName))
        {
            company = await _companies.InsertAsync(new Company
            {
                Title = lead.CompanyName!,
                Type = "Cliente",
                Phone = lead.Phone,
                Email = lead.Email,
                AssignedTo = lead.AssignedTo,
                Comments = lead.Comments
            });
            lead.ConvertedCompanyId = company.Id;
            await _activities.LogAsync("company", company.Id!, company.Title, ActivityKinds.Created, $"Creada al convertir el prospecto \"{lead.Title}\"", author);
            await _automations.TriggerAsync(new AutomationEvent("companies", TriggerTypes.Created, company, author));
        }

        Contact? contact = null;
        if (options.CreateContact)
        {
            contact = await _contacts.InsertAsync(new Contact
            {
                Name = string.IsNullOrWhiteSpace(lead.Name) ? lead.Title : lead.Name!,
                LastName = lead.LastName,
                Position = lead.Position,
                Phone = lead.Phone,
                Email = lead.Email,
                Source = lead.Source,
                Type = "Cliente",
                CompanyId = company?.Id,
                AssignedTo = lead.AssignedTo,
                Comments = lead.Comments
            });
            lead.ConvertedContactId = contact.Id;
            await _activities.LogAsync("contact", contact.Id!, contact.FullName, ActivityKinds.Created, $"Creado al convertir el prospecto \"{lead.Title}\"", author);
            await _automations.TriggerAsync(new AutomationEvent("contacts", TriggerTypes.Created, contact, author));
        }

        if (options.CreateDeal)
        {
            var pipeline = (options.PipelineId is null ? null : await _pipelines.GetAsync(options.PipelineId))
                           ?? await _pipelines.GetDefaultAsync();
            var deal = await _deals.SaveAsync(new Deal
            {
                Title = lead.Title,
                PipelineId = pipeline.Id!,
                StageId = pipeline.FirstStage?.Id ?? "",
                ContactId = contact?.Id,
                CompanyId = company?.Id,
                LeadId = lead.Id,
                Amount = lead.Opportunity,
                Currency = lead.Currency,
                Source = lead.Source,
                AssignedTo = lead.AssignedTo,
                Comments = lead.Comments
            }, author);
            lead.ConvertedDealId = deal.Id;
        }

        lead.Status = LeadStatuses.Converted;
        await _leads.UpdateAsync(lead);
        var created = new List<string>();
        if (contact is not null) created.Add("contacto");
        if (company is not null) created.Add("compañía");
        if (lead.ConvertedDealId is not null) created.Add("negociación");
        await _activities.LogAsync("lead", lead.Id!, lead.Title, ActivityKinds.Converted, $"Convirtió el prospecto en {string.Join(", ", created)}", author);
        return lead;
    }
}
