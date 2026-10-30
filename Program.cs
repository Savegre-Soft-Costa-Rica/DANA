using System.Security.Claims;
using Dana.Components;
using Dana.Data;
using Dana.Models;
using Dana.Services;
using Dana.Services.Invoicing;
using Dana.Services.Messaging;
using Dana.Services.Telegram;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------- MongoDB ----------
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddSingleton<ChangeNotifier>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton(typeof(IRepository<>), typeof(MongoRepository<>));

// ---------- Servicios CRM ----------
builder.Services.AddSingleton<PipelineService>();
builder.Services.AddSingleton<DealService>();
builder.Services.AddSingleton<LeadService>();
builder.Services.AddSingleton<DashboardService>();
builder.Services.AddSingleton<DemoDataService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ActivityService>();
builder.Services.AddSingleton<BoardConfigService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<TaskService>();
builder.Services.AddSingleton<AttachmentService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions());
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(TwilioOptions.Section).Get<TwilioOptions>() ?? new TwilioOptions());
builder.Services.AddSingleton<TwilioMessagingService>();
builder.Services.AddSingleton<IElectronicInvoiceProvider, NotConfiguredInvoiceProvider>();
builder.Services.AddSingleton<InvoiceService>();
// Telegram: bot con long polling (sin URL pública). Sin token no se inicia.
builder.Services.AddSingleton(builder.Configuration.GetSection(TelegramOptions.Section).Get<TelegramOptions>() ?? new TelegramOptions());
builder.Services.AddHttpClient("telegram", c =>
{
    c.BaseAddress = new Uri("https://api.telegram.org/");
    c.Timeout = TimeSpan.FromSeconds(60); // mayor que el timeout de 25 s del long polling
});
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddHostedService<TelegramPollingWorker>();
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddSingleton<InboundLeadService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<LeadFormService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<NavService>();
builder.Services.AddSingleton<QuoteService>();
builder.Services.AddSingleton<ExportImportService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<Dana.Services.Ai.AgentTools>();
builder.Services.AddSingleton<Dana.Services.Ai.IChatProvider, Dana.Services.Ai.AnthropicProvider>();
builder.Services.AddSingleton<Dana.Services.Ai.IChatProvider, Dana.Services.Ai.OpenAiProvider>();
builder.Services.AddSingleton<Dana.Services.Ai.AgentService>();
builder.Services.AddSingleton<Dana.Services.Ai.OfficeService>();
builder.Services.AddSingleton<Dana.Services.Ai.WebSearchService>();
builder.Services.AddHttpClient("websearch", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHostedService<Dana.Services.Ai.AgentRoutineWorker>();
builder.Services.AddHttpClient("openai", c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Escribimos cuerpo: sin él, UseStatusCodePagesWithReExecute reejecutaría la petición hacia /not-found.
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsync("{\"error\":\"Demasiadas peticiones; intente de nuevo en un minuto.\"}", ct);
    };
    o.AddPolicy("public", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddCors(o => o.AddPolicy("public-api", p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("POST", "OPTIONS")));
builder.Services.AddHttpClient("automations", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<DrawerService>();
builder.Services.AddScoped<DialogService>();
builder.Services.AddHostedService<MongoInitializer>();

// ---------- Autenticación ----------
// Usuarios locales (correo + contraseña) guardados en Mongo + Google OAuth2 opcional.
// Google: Authentication:Google:ClientId / ClientSecret (user-secrets o variables de entorno).
var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(authOptions.Google);

var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/cuenta/login";
    options.LogoutPath = "/cuenta/logout";
    options.AccessDeniedPath = "/cuenta/acceso-denegado";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.Name = "dana.auth";
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Si el usuario fue desactivado o eliminado, invalidar su sesión.
    options.Events.OnValidatePrincipal = async ctx =>
    {
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
        var user = await users.GetAsync(id);
        if (user is null || !user.IsActive)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});

if (authOptions.Google.IsConfigured)
{
    auth.AddGoogle(options =>
    {
        options.ClientId = authOptions.Google.ClientId!;
        options.ClientSecret = authOptions.Google.ClientSecret!;
        options.CallbackPath = "/signin-google"; // URI de redirección autorizada en Google Cloud Console
        options.SaveTokens = false;
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.ClaimActions.MapJsonKey("picture", "picture");

        options.Events.OnTicketReceived = async ctx =>
        {
            var email = ctx.Principal?.FindFirstValue(ClaimTypes.Email) ?? "";
            var domain = email.Contains('@') ? email[(email.IndexOf('@') + 1)..] : "";
            var allowed = authOptions.Google.AllowedDomains;
            if (allowed.Count > 0 && !allowed.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Fail($"La cuenta {email} no pertenece a un dominio autorizado.");
                return;
            }

            // Enlazar con el usuario local (se crea si no existe) y emitir nuestros propios claims (id, rol).
            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
            var user = await users.SignInWithGoogleAsync(email,
                ctx.Principal?.FindFirstValue(ClaimTypes.Name),
                ctx.Principal?.FindFirstValue("picture"));
            if (user is null)
            {
                ctx.Fail("La cuenta está desactivada.");
                return;
            }
            ctx.Principal = UserService.CreatePrincipal(user);
        };
        options.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect("/cuenta/acceso-denegado?error=" + Uri.EscapeDataString(ctx.Failure?.Message ?? "Error de autenticación"));
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    // Toda la app exige sesión; las páginas de /cuenta usan [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("Admin", p => p.RequireRole(Roles.Admin));
});
builder.Services.AddCascadingAuthenticationState();

// Producción: claves de protección de datos persistentes (cookies válidas tras reinicios) y proxy inverso
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("dana");
}
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!string.Equals(builder.Configuration["DisableHttpsRedirect"], "true", StringComparison.OrdinalIgnoreCase)) app.UseHttpsRedirection();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ---------- Endpoints de sesión ----------
app.MapGet("/cuenta/login-google", (string? returnUrl) =>
{
    if (!authOptions.Google.IsConfigured) return Results.Redirect("/cuenta/login");
    var redirect = string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl;
    return Results.Challenge(new AuthenticationProperties { RedirectUri = redirect }, [GoogleDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapPost("/cuenta/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/cuenta/login?salio=1");
}).AllowAnonymous();

// Los archivos estáticos (CSS, JS, imágenes) deben servirse sin sesión; la FallbackPolicy aplicaría también a estos endpoints.
// API pública de prospectos: recibe el formulario de contacto del sitio web (React, WordPress, n8n…).
// Autenticación por clave de canal (configurable en /configuracion/api). CORS abierto: la clave es la credencial.
async Task<IResult> ReceiveLeadAsync(HttpContext ctx, InboundLeadService inbound)
{
    Dictionary<string, string> payload;
    string? raw = null;
    if (ctx.Request.HasJsonContentType())
    {
        using var reader = new StreamReader(ctx.Request.Body);
        raw = await reader.ReadToEndAsync();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            payload = InboundLeadService.Flatten(doc.RootElement);
        }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "JSON no válido." }); }
    }
    else if (ctx.Request.HasFormContentType)
    {
        var form = await ctx.Request.ReadFormAsync();
        payload = form.Where(kv => kv.Key != "__RequestVerificationToken").ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        raw = string.Join("&", payload.Select(kv => $"{kv.Key}={kv.Value}"));
    }
    else return Results.BadRequest(new { error = "Envíe JSON (application/json) o un formulario (application/x-www-form-urlencoded)." });

    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault() ?? ctx.Request.Query["key"].FirstOrDefault() ?? payload.GetValueOrDefault("apiKey");
    var result = await inbound.ProcessAsync(key, payload, ctx.Request.Headers.Origin.FirstOrDefault(), raw);
    return result.Ok
        ? Results.Created($"/leads/{result.Lead!.Id}", new { ok = true, id = result.Lead.Id, title = result.Lead.Title })
        : Results.Json(new { ok = false, error = result.Error }, statusCode: result.Status);
}
// Envío de un formulario público por API (JSON), sin clave: el slug identifica el formulario.
app.MapPost("/api/public/forms/{slug}", async (string slug, HttpContext ctx, LeadFormService forms) =>
{
    var form = await forms.FindBySlugAsync(slug);
    if (form is null) return Results.NotFound(new { ok = false, error = "Formulario no encontrado." });
    FormSubmission? input;
    try { input = await ctx.Request.ReadFromJsonAsync<FormSubmission>(); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { ok = false, error = "JSON no válido." }); }
    if (input is null) return Results.BadRequest(new { ok = false, error = "Cuerpo vacío." });
    var r = await forms.SubmitAsync(form, input);
    return r.Ok ? Results.Json(new { ok = true, id = r.Lead?.Id, message = form.SuccessMessage }, statusCode: 201)
                : Results.Json(new { ok = false, error = r.Error }, statusCode: r.Status);
}).AllowAnonymous().RequireCors("public-api");

