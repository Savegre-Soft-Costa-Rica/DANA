using System.Text.Json;
using System.Text.Json.Serialization;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>API REST /api/v1 con tokens personales. Expone prospectos, contactos, compañías, negociaciones, embudos, actividades y usuarios.</summary>
public static class RestApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").AllowAnonymous().RequireCors("public-api").AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var key = http.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                      ?? http.Request.Headers["X-Api-Key"].FirstOrDefault() ?? http.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key)) return Results.Json(new { error = "Falta el token (Authorization: Bearer <token>)." }, statusCode: 401);
            var tokens = http.RequestServices.GetRequiredService<IRepository<ApiToken>>();
            var token = await tokens.Collection.Find(t => t.Key == key && t.DeletedAt == null).FirstOrDefaultAsync();
            if (token is null || !token.IsEnabled) return Results.Json(new { error = "Token inválido o deshabilitado." }, statusCode: 401);
            token.LastUsedAt = DateTime.UtcNow; token.UseCount++;
            _ = tokens.Collection.UpdateOneAsync(t => t.Id == token.Id, Builders<ApiToken>.Update.Set(t => t.LastUsedAt, token.LastUsedAt).Set(t => t.UseCount, token.UseCount));
            http.Items["api.user"] = token.UserName;
            http.Items["api.admin"] = token.IsAdmin;
            return await next(ctx);
        });

        api.MapGet("/me", (HttpContext http) => Results.Ok(new { user = http.Items["api.user"], admin = http.Items["api.admin"] }));
        api.MapGet("/pipelines", async (PipelineService pipelines) => Results.Ok((await pipelines.GetAllAsync()).Select(p => new { p.Id, p.Name, p.IsDefault, stages = p.OrderedStages.Select(s => new { s.Id, s.Name, s.Color, kind = s.Kind.ToString() }) })));
        api.MapGet("/users", async (UserService users) => Results.Ok((await users.GetAllAsync()).Where(u => u.IsActive).Select(u => new { u.Id, u.Name, u.Email, u.Role })));

        MapCrud<Lead>(api, "leads", (l, q) => Matches(q, l.Title, l.FullName, l.CompanyName, l.Email, l.Phone),
            (sp, e, by) => sp.GetRequiredService<LeadService>().SaveAsync(e, by), (sp, id, by) => sp.GetRequiredService<LeadService>().DeleteAsync(id, by));
        MapCrud<Contact>(api, "contacts", (c, q) => Matches(q, c.FullName, c.Email, c.Phone, c.Position),
            async (sp, e, by) => { var r = sp.GetRequiredService<IRepository<Contact>>(); e.UpdatedBy = by; if (e.Id is null) { e.CreatedBy = by; await r.InsertAsync(e); } else await r.UpdateAsync(e); },
            (sp, id, by) => sp.GetRequiredService<IRepository<Contact>>().DeleteAsync(id, by));
        MapCrud<Company>(api, "companies", (c, q) => Matches(q, c.Title, c.Email, c.Phone, c.Industry, c.TaxId),
            async (sp, e, by) => { var r = sp.GetRequiredService<IRepository<Company>>(); e.UpdatedBy = by; if (e.Id is null) { e.CreatedBy = by; await r.InsertAsync(e); } else await r.UpdateAsync(e); },
            (sp, id, by) => sp.GetRequiredService<IRepository<Company>>().DeleteAsync(id, by));
        MapCrud<Deal>(api, "deals", (d, q) => Matches(q, d.Title, d.Comments),
            async (sp, e, by) => { await sp.GetRequiredService<DealService>().SaveAsync(e, by); }, (sp, id, by) => sp.GetRequiredService<DealService>().DeleteAsync(id, by));
        MapCrud<TaskItem>(api, "tasks", (t, q) => Matches(q, t.Title, t.Notes, t.EntityTitle),
            async (sp, e, by) => { await sp.GetRequiredService<TaskService>().SaveAsync(e, by); }, (sp, id, by) => sp.GetRequiredService<TaskService>().DeleteAsync(id));

        MapCrud<Product>(api, "products", (p, q) => Matches(q, p.Name, p.Sku, p.Description, p.Category, string.Join(' ', p.Tags)),
            (sp, e, by) => sp.GetRequiredService<QuoteService>().SaveProductAsync(e, by), (sp, id, by) => sp.GetRequiredService<QuoteService>().DeleteProductAsync(id, by));
        api.MapGet("/product-categories", async (QuoteService quotes) => Results.Ok(await quotes.GetCategoriesAsync()));

        // Mover negociación de etapa
        api.MapPost("/deals/{id}/stage", async (string id, HttpContext http, DealService deals) =>
        {
            var body = await http.Request.ReadFromJsonAsync<Dictionary<string, string>>() ?? new();
            if (!body.TryGetValue("stageId", out var stageId)) return Results.BadRequest(new { error = "stageId requerido" });
            var d = await deals.MoveToStageAsync(id, stageId, http.Items["api.user"]?.ToString());
            return d is null ? Results.NotFound() : Results.Ok(d);
        });
        // Nota en la línea de tiempo
        api.MapPost("/{board}/{id}/notes", async (string board, string id, HttpContext http, ActivityService activities, IServiceProvider sp) =>
        {
            var type = board switch { "leads" => "lead", "contacts" => "contact", "companies" => "company", "deals" => "deal", _ => null };
            if (type is null) return Results.NotFound();
            var entity = await CrmEntities.LoadAsync(sp, type, id);
            if (entity is null) return Results.NotFound();
            var body = await http.Request.ReadFromJsonAsync<Dictionary<string, string>>() ?? new();
            if (!body.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new { error = "text requerido" });
            var a = await activities.LogAsync(type, id, CrmEntities.TitleOf(entity), ActivityKinds.Note, text, http.Items["api.user"]?.ToString());
            return Results.Created($"/api/v1/{board}/{id}/notes/{a.Id}", a);
        });
        api.MapGet("/{board}/{id}/notes", async (string board, string id, ActivityService activities) =>
        {
            var type = board switch { "leads" => "lead", "contacts" => "contact", "companies" => "company", "deals" => "deal", _ => null };
            return type is null ? Results.NotFound() : Results.Ok(await activities.GetForAsync(type, id));
        });
    }

    private static bool Matches(string q, params string?[] fields) => fields.Any(f => f is not null && f.Contains(q, StringComparison.OrdinalIgnoreCase));

    private static void MapCrud<T>(RouteGroupBuilder api, string path, Func<T, string, bool> search, Func<IServiceProvider, T, string?, Task> save, Func<IServiceProvider, string, string?, Task> delete) where T : Entity
    {
        api.MapGet($"/{path}", async (HttpContext http, IRepository<T> repo, string? q, int? limit, int? skip, bool? archived) =>
        {
            var items = (await repo.GetAllAsync()).Where(i => archived == true || !i.IsArchived);
            if (!string.IsNullOrWhiteSpace(q)) items = items.Where(i => search(i, q));
            var list = items.Skip(skip ?? 0).Take(Math.Clamp(limit ?? 100, 1, 500)).ToList();
            return Results.Json(new { count = list.Count, items = list }, Json);
        });
        api.MapGet($"/{path}/{{id}}", async (string id, IRepository<T> repo) =>
        {
            var e = await repo.GetByIdAsync(id);
            return e is null ? Results.NotFound(new { error = "No encontrado" }) : Results.Json(e, Json);
        });
        api.MapPost($"/{path}", async (HttpContext http, IServiceProvider sp) =>
        {
            T? e;
            try { e = await http.Request.ReadFromJsonAsync<T>(Json); } catch (JsonException ex) { return Results.BadRequest(new { error = "JSON no válido: " + ex.Message }); }
            if (e is null) return Results.BadRequest(new { error = "Cuerpo vacío" });
            e.Id = null;
            try { await save(sp, e, http.Items["api.user"]?.ToString()); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
            return Results.Json(e, Json, statusCode: 201);
        });
        api.MapPut($"/{path}/{{id}}", async (string id, HttpContext http, IServiceProvider sp, IRepository<T> repo) =>
        {
            var existing = await repo.GetByIdAsync(id);
            if (existing is null) return Results.NotFound(new { error = "No encontrado" });
            // Actualización parcial: se aplican solo los campos enviados sobre el documento existente
            JsonDocument? patch;
            try { patch = await http.Request.ReadFromJsonAsync<JsonDocument>(); } catch (JsonException ex) { return Results.BadRequest(new { error = "JSON no válido: " + ex.Message }); }
            if (patch is null) return Results.BadRequest(new { error = "Cuerpo vacío" });
            var merged = JsonSerializer.SerializeToNode(existing, Json)!.AsObject();
            foreach (var p in patch.RootElement.EnumerateObject())
            {
                if (p.Name is "id" or "createdAt" or "deletedAt") continue;
                merged[p.Name] = System.Text.Json.Nodes.JsonNode.Parse(p.Value.GetRawText());
            }
            var e = merged.Deserialize<T>(Json)!;
            e.Id = id; e.CreatedAt = existing.CreatedAt; e.CreatedBy = existing.CreatedBy;
            try { await save(sp, e, http.Items["api.user"]?.ToString()); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
            return Results.Json(e, Json);
        });
        api.MapDelete($"/{path}/{{id}}", async (string id, HttpContext http, IServiceProvider sp, IRepository<T> repo) =>
        {
            if (await repo.GetByIdAsync(id) is null) return Results.NotFound(new { error = "No encontrado" });
            await delete(sp, id, http.Items["api.user"]?.ToString());
            return Results.NoContent();
        });
    }
}
