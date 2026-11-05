using System.Text.Json;
using Dana.Data;
using Dana.Models;
using Dana.Services.Ai;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>
/// Servidor MCP (Model Context Protocol) de Dana. Expone TODO el catálogo de herramientas de los
/// agentes de IA (<see cref="AgentTools.Catalog"/>) como herramientas MCP, de modo que un cliente
/// MCP —Claude Code, Claude Desktop, etc.— puede operar el CRM completo: prospectos, contactos,
/// compañías, negociaciones, cotizaciones, facturación electrónica, contabilidad, suscripciones,
/// documentos y proyectos.
///
/// Transporte: HTTP "Streamable" (sin estado) en la ruta <c>/mcp</c>.
/// Autenticación: los mismos tokens personales de la API REST (página <c>/configuracion/api</c>),
/// por cabecera <c>Authorization: Bearer &lt;token&gt;</c> o <c>X-Api-Key</c>.
/// </summary>
public static class DanaMcp
{
    // Clave en HttpContext.Items donde el filtro deja el usuario autenticado (para auditar quién actúa).
    private const string UserKey = "mcp.user";

    /// <summary>Registra el servidor MCP en el contenedor de servicios. Todo el catálogo de
    /// <see cref="AgentTools"/> se publica dinámicamente; no hay que tocar nada al añadir una herramienta nueva.</summary>
    /// <param name="allowWrites">Si es <c>false</c>, las herramientas que modifican datos quedan bloqueadas
    /// (solo lectura). Las acciones que piden confirmación —correo, SMS/WhatsApp— se ejecutan directamente
    /// cuando se permiten escrituras, porque el propio cliente MCP ya pide la aprobación del usuario antes de
    /// cada llamada a una herramienta.</param>
    public static IServiceCollection AddDanaMcp(this IServiceCollection services, bool allowWrites)
    {
        services.AddMcpServer()
            .WithHttpTransport(h => h.Stateless = true)
            .WithListToolsHandler((_, _) =>
                ValueTask.FromResult(new ListToolsResult { Tools = AgentTools.Catalog.Select(ToMcpTool).ToList() }))
            .WithCallToolHandler(async (request, ct) =>
            {
                var name = request.Params?.Name ?? "";
                var tool = AgentTools.Find(name);
                if (tool is null)
                    return Fail($"Herramienta desconocida: {name}");

                // Los argumentos llegan como diccionario nombre→valor; AgentTools espera un único objeto JSON.
                var args = request.Params?.Arguments;
                var input = args is { Count: > 0 }
                    ? JsonSerializer.SerializeToElement(args)
                    : EmptyObject;

                var author = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Items[UserKey] as string ?? "MCP";
                var tools = (request.Services ?? throw new InvalidOperationException("Sin proveedor de servicios"))
                    .GetRequiredService<AgentTools>();

                // Confirmed = allowWrites: el cliente MCP ya confirma cada llamada con el usuario.
                var context = new ToolContext(author, allowWrites, Confirmed: allowWrites);
                var result = await tools.ExecuteAsync(name, input, context);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = result.Output }],
                    IsError = result.IsError,
                };
            });
        return services;
    }

    /// <summary>Publica el endpoint MCP en <c>/mcp</c>, protegido con los tokens de la API REST.
    /// <c>AllowAnonymous</c> evita que la <c>FallbackPolicy</c> global (exige sesión) redirija a /cuenta/login
    /// con un 302; la autenticación real la hace el filtro de token de abajo (igual que la API REST).</summary>
    public static IEndpointConventionBuilder MapDanaMcp(this WebApplication app) =>
        app.MapMcp("/mcp").AllowAnonymous().DisableAntiforgery().AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var key = http.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                      ?? http.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key))
                return Results.Json(new { error = "Falta el token (Authorization: Bearer <token>)." }, statusCode: 401);

            var tokens = http.RequestServices.GetRequiredService<IRepository<ApiToken>>();
            var token = await tokens.Collection.Find(t => t.Key == key && t.DeletedAt == null).FirstOrDefaultAsync();
            if (token is null || !token.IsEnabled)
                return Results.Json(new { error = "Token inválido o deshabilitado." }, statusCode: 401);

            token.LastUsedAt = DateTime.UtcNow; token.UseCount++;
            _ = tokens.Collection.UpdateOneAsync(t => t.Id == token.Id,
                Builders<ApiToken>.Update.Set(t => t.LastUsedAt, token.LastUsedAt).Set(t => t.UseCount, token.UseCount));
            http.Items[UserKey] = token.UserName;
            return await next(ctx);
        });

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    private static Tool ToMcpTool(ToolDef d) => new()
    {
        Name = d.Name,
        Title = d.Title,
        Description = d.Write ? d.Description + " [MODIFICA DATOS]" : d.Description,
        InputSchema = JsonSerializer.SerializeToElement(d.Schema),
    };

    private static CallToolResult Fail(string message) =>
        new() { Content = [new TextContentBlock { Text = message }], IsError = true };
}