app.MapPost("/api/public/leads", ReceiveLeadAsync).AllowAnonymous().RequireCors("public-api").RequireRateLimiting("public");
app.MapPost("/api/webhooks/leads", ReceiveLeadAsync).AllowAnonymous().RequireCors("public-api").RequireRateLimiting("public");
// Webhook de Twilio: autenticado por la firma X-Twilio-Signature, no por sesión.
app.MapPost(TwilioMessagingService.StatusPath, (HttpContext ctx, TwilioMessagingService twilio) => twilio.HandleStatusCallbackAsync(ctx))
    .AllowAnonymous().DisableAntiforgery();

// Exportación de tableros (requiere sesión)
app.MapGet("/export/{board}.{format}", async (string board, string format, ExportImportService export) =>
{
    if (board is not ("leads" or "deals" or "contacts" or "companies")) return Results.NotFound();
    var name = $"{ExportImportService.BoardTitle(board)}-{DateTime.Now:yyyyMMdd-HHmm}";
    return format == "csv"
        ? Results.File(await export.ExportCsvAsync(board), "text/csv; charset=utf-8", name + ".csv")
        : Results.File(await export.ExportXlsxAsync(board), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
});

// Descarga de adjuntos (requiere sesión por la FallbackPolicy)
app.MapPost("/api/firmar/{token}", async (string token, HttpContext ctx, DocumentService documents) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var name = form["nombre"].ToString().Trim();
    if (name.Length < 2 || form["acepto"] != "on") return Results.Redirect($"/firmar/{token}?error=1");
    var doc = await documents.SignAsync(token, name, form["cargo"].ToString(), form["firma"].ToString(), ctx.Connection.RemoteIpAddress?.ToString());
    return Results.Redirect(doc is null ? $"/firmar/{token}?error=2" : $"/firmar/{token}?ok=1");
}).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("public");

