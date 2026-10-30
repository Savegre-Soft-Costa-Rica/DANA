using Dana.Data;
using Dana.Models;

namespace Dana.Services;

/// <summary>Acceso uniforme a los datos comunes de las entidades del CRM (título, correo, responsable, enlace).</summary>
public static class CrmEntities
{
    public static string BoardKeyOf(Entity e) => e switch { Lead => "leads", Deal => "deals", Contact => "contacts", Company => "companies", _ => "" };
    public static string TypeOf(Entity e) => e switch { Lead => "lead", Deal => "deal", Contact => "contact", Company => "company", _ => "" };

    public static string TitleOf(Entity e) => e switch { Lead l => l.Title, Deal d => d.Title, Contact c => c.FullName, Company k => k.Title, _ => "" };

    public static string? EmailOf(Entity e) => e switch { Lead l => l.Email, Contact c => c.Email, Company k => k.Email, _ => null };

    public static string? AssigneeOf(Entity e) => e switch { Lead l => l.AssignedTo, Deal d => d.AssignedTo, Contact c => c.AssignedTo, Company k => k.AssignedTo, _ => null };

    public static void SetAssignee(Entity e, string? user)
    {
        switch (e)
        {
            case Lead l: l.AssignedTo = user; break;
            case Deal d: d.AssignedTo = user; break;
            case Contact c: c.AssignedTo = user; break;
            case Company k: k.AssignedTo = user; break;
        }
    }

    public static string LinkOf(Entity e) => e switch
    {
        Lead => $"leads/{e.Id}", Deal => $"deals/{e.Id}", Contact => $"contacts/{e.Id}", Company => $"companies/{e.Id}", _ => ""
    };

    public static string LinkOf(string entityType, string id) => entityType switch
    {
        "lead" => $"leads/{id}", "contact" => $"contacts/{id}", "company" => $"companies/{id}", _ => $"deals/{id}"
    };

    public static string BoardOfType(string entityType) => entityType switch { "lead" => "leads", "contact" => "contacts", "company" => "companies", _ => "deals" };

    /// <summary>Guarda la entidad sin disparar automatizaciones.</summary>
    public static Task PersistAsync(IServiceProvider sp, Entity e) => e switch
    {
        Lead l => sp.GetRequiredService<IRepository<Lead>>().UpdateAsync(l),
        Deal d => sp.GetRequiredService<IRepository<Deal>>().UpdateAsync(d),
        Contact c => sp.GetRequiredService<IRepository<Contact>>().UpdateAsync(c),
        Company k => sp.GetRequiredService<IRepository<Company>>().UpdateAsync(k),
        _ => Task.CompletedTask
    };

    public static async Task<Entity?> LoadAsync(IServiceProvider sp, string entityType, string id) => entityType switch
    {
        "lead" => await sp.GetRequiredService<IRepository<Lead>>().GetByIdAsync(id),
        "contact" => await sp.GetRequiredService<IRepository<Contact>>().GetByIdAsync(id),
        "company" => await sp.GetRequiredService<IRepository<Company>>().GetByIdAsync(id),
        "deal" => await sp.GetRequiredService<IRepository<Deal>>().GetByIdAsync(id),
        _ => null
    };
}
