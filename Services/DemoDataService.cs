using Dana.Data;
using Dana.Models;

namespace Dana.Services;

/// <summary>Carga datos de ejemplo para probar el CRM. Solo actúa si la base está vacía.</summary>
public class DemoDataService
{
    private readonly IRepository<Lead> _leads;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly DealService _deals;
    private readonly PipelineService _pipelines;

    public DemoDataService(IRepository<Lead> leads, IRepository<Contact> contacts, IRepository<Company> companies,
        DealService deals, PipelineService pipelines)
    {
        _leads = leads;
        _contacts = contacts;
        _companies = companies;
        _deals = deals;
        _pipelines = pipelines;
    }

    public async Task<bool> IsEmptyAsync() =>
        await _contacts.CountAsync() == 0 && await _companies.CountAsync() == 0 && await _leads.CountAsync() == 0;

    public async Task SeedAsync()
    {
        if (!await IsEmptyAsync()) return;

        var pipeline = await _pipelines.GetDefaultAsync();
        var stages = pipeline.OrderedStages.ToList();

        var c1 = await _companies.InsertAsync(new Company { Title = "Constructora Horizonte S.A.", Type = "Cliente", Industry = "Construcción", Phone = "+506 2222-1111", Email = "info@horizonte.example", Web = "https://horizonte.example", Employees = 120 });
        var c2 = await _companies.InsertAsync(new Company { Title = "TecnoSoluciones CR", Type = "Cliente", Industry = "Tecnología", Phone = "+506 2233-4455", Email = "ventas@tecnosol.example", Employees = 35 });
        var c3 = await _companies.InsertAsync(new Company { Title = "Distribuidora La Central", Type = "Proveedor", Industry = "Comercio", Phone = "+506 2244-7788", Email = "compras@lacentral.example", Employees = 60 });

        var p1 = await _contacts.InsertAsync(new Contact { Name = "María", LastName = "Rodríguez", Position = "Gerente de compras", Phone = "+506 8888-1234", Email = "maria.rodriguez@horizonte.example", CompanyId = c1.Id, Type = "Cliente", Source = "Recomendación" });
        var p2 = await _contacts.InsertAsync(new Contact { Name = "Carlos", LastName = "Jiménez", Position = "CTO", Phone = "+506 8777-5678", Email = "cjimenez@tecnosol.example", CompanyId = c2.Id, Type = "Cliente", Source = "Sitio web" });
        var p3 = await _contacts.InsertAsync(new Contact { Name = "Ana", LastName = "Vargas", Position = "Directora comercial", Phone = "+506 8666-9012", Email = "avargas@lacentral.example", CompanyId = c3.Id, Type = "Proveedor", Source = "Feria / evento" });
        var p4 = await _contacts.InsertAsync(new Contact { Name = "Luis", LastName = "Mora", Position = "Independiente", Phone = "+506 8555-3456", Email = "luis.mora@example.com", Type = "Prospecto", Source = "Redes sociales" });

        async Task Deal(string title, int stageIdx, decimal amount, string? contactId, string? companyId, string type, int prob, int days) =>
            await _deals.SaveAsync(new Deal
            {
                Title = title, PipelineId = pipeline.Id!, StageId = stages[stageIdx].Id, Amount = amount, Currency = "USD",
                ContactId = contactId, CompanyId = companyId, Type = type, Probability = prob,
                ExpectedCloseDate = DateTime.UtcNow.Date.AddDays(days), Source = "Sitio web"
            });

        await Deal("Mantenimiento planta eléctrica", 0, 4500, p1.Id, c1.Id, "Servicio", 30, 30);
        await Deal("Licencias software ERP", 1, 18000, p2.Id, c2.Id, "Venta", 40, 45);
        await Deal("Instalación tableros", 2, 9800, p1.Id, c1.Id, "Venta", 60, 20);
        await Deal("Contrato soporte anual", 3, 12000, p2.Id, c2.Id, "Venta recurrente", 75, 10);
        await Deal("Suministro cableado", 2, 6300, p3.Id, c3.Id, "Venta", 55, 25);
        await Deal("Auditoría eléctrica", 4, 3200, p4.Id, null, "Servicio", 100, -5);
        await Deal("Proyecto iluminación LED", 5, 15000, p3.Id, c3.Id, "Venta", 0, -12);

        await _leads.InsertAsync(new Lead { Title = "Consulta web – Hotel Pacífico", Name = "Sofía", LastName = "Castro", CompanyName = "Hotel Pacífico", Phone = "+506 8444-1111", Email = "scastro@hotelpacifico.example", Source = "Sitio web", Status = LeadStatuses.New, Opportunity = 25000 });
        await _leads.InsertAsync(new Lead { Title = "Llamada – Panadería Don Pepe", Name = "Pedro", LastName = "Alfaro", CompanyName = "Panadería Don Pepe", Phone = "+506 8333-2222", Source = "Llamada", Status = LeadStatuses.InProcess, Opportunity = 2500 });
        await _leads.InsertAsync(new Lead { Title = "Feria industrial – Metalmecánica Sur", Name = "Jorge", LastName = "Pérez", CompanyName = "Metalmecánica Sur", Email = "jperez@metalsur.example", Source = "Feria / evento", Status = LeadStatuses.Qualified, Opportunity = 40000 });
        await _leads.InsertAsync(new Lead { Title = "Spam formulario", Source = "Sitio web", Status = LeadStatuses.Junk });
    }
}