app.MapGet("/documentos/{id}/pdf", async (string id, DocumentService documents, IWebHostEnvironment env) =>
{
    var d = await documents.GetAsync(id);
    if (d is null) return Results.NotFound();
    var bytes = await documents.ToPdfAsync(d, env.WebRootPath);
    var name = string.Concat(d.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".pdf";
    return Results.File(bytes, "application/pdf", name);
});

app.MapGet("/documentos/{id}/word", async (string id, DocumentService documents) =>
{
    var d = await documents.GetAsync(id);
    if (d is null) return Results.NotFound();
    var bytes = await documents.ToDocxAsync(d);
    var name = string.Concat(d.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".docx";
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", name);
});

app.MapGet("/files/{id}", async (string id, AttachmentService attachments) =>
{
    var a = await attachments.GetAsync(id);
    if (a is null) return Results.NotFound();
    var path = attachments.PathFor(a);
    if (!File.Exists(path)) return Results.NotFound();
    return Results.File(path, a.ContentType, a.IsImage ? null : a.FileName, enableRangeProcessing: true);
});

// Catálogo público de productos (para webs externas): clave de canal (X-Api-Key o ?key=) con "catálogo" permitido
async Task<IResult> CatalogAuthAsync(HttpContext ctx, InboundLeadService inbound, Func<Task<IResult>> next)
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault() ?? ctx.Request.Query["key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) return Results.Json(new { ok = false, error = "Falta la clave de API (X-Api-Key o ?key=)." }, statusCode: 401);
    var channel = await inbound.FindByKeyAsync(key);
    if (channel is null || !channel.IsEnabled) return Results.Json(new { ok = false, error = "Clave de API no válida." }, statusCode: 401);
    if (!channel.AllowCatalog) return Results.Json(new { ok = false, error = "Este canal no tiene permitido el catálogo." }, statusCode: 403);
    return await next();
}
string PublicBase(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";
app.MapGet("/api/public/products", (HttpContext ctx, InboundLeadService inbound, QuoteService quotes, string? category, string? tag, string? q) =>
    CatalogAuthAsync(ctx, inbound, async () =>
    {
        var items = await quotes.GetCatalogAsync(category, tag, q);
        var cats = await quotes.GetCategoriesAsync();
        return Results.Ok(new { ok = true, count = items.Count, categories = cats.Select(c => new { c.Name, c.Color, c.Description }), items = items.Select(p => QuoteService.ToPublic(p, PublicBase(ctx))) });
    })).AllowAnonymous().RequireCors("public-api");
app.MapGet("/api/public/products/{id}", (string id, HttpContext ctx, InboundLeadService inbound, QuoteService quotes) =>
    CatalogAuthAsync(ctx, inbound, async () =>
    {
        var p = await quotes.GetProductAsync(id);
        return p is null || !p.IsActive || !p.IsPublic ? Results.NotFound(new { ok = false, error = "Producto no encontrado." }) : Results.Ok(new { ok = true, item = QuoteService.ToPublic(p, PublicBase(ctx)) });
    })).AllowAnonymous().RequireCors("public-api");
// Imágenes de productos: públicas (solo adjuntos de productos)
app.MapGet("/catalog/images/{id}", async (string id, AttachmentService attachments) =>
{
    var a = await attachments.GetAsync(id);
    if (a is null || a.EntityType != "product" || !a.IsImage) return Results.NotFound();
    var path = attachments.PathFor(a);
    return File.Exists(path) ? Results.File(path, a.ContentType, enableRangeProcessing: true) : Results.NotFound();
}).AllowAnonymous().RequireCors("public-api");

// Salud (Docker / balanceadores): comprueba la conexión a MongoDB
app.MapGet("/health", async (MongoContext mongo) =>
{
    try
    {
        await mongo.Database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
        return Results.Ok(new { status = "ok", mongo = "ok", time = DateTime.UtcNow });
    }
    catch (Exception ex) { return Results.Json(new { status = "degraded", mongo = ex.Message }, statusCode: 503); }
}).AllowAnonymous();

RestApi.Map(app);

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
