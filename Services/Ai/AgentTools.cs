using System.Globalization;
using System.Text.Json;
using Dana.Data;
using Dana.Models;
using MongoDB.Driver;
using Dana.Services.Messaging;

namespace Dana.Services.Ai;

/// <summary>Definición de una herramienta que el modelo puede invocar (esquema JSON neutral para Claude y OpenAI).</summary>
public record ToolDef(string Name, string Title, string Description, object Schema, bool Write = false, bool Confirm = false);

/// <summary>Resultado de ejecutar una herramienta.</summary>
public record ToolResult(string Output, bool IsError = false);

/// <summary>Contexto de ejecución: quién actúa, con qué permisos y desde qué agente.</summary>
public record ToolContext(string Author, bool AllowWrites, AiAgent? Agent = null, string? User = null, bool Confirmed = false, AgentConversation? Conversation = null);

/// <summary>Catálogo y ejecución de herramientas sobre los datos del CRM. Los agentes actúan en nombre del usuario.</summary>
public class AgentTools
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const string Types = "lead (prospecto), contact (contacto), company (compañía), deal (negociación), task (tarea), product (producto)";

    public static readonly ToolDef[] Catalog =
    [
        new("buscar", "Buscar registros", $"Busca registros del CRM por texto. Tipos: {Types}. Devuelve una lista resumida (id, título y campos clave). Úsala antes de obtener o actualizar un registro.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "task", "product" } }, texto = new { type = "string", description = "Texto a buscar (nombre, correo, empresa…). Vacío = los más recientes." }, limite = new { type = "integer", description = "Máximo de resultados (1-50)", @default = 10 } }, required = new[] { "tipo" } }),
        new("obtener", "Obtener registro", "Devuelve todos los campos de un registro por su id, incluidas sus últimas actividades y tareas.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "task", "product" } }, id = new { type = "string" } }, required = new[] { "tipo", "id" } }),
        new("campos", "Campos disponibles", "Lista los campos de un tipo de registro (lead, contact, company o deal): los campos estándar y los campos personalizados ya definidos (con su título y tipo). Úsala ANTES de enriquecer un registro para ver qué campos existen y reutilizarlos en vez de crear duplicados; los campos personalizados se escriben con el objeto 'custom' en crear/actualizar.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } } }, required = new[] { "tipo" } }),
        new("estadisticas", "Estadísticas", "Resumen numérico del CRM: prospectos por estado, negociaciones abiertas/ganadas/perdidas y montos (moneda base), tareas pendientes y vencidas.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("priorizar", "Priorizar", "Devuelve los prospectos o negociaciones MÁS prioritarios ahora mismo, con su puntuación (0-100), nivel (Alta/Media/Baja) y la siguiente mejor acción recomendada. Úsala para decidir en qué enfocarse hoy o para reactivar lo estancado. tipo: lead | deal.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "deal" } }, limite = new { type = "integer", @default = 10 } }, required = new[] { "tipo" } }),
        new("resumen_embudo", "Resumen de embudo", "Negociaciones de un embudo agrupadas por etapa, con cantidad y monto por etapa. Sin pipelineId usa el embudo por defecto.",
            new { type = "object", properties = new { pipelineId = new { type = "string" } }, required = Array.Empty<string>() }),
        new("listar_embudos", "Listar embudos", "Lista los embudos (pipelines) y sus etapas con ids.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("listar_usuarios", "Listar usuarios", "Lista los usuarios del sistema (nombre y rol) para asignar responsables.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("crear", "Crear registro", "Crea un registro. Campos por tipo — lead: title*, name, lastName, companyName, email, phone, source, status(NEW|IN_PROCESS|QUALIFIED|JUNK), opportunity, currency, assignedTo, comments. contact: name*, lastName, email, phone, position, companyId, type, comments. company: title*, industry, email, phone, web, address, comments. deal: title*, amount, currency, pipelineId, stageId, contactId, companyId, probability, expectedCloseDate(YYYY-MM-DD), assignedTo, comments. task: title*, kind(call|meeting|email|task), dueAt(ISO), assignedTo, notes, entityType, entityId. product: name*, sku, description, category, tags(lista), unit, price, currency, taxRate, stock(-1 = sin control), isActive, isPublic. Para datos extra que no encajen en los campos estándar (lead/contact/company/deal) añade un objeto 'custom' con el título del campo como clave (ver la herramienta actualizar).",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "task", "product" } }, datos = new { type = "object", description = "Campos del registro", additionalProperties = true } }, required = new[] { "tipo", "datos" } }, Write: true),
        new("actualizar", "Actualizar registro", "Modifica campos de un registro existente (mismos campos que en 'crear'; para mover una negociación de etapa use stageId; para marcar una tarea use isDone=true; para productos: price, stock, category, tags, isActive, isPublic). IMPORTANTE: los datos que tengan campo estándar (correo→email, teléfono→phone, sitio web→web, dirección→address, industria→industry, nº de empleados→employees, RUC/cédula→taxId, etc.) van SIEMPRE en su campo estándar, NO en 'custom'; consulta la lista con la herramienta 'campos' si dudas. Usa el objeto 'custom' SOLO para datos que de verdad no tienen campo estándar (p. ej. año de fundación, horario de atención, certificaciones), con el TÍTULO del campo como clave: datos:{\"custom\":{\"Año de fundación\":\"2007\"}}. Los campos personalizados que ya existan se reutilizan por su título; los que no existan se crean como campo nuevo del tablero. Solo lead, contact, company y deal admiten campos personalizados.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "task", "product" } }, id = new { type = "string" }, datos = new { type = "object", additionalProperties = true } }, required = new[] { "tipo", "id", "datos" } }, Write: true),
        new("agregar_nota", "Agregar nota", "Añade una actualización/nota a la línea de tiempo de un prospecto, contacto, compañía o negociación.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, id = new { type = "string" }, texto = new { type = "string" } }, required = new[] { "tipo", "id", "texto" } }, Write: true),
        new("convertir", "Convertir prospecto", "Convierte un prospecto (lead) en contacto, compañía y/o negociación con sus datos y lo marca como Convertido. Indica qué crear (por defecto los tres); pipelineId opcional para la negociación (si falta, usa el embudo predeterminado). Solo prospectos aún no convertidos.",
            new { type = "object", properties = new { id = new { type = "string" }, contacto = new { type = "boolean", @default = true }, compania = new { type = "boolean", @default = true }, negociacion = new { type = "boolean", @default = true }, pipelineId = new { type = "string" } }, required = new[] { "id" } }, Write: true),
        new("archivar", "Archivar / restaurar", "Archiva o restaura un registro (lead, contact, company o deal). archivar=true lo archiva (deja de verse en el tablero salvo con el filtro Archivados); archivar=false lo restaura.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, id = new { type = "string" }, archivar = new { type = "boolean", @default = true } }, required = new[] { "tipo", "id" } }, Write: true),
        new("eliminar", "Eliminar (a papelera)", "Envía un registro a la papelera (borrado lógico, recuperable). Tipos: lead, contact, company, deal, product. Úsala para descartar prospectos basura (JUNK) o duplicados.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "product" } }, id = new { type = "string" } }, required = new[] { "tipo", "id" } }, Write: true),
        new("emitir_factura", "Emitir factura electrónica", "Crea y emite una factura electrónica (Hacienda, Costa Rica) a partir de una cotización (quoteId): toma sus líneas con el código CABYS de cada producto y los datos del receptor (de la compañía/contacto). En modo Sandbox se simula y queda aceptada. Con emitir=false solo deja el borrador. Requiere que los productos tengan CABYS; indica cedulaReceptor si la compañía no la tiene.",
            new { type = "object", properties = new { quoteId = new { type = "string" }, cedulaReceptor = new { type = "string" }, emitir = new { type = "boolean", @default = true } }, required = new[] { "quoteId" } }, Write: true),
        new("crear_enlace_pago", "Crear enlace de pago", "Genera un enlace de pago (Tilopay; en Sandbox se simula) por un monto, para enviar al cliente (p. ej. por el chat o correo). Puedes ligarlo a una negociación (dealId), cotización (quoteId) o factura (facturaId). Devuelve la URL.",
            new { type = "object", properties = new { monto = new { type = "number" }, moneda = new { type = "string", @default = "CRC" }, descripcion = new { type = "string" }, dealId = new { type = "string" }, quoteId = new { type = "string" }, facturaId = new { type = "string" } }, required = new[] { "monto", "descripcion" } }, Write: true),
        new("solicitar_firma", "Solicitar firma de documento", "Agrega un firmante a un documento del sistema y devuelve su enlace de firma (firmar/{token}) para enviárselo al cliente. Indica el id del documento y el nombre del firmante; correo y rol opcionales. Puedes repetirla para varios firmantes.",
            new { type = "object", properties = new { documentoId = new { type = "string" }, nombre = new { type = "string" }, correo = new { type = "string" }, rol = new { type = "string" } }, required = new[] { "documentoId", "nombre" } }, Write: true),
        new("cotizaciones_buscar", "Buscar cotizaciones", "Lista cotizaciones por texto, estado (draft|sent|accepted|rejected|expired) o negociación (dealId), con número, total, vencimiento y estado.",
            new { type = "object", properties = new { texto = new { type = "string" }, estado = new { type = "string" }, dealId = new { type = "string" }, limite = new { type = "integer", @default = 20 } }, required = Array.Empty<string>() }),
        new("cotizacion_obtener", "Obtener cotización", "Devuelve una cotización completa: líneas (producto, cantidad, precio, descuento, impuesto), totales, estado, vigencia y negociación asociada. Úsala para revisar o corregir una cotización.",
            new { type = "object", properties = new { id = new { type = "string", description = "Id o número (p. ej. COT-0003)" } }, required = new[] { "id" } }),
        new("cotizacion_crear", "Crear cotización", "Crea una cotización. Líneas: productId (usa el precio/unidad/impuesto del catálogo) o description+unitPrice para líneas manuales; quantity, discountPercent opcionales. dealId opcional (vincula a la negociación y toma su contacto/compañía).",
            new { type = "object", properties = new { title = new { type = "string" }, dealId = new { type = "string" }, contactId = new { type = "string" }, companyId = new { type = "string" }, currency = new { type = "string" }, validDays = new { type = "integer" }, notes = new { type = "string" },
                lines = new { type = "array", items = new { type = "object", properties = new { productId = new { type = "string" }, description = new { type = "string" }, quantity = new { type = "number" }, unitPrice = new { type = "number" }, discountPercent = new { type = "number" }, taxRate = new { type = "number" } } } } }, required = new[] { "title", "lines" } }, Write: true),
        new("cotizacion_actualizar", "Actualizar cotización", "Modifica una cotización: status (draft|sent|accepted|rejected|expired), title, notes, validUntil (YYYY-MM-DD) o reemplaza todas sus líneas (lines, mismo formato que en crear).",
            new { type = "object", properties = new { id = new { type = "string" }, status = new { type = "string" }, title = new { type = "string" }, notes = new { type = "string" }, validUntil = new { type = "string" }, lines = new { type = "array", items = new { type = "object", additionalProperties = true } } }, required = new[] { "id" } }, Write: true),
        new("buscar_web", "Buscar en internet", "Busca en la web (empresas, personas, noticias, directorios) y devuelve títulos, URLs y resúmenes. Útil para encontrar prospectos nuevos, investigar una compañía o verificar datos de contacto. Combínala con leer_pagina para profundizar.",
            new { type = "object", properties = new { consulta = new { type = "string" }, limite = new { type = "integer", @default = 6 } }, required = new[] { "consulta" } }),
        new("leer_pagina", "Leer página web", "Descarga una página pública (URL completa) y devuelve su texto para extraer datos: servicios, dirección, correo, teléfono, personas de contacto.",
            new { type = "object", properties = new { url = new { type = "string" } }, required = new[] { "url" } }),
        new("enviar_correo", "Enviar correo", "OBLIGATORIA para enviar correos: prepara el envío con destinatario, asunto y cuerpo. No envía de inmediato: el sistema muestra al usuario una tarjeta con botones para confirmar o cancelar, así que invócala directamente sin pedir permiso antes. Opcionalmente tipo e id del registro asociado para dejar constancia en su línea de tiempo.",
            new { type = "object", properties = new { para = new { type = "string" }, asunto = new { type = "string" }, cuerpo = new { type = "string" }, tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, id = new { type = "string" }, documentoId = new { type = "string", description = "Id de un documento del sistema para adjuntarlo" }, formato = new { type = "string", @enum = new[] { "pdf", "word" }, description = "Formato del documento adjunto (pdf por defecto)" }, adjuntoId = new { type = "string", description = "Id de un archivo adjunto para reenviarlo" } }, required = new[] { "para", "asunto", "cuerpo" } }, Write: true, Confirm: true),
        new("enviar_mensaje", "Enviar SMS/WhatsApp", "Prepara y envía un SMS o WhatsApp mediante Twilio. Llámala directamente (el sistema pide la confirmación al usuario con un botón). canal: sms | whatsapp; numero en formato internacional.",
            new { type = "object", properties = new { canal = new { type = "string", @enum = new[] { "sms", "whatsapp" } }, numero = new { type = "string" }, texto = new { type = "string" }, tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, id = new { type = "string" } }, required = new[] { "canal", "numero", "texto" } }, Write: true, Confirm: true),
        new("notificar_usuario", "Notificar a un usuario", "Envía una notificación interna (campana) a un usuario del sistema con un texto y un enlace opcional.",
            new { type = "object", properties = new { usuario = new { type = "string", description = "Nombre del usuario" }, texto = new { type = "string" }, enlace = new { type = "string" } }, required = new[] { "usuario", "texto" } }, Write: true),
        new("recordar", "Recordar", "Guarda en tu memoria de largo plazo un hecho, preferencia o acuerdo útil para futuras conversaciones (p. ej. \"Steven prefiere informes en tablas\", \"la empresa no vende a particulares\"). alcance: usuario (solo con quien hablas) o equipo (todos).",
            new { type = "object", properties = new { texto = new { type = "string" }, alcance = new { type = "string", @enum = new[] { "usuario", "equipo" }, @default = "usuario" } }, required = new[] { "texto" } }),
        new("olvidar", "Olvidar", "Elimina un recuerdo de tu memoria por su id (los ids aparecen en la lista de memoria del system prompt).",
            new { type = "object", properties = new { id = new { type = "string" } }, required = new[] { "id" } }),
        new("documentos_buscar", "Buscar documentos", "Lista documentos del sistema (NDA, contratos, propuestas, cartas, actas) por texto, tipo o registro ligado (tipoRegistro + idRegistro). Devuelve id, título, tipo, estado, versión y enlace.",
            new { type = "object", properties = new { texto = new { type = "string" }, tipo = new { type = "string", @enum = new[] { "nda", "contrato", "propuesta", "carta", "acta", "otro" } }, tipoRegistro = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, idRegistro = new { type = "string" } }, required = Array.Empty<string>() }),
        new("documentos_plantillas", "Listar plantillas de documentos", "Lista las plantillas disponibles (predefinidas y propias de la empresa) con id, título, tipo y descripción, para usarlas en documento_crear.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("documento_obtener", "Leer documento", "Devuelve el contenido completo (Markdown) de un documento del sistema, con sus metadatos y versión.",
            new { type = "object", properties = new { id = new { type = "string" } }, required = new[] { "id" } }),
        new("documento_crear", "Crear documento", "Crea un documento del sistema (queda guardado, versionado, exportable a Word y visible en la pestaña Documentos del registro). contenido en Markdown (# títulos, listas, **negrita**, tablas con |). Si indicas plantilla (nda|contrato|propuesta|carta|acta) y no das contenido, se genera desde la plantilla con los datos del registro; puedes dar contenido propio para sustituirla. Liga el documento a un registro con tipoRegistro + idRegistro.",
            new { type = "object", properties = new { titulo = new { type = "string" }, tipo = new { type = "string", @enum = new[] { "nda", "contrato", "propuesta", "carta", "acta", "otro" } }, plantilla = new { type = "string", description = "nda | contrato | propuesta | carta | acta, o el nombre de una plantilla propia (ver documentos_plantillas)" }, contenido = new { type = "string" }, tipoRegistro = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, idRegistro = new { type = "string" }, asunto = new { type = "string", description = "Objeto del documento para rellenar la plantilla" } }, required = new[] { "titulo", "tipo" } }, Write: true),
        new("documento_actualizar", "Actualizar documento", "Modifica un documento existente: contenido completo nuevo (crea una versión), título, estado (draft|review|final|signed) y nota de cambio. Para cambios parciales, lee el documento, aplica la edición y envía el contenido completo.",
            new { type = "object", properties = new { id = new { type = "string" }, contenido = new { type = "string" }, titulo = new { type = "string" }, estado = new { type = "string" }, nota = new { type = "string" } }, required = new[] { "id" } }, Write: true),
        new("documento_importar", "Importar Word/PDF como documento", "Convierte un archivo adjunto (docx, pdf, md, txt) en un documento editable del sistema, opcionalmente ligado a un registro. Útil cuando el usuario sube un contrato recibido para revisarlo o adaptarlo.",
            new { type = "object", properties = new { adjuntoId = new { type = "string" }, titulo = new { type = "string" }, tipo = new { type = "string", @enum = new[] { "nda", "contrato", "propuesta", "carta", "acta", "otro" } }, tipoRegistro = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal" } }, idRegistro = new { type = "string" } }, required = new[] { "adjuntoId" } }, Write: true),
        new("adjuntos_listar", "Listar adjuntos", "Lista los archivos adjuntos de un registro (tipo + id) o de esta conversación (tipo=conversation, id de la conversación): nombre, tamaño, formato e id para leerlos.",
            new { type = "object", properties = new { tipo = new { type = "string", @enum = new[] { "lead", "contact", "company", "deal", "conversation" } }, id = new { type = "string" } }, required = new[] { "tipo", "id" } }),
        new("adjunto_leer", "Leer adjunto", "Extrae el texto de un archivo adjunto por su id (txt, md, csv, html, docx, pdf, xlsx) para revisarlo, resumirlo o usarlo como base de un documento nuevo.",
            new { type = "object", properties = new { id = new { type = "string" } }, required = new[] { "id" } }),
        new("consultar_agente", "Consultar a otro agente", "Envía una pregunta o encargo a otro agente del equipo (por nombre) y devuelve su respuesta. Úsala para delegar en especialistas (cotizador, analista, redacción…) o pedir una segunda opinión.",
            new { type = "object", properties = new { agente = new { type = "string", description = "Nombre del agente" }, mensaje = new { type = "string" } }, required = new[] { "agente", "mensaje" } }),
    ];

    public static ToolDef? Find(string name) => Catalog.FirstOrDefault(t => t.Name == name);

    private readonly LeadService _leads;
    private readonly DealService _deals;
    private readonly IRepository<Contact> _contacts;
    private readonly IRepository<Company> _companies;
    private readonly IRepository<Lead> _leadRepo;
    private readonly IRepository<Deal> _dealRepo;
    private readonly IRepository<Product> _products;
    private readonly PipelineService _pipelines;
    private readonly TaskService _tasks;
    private readonly ActivityService _activities;
    private readonly UserService _users;
    private readonly SettingsService _settings;
    private readonly QuoteService _quotes;
    private readonly IRepository<Quote> _quoteRepo;
    private readonly BoardConfigService _boards;
    private readonly IServiceProvider _sp;

    public AgentTools(LeadService leads, DealService deals, IRepository<Contact> contacts, IRepository<Company> companies, IRepository<Lead> leadRepo, IRepository<Deal> dealRepo,
        IRepository<Product> products, PipelineService pipelines, TaskService tasks, ActivityService activities, UserService users, SettingsService settings, QuoteService quotes, IRepository<Quote> quoteRepo, BoardConfigService boards, IServiceProvider sp)
    {
        _quoteRepo = quoteRepo;
        _leads = leads; _deals = deals; _contacts = contacts; _companies = companies; _leadRepo = leadRepo; _dealRepo = dealRepo;
        _products = products; _pipelines = pipelines; _tasks = tasks; _activities = activities; _users = users; _settings = settings; _quotes = quotes; _boards = boards; _sp = sp;
    }

    public Task<ToolResult> ExecuteAsync(string name, JsonElement input, string author, bool allowWrites) => ExecuteAsync(name, input, new ToolContext(author, allowWrites));

    public async Task<ToolResult> ExecuteAsync(string name, JsonElement input, ToolContext ctx)
    {
        var def = Find(name);
        var author = ctx.Author; var allowWrites = ctx.AllowWrites;
        if (def is null) return new("Herramienta desconocida: " + name, true);
        if (def.Write && !allowWrites) return new("Este agente no tiene permiso para modificar datos. Pida al usuario que active \"Puede modificar datos\" en la configuración del agente.", true);
        if (def.Confirm && !ctx.Confirmed)
        {
            var autonomous = ctx.Conversation is null || ctx.User is null || ctx.User == AgentService.SystemUser || ctx.User.EndsWith("(agente)");
            if (autonomous)
            {
                // Sin usuario que confirme: solo se ejecuta si el agente tiene auto-aprobación y dentro del tope diario.
                if (ctx.Agent is null || !ctx.Agent.AutoApprove)
                    return new("Esta acción requiere confirmación de un usuario en el chat. Para permitirla en rutinas/automatizaciones, active \"Auto-aprobar envíos\" en la configuración del agente.", true);
                var gate = await _sp.GetRequiredService<AgentService>().TryConsumeSendAsync(ctx.Agent);
                if (gate is not null) return new(gate, true);
                // auto-aprobado y dentro del límite → continúa y ejecuta directamente (sin acción pendiente)
            }
            else
            {
                var summary = Summarize(name, input);
                ctx.Conversation!.Messages.Add(new AgentMessage { Role = "action", Text = summary, Action = new PendingAction { Tool = name, InputJson = input.GetRawText(), Summary = summary } });
                return new("Acción preparada y PENDIENTE DE CONFIRMACIÓN del usuario (verá un botón para aprobarla). No la des por realizada: dile que la revise y confirme.");
            }
        }
        try
        {
            var output = name switch
            {
                "documentos_buscar" => await DocsSearchAsync(Str(input, "texto"), Str(input, "tipo"), Str(input, "tipoRegistro"), Str(input, "idRegistro")),
                "documento_obtener" => await DocGetAsync(Str(input, "id")),
                "documentos_plantillas" => Ser((await Docs.GetAllTemplatesAsync()).Select(t => new { t.Id, t.Title, t.Kind, t.Description, propia = DocumentService.FindTemplate(t.Id) is null })),
                "documento_crear" => await DocCreateAsync(input, author),
                "documento_actualizar" => await DocUpdateAsync(input, author),
                "documento_importar" => await DocImportAsync(input, author),
                "adjuntos_listar" => await AttachmentsAsync(Str(input, "tipo"), Str(input, "id"), ctx),
                "adjunto_leer" => await AttachmentReadAsync(Str(input, "id")),
                "enviar_correo" => await SendEmailAsync(input, author),
                "enviar_mensaje" => await SendMessageAsync(input, author),
                "notificar_usuario" => await NotifyAsync(input, author),
                "recordar" => await RememberAsync(ctx, Str(input, "texto"), Str(input, "alcance")),
                "olvidar" => await ForgetAsync(ctx, Str(input, "id")),
                "cotizaciones_buscar" => await QuotesSearchAsync(Str(input, "texto"), Str(input, "estado"), Str(input, "dealId"), Int(input, "limite", 20)),
                "cotizacion_obtener" => await QuoteGetAsync(Str(input, "id")),
                "cotizacion_crear" => await QuoteCreateAsync(input, author),
                "cotizacion_actualizar" => await QuoteUpdateAsync(input, author),
                "consultar_agente" => await AskAgentAsync(ctx, Str(input, "agente"), Str(input, "mensaje")),
                "buscar_web" => Ser(await _sp.GetRequiredService<WebSearchService>().SearchAsync(Str(input, "consulta"), Int(input, "limite", 6))),
                "leer_pagina" => await _sp.GetRequiredService<WebSearchService>().ReadAsync(Str(input, "url")),
                "buscar" => await SearchAsync(Str(input, "tipo"), Str(input, "texto"), Int(input, "limite", 10)),
                "obtener" => await GetAsync(Str(input, "tipo"), Str(input, "id")),
                "campos" => await FieldsAsync(Str(input, "tipo")),
                "estadisticas" => await StatsAsync(),
                "priorizar" => await PrioritizeAsync(Str(input, "tipo"), Int(input, "limite", 10)),
                "resumen_embudo" => await PipelineSummaryAsync(Str(input, "pipelineId")),
                "listar_embudos" => await ListPipelinesAsync(),
                "listar_usuarios" => Ser((await _users.GetAllAsync()).Where(u => u.IsActive).Select(u => new { u.Name, u.Email, u.Role })),
                "crear" => await CreateAsync(Str(input, "tipo"), Obj(input, "datos"), author),
                "actualizar" => await UpdateAsync(Str(input, "tipo"), Str(input, "id"), Obj(input, "datos"), author),
                "agregar_nota" => await NoteAsync(Str(input, "tipo"), Str(input, "id"), Str(input, "texto"), author),
                "convertir" => await ConvertLeadAsync(input, author),
                "archivar" => await ArchiveAsync(Str(input, "tipo"), Str(input, "id"), Bool(input, "archivar", true), author),
                "eliminar" => await DeleteRecordAsync(Str(input, "tipo"), Str(input, "id"), author),
                "emitir_factura" => await EmitInvoiceAsync(input, author),
                "crear_enlace_pago" => await CreatePaymentLinkAsync(input, author),
                "solicitar_firma" => await RequestSignatureAsync(input, author),
                _ => throw new InvalidOperationException("Herramienta no implementada")
            };
            return new(output);
        }
        catch (Exception ex) { return new("Error: " + ex.Message, true); }
    }

    // ---------- lectura ----------
    private async Task<string> SearchAsync(string tipo, string? texto, int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        var q = (texto ?? "").Trim();
        bool M(params string?[] fields) => q.Length == 0 || fields.Any(f => f is not null && f.Contains(q, StringComparison.OrdinalIgnoreCase));
        switch (tipo)
        {
            case "lead":
                return Ser((await _leads.GetAllAsync()).Where(l => M(l.Title, l.Name, l.LastName, l.CompanyName, l.Email, l.Phone)).Take(limit)
                    .Select(l => new { l.Id, l.Title, Nombre = l.FullName, l.CompanyName, l.Email, l.Phone, l.Status, l.Opportunity, l.Currency, l.AssignedTo, l.Source, l.CreatedAt }));
            case "contact":
                return Ser((await _contacts.GetAllAsync()).Where(c => M(c.Name, c.LastName, c.Email, c.Phone, c.Position)).Take(limit)
                    .Select(c => new { c.Id, Nombre = c.FullName, c.Email, c.Phone, c.Position, c.CompanyId, c.Type, c.AssignedTo }));
            case "company":
                return Ser((await _companies.GetAllAsync()).Where(k => M(k.Title, k.Industry, k.Email, k.Web, k.TaxId)).Take(limit)
                    .Select(k => new { k.Id, k.Title, k.Industry, k.Email, k.Phone, k.Web, k.Type, k.AssignedTo }));
            case "deal":
            {
                var pipes = await _pipelines.GetAllAsync();
                return Ser((await _deals.GetAllAsync()).Where(d => M(d.Title, d.Comments)).Take(limit)
                    .Select(d => new { d.Id, d.Title, d.Amount, d.Currency, d.Probability, Pipeline = pipes.FirstOrDefault(p => p.Id == d.PipelineId)?.Name, Stage = pipes.FirstOrDefault(p => p.Id == d.PipelineId)?.GetStage(d.StageId)?.Name, d.StageId, d.PipelineId, d.ContactId, d.CompanyId, d.ExpectedCloseDate, d.ClosedAt, d.AssignedTo }));
            }
            case "task":
                return Ser((await _tasks.GetAllAsync()).Where(t => M(t.Title, t.Notes, t.EntityTitle)).OrderBy(t => t.IsDone).ThenBy(t => t.DueAt).Take(limit)
                    .Select(t => new { t.Id, t.Title, t.Kind, t.IsDone, t.DueAt, Vencida = t.IsOverdue, t.AssignedTo, t.EntityType, t.EntityId, t.EntityTitle }));
            case "product":
                return Ser((await _products.GetAllAsync()).Where(p => M(p.Name, p.Sku, p.Description, p.Category) || p.Tags.Any(t => M(t))).Take(limit)
                    .Select(p => new { p.Id, p.Name, p.Sku, p.Category, p.Tags, p.Price, p.Currency, p.Unit, p.Stock, p.IsActive }));
            default: throw new ArgumentException("Tipo no válido: " + tipo);
        }
    }

    private async Task<string> GetAsync(string tipo, string id)
    {
        object? e = tipo switch
        {
            "lead" => await _leads.GetAsync(id),
            "contact" => await _contacts.GetByIdAsync(id),
            "company" => await _companies.GetByIdAsync(id),
            "deal" => await _deals.GetAsync(id),
            "task" => await _tasks.GetAsync(id),
            "product" => await _products.GetByIdAsync(id),
            _ => throw new ArgumentException("Tipo no válido: " + tipo)
        };
        if (e is null) return "No existe un registro " + tipo + " con id " + id;
        if (e is Entity ent && tipo is "lead" or "contact" or "company" or "deal")
        {
            var acts = (await _activities.GetForAsync(tipo, id)).Take(10).Select(a => new { a.CreatedAt, a.Kind, a.Text, a.CreatedBy });
            var tasks = (await _tasks.GetForEntityAsync(tipo, id)).Select(t => new { t.Id, t.Title, t.Kind, t.IsDone, t.DueAt, t.AssignedTo });
            return Ser(new { registro = e, actividades = acts, tareas = tasks, enlace = CrmEntities.LinkOf(ent) });
        }
        return Ser(e);
    }

    private async Task<string> StatsAsync()
    {
        var s = await _settings.GetAsync();
        var leads = await _leads.GetAllAsync();
        var deals = await _deals.GetAllAsync();
        var pipes = await _pipelines.GetAllAsync();
        var tasks = await _tasks.GetAllAsync();
        StageKind KindOf(Deal d) => pipes.FirstOrDefault(p => p.Id == d.PipelineId)?.GetStage(d.StageId)?.Kind ?? StageKind.Process;
        var open = deals.Where(d => KindOf(d) == StageKind.Process).ToList();
        var won = deals.Where(d => KindOf(d) == StageKind.Success).ToList();
        var lost = deals.Where(d => KindOf(d) == StageKind.Failure).ToList();
        var month = DateTime.UtcNow.AddDays(-30);
        return Ser(new
        {
            monedaBase = s.BaseCurrency,
            prospectos = new { total = leads.Count, porEstado = leads.GroupBy(l => LeadStatuses.Get(l.Status).Name).ToDictionary(g => g.Key, g => g.Count()), nuevosUltimos30Dias = leads.Count(l => l.CreatedAt > month) },
            negociaciones = new
            {
                abiertas = open.Count, montoAbierto = open.Sum(d => s.ToBase(d.Amount, d.Currency)),
                ganadas = won.Count, montoGanado = won.Sum(d => s.ToBase(d.Amount, d.Currency)), ganadasUltimos30Dias = won.Count(d => d.ClosedAt > month),
                perdidas = lost.Count, montoPerdido = lost.Sum(d => s.ToBase(d.Amount, d.Currency)),
                ponderadoAbierto = open.Sum(d => s.ToBase(d.Amount, d.Currency) * d.Probability / 100m),
            },
            contactos = await _contacts.CountAsync(),
            companias = await _companies.CountAsync(),
            tareas = new { pendientes = tasks.Count(t => !t.IsDone), vencidas = tasks.Count(t => t.IsOverdue), completadas = tasks.Count(t => t.IsDone) },
        });
    }

    private async Task<string> PipelineSummaryAsync(string? pipelineId)
    {
        var s = await _settings.GetAsync();
        var pipe = string.IsNullOrEmpty(pipelineId) ? await _pipelines.GetDefaultAsync() : await _pipelines.GetAsync(pipelineId) ?? throw new ArgumentException("Embudo no encontrado");
        var deals = await _deals.GetByPipelineAsync(pipe.Id!);
        return Ser(new
        {
            embudo = new { pipe.Id, pipe.Name },
            etapas = pipe.OrderedStages.Select(st =>
            {
                var ds = deals.Where(d => d.StageId == st.Id).ToList();
                return new { st.Id, st.Name, tipo = st.Kind.ToString(), cantidad = ds.Count, monto = ds.Sum(d => s.ToBase(d.Amount, d.Currency)), negociaciones = ds.Take(20).Select(d => new { d.Id, d.Title, d.Amount, d.Currency, d.AssignedTo, d.ExpectedCloseDate }) };
            })
        });
    }

    private async Task<string> ListPipelinesAsync() =>
        Ser((await _pipelines.GetAllAsync()).Select(p => new { p.Id, p.Name, p.IsDefault, etapas = p.OrderedStages.Select(s => new { s.Id, s.Name, tipo = s.Kind.ToString() }) }));

    // ---------- escritura ----------
    private async Task<string> CreateAsync(string tipo, JsonElement datos, string author)
    {
        switch (tipo)
        {
            case "lead":
            {
                var l = new Lead { CreatedBy = author };
                Apply(l, datos);
                if (string.IsNullOrWhiteSpace(l.Title)) l.Title = string.Join(" ", new[] { l.FullName, l.CompanyName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (string.IsNullOrWhiteSpace(l.Title)) throw new ArgumentException("title es obligatorio");
                var lcf = await ApplyCustomAsync(l, "lead", datos);
                await _leads.SaveAsync(l, author);
                return Ser(new { ok = true, id = l.Id, enlace = CrmEntities.LinkOf(l), camposPersonalizados = lcf });
            }
            case "contact":
            {
                var c = new Contact { CreatedBy = author }; Apply(c, datos);
                if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("name es obligatorio");
                var ccf = await ApplyCustomAsync(c, "contact", datos);
                await _contacts.InsertAsync(c);
                return Ser(new { ok = true, id = c.Id, enlace = CrmEntities.LinkOf(c), camposPersonalizados = ccf });
            }
            case "company":
            {
                var k = new Company { CreatedBy = author }; Apply(k, datos);
                if (string.IsNullOrWhiteSpace(k.Title)) throw new ArgumentException("title es obligatorio");
                var kcf = await ApplyCustomAsync(k, "company", datos);
                await _companies.InsertAsync(k);
                return Ser(new { ok = true, id = k.Id, enlace = CrmEntities.LinkOf(k), camposPersonalizados = kcf });
            }
            case "deal":
            {
                var d = new Deal { CreatedBy = author }; Apply(d, datos);
                if (string.IsNullOrWhiteSpace(d.Title)) throw new ArgumentException("title es obligatorio");
                if (string.IsNullOrWhiteSpace(d.PipelineId)) d.PipelineId = (await _pipelines.GetDefaultAsync()).Id!;
                var dcf = await ApplyCustomAsync(d, "deal", datos);
                await _deals.SaveAsync(d, author);
                return Ser(new { ok = true, id = d.Id, enlace = CrmEntities.LinkOf(d), camposPersonalizados = dcf });
            }
            case "product":
            {
                var p = new Product { CreatedBy = author }; Apply(p, datos);
                if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("name es obligatorio");
                await _products.InsertAsync(p);
                return Ser(new { ok = true, id = p.Id, enlace = "productos" });
            }
            case "task":
            {
                var t = new TaskItem { CreatedBy = author, AssignedTo = author }; Apply(t, datos);
                if (string.IsNullOrWhiteSpace(t.Title)) throw new ArgumentException("title es obligatorio");
                if (t.EntityType is not null && t.EntityId is not null && string.IsNullOrEmpty(t.EntityTitle))
                    t.EntityTitle = await TitleOfAsync(t.EntityType, t.EntityId);
                await _tasks.SaveAsync(t, author);
                return Ser(new { ok = true, id = t.Id, enlace = "mi-trabajo" });
            }
            default: throw new ArgumentException("Tipo no válido: " + tipo);
        }
    }

    private async Task<string> UpdateAsync(string tipo, string id, JsonElement datos, string author)
    {
        switch (tipo)
        {
            case "lead": { var l = await _leads.GetAsync(id) ?? throw new ArgumentException("Prospecto no encontrado"); Apply(l, datos); var cf = await ApplyCustomAsync(l, "lead", datos); await _leads.SaveAsync(l, author); return Ser(new { ok = true, id, enlace = CrmEntities.LinkOf(l), camposPersonalizados = cf }); }
            case "contact": { var c = await _contacts.GetByIdAsync(id) ?? throw new ArgumentException("Contacto no encontrado"); Apply(c, datos); var cf = await ApplyCustomAsync(c, "contact", datos); c.UpdatedBy = author; await _contacts.UpdateAsync(c); return Ser(new { ok = true, id, camposPersonalizados = cf }); }
            case "company": { var k = await _companies.GetByIdAsync(id) ?? throw new ArgumentException("Compañía no encontrada"); Apply(k, datos); var cf = await ApplyCustomAsync(k, "company", datos); k.UpdatedBy = author; await _companies.UpdateAsync(k); return Ser(new { ok = true, id, camposPersonalizados = cf }); }
            case "deal": { var d = await _deals.GetAsync(id) ?? throw new ArgumentException("Negociación no encontrada"); Apply(d, datos); var cf = await ApplyCustomAsync(d, "deal", datos); await _deals.SaveAsync(d, author); return Ser(new { ok = true, id, enlace = CrmEntities.LinkOf(d), camposPersonalizados = cf }); }
            case "task": { var t = await _tasks.GetAsync(id) ?? throw new ArgumentException("Tarea no encontrada"); Apply(t, datos); await _tasks.SaveAsync(t, author); return Ser(new { ok = true, id }); }
            case "product": { var p = await _products.GetByIdAsync(id) ?? throw new ArgumentException("Producto no encontrado"); Apply(p, datos); p.UpdatedBy = author; await _products.UpdateAsync(p); return Ser(new { ok = true, id, enlace = "productos" }); }
            default: throw new ArgumentException("Tipo no válido: " + tipo);
        }
    }

    private async Task<string> NoteAsync(string tipo, string id, string texto, string author)
    {
        var title = await TitleOfAsync(tipo, id);
        if (title is null) throw new ArgumentException("Registro no encontrado");
        await _activities.LogAsync(tipo, id, title, ActivityKinds.Note, texto, author);
        return Ser(new { ok = true });
    }

    // ---------- campos personalizados ----------
    /// <summary>Clave de tablero (BoardConfig) para cada tipo que admite columnas personalizadas.</summary>
    private static string? BoardKeyOf(string tipo) => tipo switch { "lead" => "leads", "contact" => "contacts", "company" => "companies", "deal" => "deals", _ => null };

    /// <summary>Campos estándar escribibles por tipo (los que acepta crear/actualizar fuera de 'custom').</summary>
    private static string[] StandardFields(string tipo) => tipo switch
    {
        "lead" => new[] { "title", "name", "lastName", "companyName", "position", "email", "phone", "source", "status", "opportunity", "currency", "assignedTo", "comments" },
        "contact" => new[] { "name", "lastName", "email", "phone", "position", "companyId", "type", "assignedTo", "comments" },
        "company" => new[] { "title", "industry", "email", "phone", "web", "address", "taxId", "type", "annualRevenue", "employees", "assignedTo", "comments" },
        "deal" => new[] { "title", "amount", "currency", "pipelineId", "stageId", "contactId", "companyId", "probability", "expectedCloseDate", "assignedTo", "comments" },
        _ => Array.Empty<string>()
    };

    private async Task<string> FieldsAsync(string tipo)
    {
        var board = BoardKeyOf(tipo) ?? throw new ArgumentException("Tipo no válido (solo lead, contact, company o deal): " + tipo);
        var cfg = await _boards.GetAsync(board);
        return Ser(new
        {
            tipo,
            camposEstandar = StandardFields(tipo),
            camposPersonalizados = cfg.CustomColumns.Select(c => new { c.Id, titulo = c.Title, tipo = c.Type, opciones = c.Options.Count == 0 ? null : c.Options.Select(o => o.Label) }),
            nota = "Para escribir un campo personalizado, usa el objeto 'custom' en crear/actualizar con el título del campo como clave. Los títulos que no existan se crearán como campo nuevo del tablero."
        });
    }

    /// <summary>Aplica el objeto 'custom' de los datos a Entity.Custom, reutilizando columnas por título/id y creando las que falten. Devuelve el detalle o null.</summary>
    private async Task<object?> ApplyCustomAsync(Entity entity, string tipo, JsonElement datos)
    {
        if (datos.ValueKind != JsonValueKind.Object || !datos.TryGetProperty("custom", out var custom) || custom.ValueKind != JsonValueKind.Object) return null;
        var board = BoardKeyOf(tipo);
        if (board is null) return null;
        var cfg = await _boards.GetAsync(board);
        var escritos = new List<string>();
        var aEstandar = new List<string>();
        var creados = new List<string>();
        var ignorados = new List<string>();
        var boardChanged = false;
        foreach (var p in custom.EnumerateObject())
        {
            var key = p.Name.Trim();
            if (key.Length == 0) continue;

            // 1) Si la clave corresponde a un campo ESTÁNDAR (p. ej. "Correo", "Teléfono", "Sitio web"),
            //    se escribe en ese campo del registro en vez de crear un campo personalizado duplicado.
            var std = MapToStandard(tipo, key);
            if (std is not null)
            {
                Apply(entity, JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [std] = p.Value }));
                aEstandar.Add($"{key} → {std}");
                continue;
            }

            // 2) Campo personalizado ya existente (por id o título): se reutiliza.
            var def = cfg.CustomColumns.FirstOrDefault(c => c.Id == key || c.Title.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (def is null)
            {
                // 3) No existe: se crea como campo nuevo del tablero (salvo que el valor venga vacío).
                var preview = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                if (string.IsNullOrWhiteSpace(preview)) { ignorados.Add(key); continue; }
                def = new CustomColumnDef { Title = key, Type = (preview!.Length > 80 ? "LongText" : "Text"), Width = preview.Length > 80 ? 220 : 150 };
                cfg.CustomColumns.Add(def);
                creados.Add(def.Title);
                boardChanged = true;
            }
            var serialized = SerializeCustomValue(def, p.Value);
            if (serialized is null) entity.Custom.Remove(def.Id);
            else entity.Custom[def.Id] = serialized;
            escritos.Add(def.Title);
        }
        if (boardChanged) await _boards.SaveAsync(cfg);
        if (escritos.Count == 0 && aEstandar.Count == 0 && ignorados.Count == 0) return null;
        return new
        {
            escritos = escritos.Count == 0 ? null : escritos,
            campoEstandar = aEstandar.Count == 0 ? null : aEstandar,
            creados = creados.Count == 0 ? null : creados,
            ignorados = ignorados.Count == 0 ? null : ignorados,
            aviso = aEstandar.Count > 0 ? "Algunos datos se guardaron en campos estándar del registro en vez de crear campos personalizados." : null
        };
    }

    /// <summary>Normaliza un texto para comparar nombres de campo: minúsculas, sin acentos ni signos.</summary>
    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Mapea una clave de 'custom' a un campo estándar del tipo, si corresponde (incluye sinónimos en español). Devuelve el nombre del campo o null.</summary>
    private static string? MapToStandard(string tipo, string key)
    {
        var n = Normalize(key);
        if (n.Length == 0) return null;
        var fields = StandardFields(tipo);
        // coincidencia directa con el nombre del campo (email, phone, web…)
        var direct = fields.FirstOrDefault(f => Normalize(f) == n);
        if (direct is not null) return direct;
        // sinónimos → campo canónico
        var canonical = n switch
        {
            "correo" or "correoelectronico" or "email" or "mail" or "ecorreo" or "correodecontacto" => "email",
            "telefono" or "tel" or "celular" or "movil" or "numerodetelefono" or "numero" or "telefonodecontacto" or "whatsapp" => "phone",
            "sitioweb" or "web" or "paginaweb" or "pagina" or "url" or "website" or "sitio" => "web",
            "direccion" or "domicilio" or "ubicacion" or "address" or "direccionfisica" => "address",
            "industria" or "sector" or "rubro" or "giro" or "actividad" or "industry" => "industry",
            "empresa" or "compania" or "nombredelaempresa" or "razonsocial" or "companyname" => "companyName",
            "cargo" or "puesto" or "posicion" or "position" => "position",
            "ruc" or "cedulajuridica" or "nit" or "taxid" or "identificacionfiscal" or "cedula" or "rfc" => "taxId",
            "ingresos" or "ingresosanuales" or "facturacion" or "annualrevenue" or "ventasanuales" => "annualRevenue",
            "empleados" or "numerodeempleados" or "employees" or "colaboradores" or "personal" => "employees",
            "comentarios" or "notas" or "observaciones" or "comments" => "comments",
            "origen" or "fuente" or "source" => "source",
            "moneda" or "currency" => "currency",
            "nombre" or "titulo" or "title" => fields.Contains("title") ? "title" : null,
            _ => null
        };
        return canonical is not null && fields.Contains(canonical) ? canonical : null;
    }

    /// <summary>Serializa un valor JSON al texto que guarda Entity.Custom, según el tipo de columna (igual que el tablero).</summary>
    private static string? SerializeCustomValue(CustomColumnDef def, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;
        var raw = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.GetRawText();
        switch (def.Type)
        {
            case "Number":
            case "Money":
                if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal().ToString(CultureInfo.InvariantCulture);
                return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : null;
            case "Date":
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt.ToString("yyyy-MM-dd") : null;
            case "Checkbox":
                var b = v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(raw, out var bb) && bb);
                return b ? "true" : "false";
            case "Status":
            case "Select":
                var opt = def.Options.FirstOrDefault(o => o.Value.Equals(raw, StringComparison.OrdinalIgnoreCase) || o.Label.Equals(raw, StringComparison.OrdinalIgnoreCase));
                return opt?.Value ?? (string.IsNullOrWhiteSpace(raw) ? null : raw);
            default:
                return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
    }

    // ---------- cotizaciones ----------
    private async Task<string> QuotesSearchAsync(string? texto, string? estado, string? dealId, int limit)
    {
        var q = (texto ?? "").Trim();
        var all = await _quotes.GetAllAsync();
        var list = all.Where(x => (string.IsNullOrEmpty(estado) || x.Status == estado) && (string.IsNullOrEmpty(dealId) || x.DealId == dealId)
            && (q.Length == 0 || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Number.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Date).Take(Math.Clamp(limit, 1, 100));
        return Ser(list.Select(x => new { x.Id, x.Number, x.Title, x.Status, estadoNombre = QuoteStatuses.Get(x.Status).Label, x.Currency, total = x.Total, x.Date, x.ValidUntil, vencida = x.ValidUntil < DateTime.UtcNow.Date && x.Status is "draft" or "sent", lineas = x.Lines.Count, x.DealId, x.PreparedBy, enlace = $"cotizaciones/{x.Id}" }));
    }

    private async Task<Quote> FindQuoteAsync(string id)
    {
        var q = await _quotes.GetAsync(id) ?? (await _quotes.GetAllAsync()).FirstOrDefault(x => x.Number.Equals(id, StringComparison.OrdinalIgnoreCase));
        return q ?? throw new ArgumentException("Cotización no encontrada: " + id);
    }

    private async Task<string> QuoteGetAsync(string id)
    {
        var q = await FindQuoteAsync(id);
        var deal = q.DealId is null ? null : await _deals.GetAsync(q.DealId);
        return Ser(new
        {
            q.Id, q.Number, q.Title, q.Status, estadoNombre = QuoteStatuses.Get(q.Status).Label, q.Currency, q.Date, q.ValidUntil, q.Notes, q.Terms, q.PreparedBy, q.ContactId, q.CompanyId,
            negociacion = deal is null ? null : new { deal.Id, deal.Title, deal.Amount, deal.StageId },
            lineas = q.Lines.Select((l, i) => new { n = i + 1, l.ProductId, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountPercent, l.TaxRate, subtotal = l.Subtotal, total = l.Total }),
            subtotal = q.Subtotal, impuestos = q.Tax, total = q.Total, enlace = $"cotizaciones/{q.Id}"
        });
    }

    private async Task<List<QuoteLine>> LinesFromAsync(JsonElement lines, string currency)
    {
        var result = new List<QuoteLine>();
        if (lines.ValueKind != JsonValueKind.Array) return result;
        foreach (var l in lines.EnumerateArray())
        {
            var line = new QuoteLine { Quantity = 1 };
            var pid = Str(l, "productId");
            if (pid.Length > 0)
            {
                var p = await _products.GetByIdAsync(pid) ?? (await _products.GetAllAsync()).FirstOrDefault(x => x.Name.Equals(pid, StringComparison.OrdinalIgnoreCase) || (x.Sku ?? "").Equals(pid, StringComparison.OrdinalIgnoreCase));
                if (p is null) throw new ArgumentException("Producto no encontrado: " + pid);
                line.ProductId = p.Id; line.Description = p.Name; line.Unit = p.Unit; line.UnitPrice = p.Price; line.TaxRate = p.TaxRate;
            }
            Apply(line, l);
            if (string.IsNullOrWhiteSpace(line.Description)) throw new ArgumentException("Cada línea necesita productId o description");
            result.Add(line);
        }
        return result;
    }

    private async Task<string> QuoteCreateAsync(JsonElement input, string author)
    {
        var s = await _settings.GetAsync();
        var deal = Str(input, "dealId") is { Length: > 0 } did ? await _deals.GetAsync(did) ?? throw new ArgumentException("Negociación no encontrada") : null;
        var q = new Quote
        {
            Number = s.QuotePrefix + (await _settings.NextQuoteNumberAsync()).ToString("D4"),
            Title = Str(input, "title") is { Length: > 0 } t ? t : deal?.Title ?? "Cotización",
            DealId = deal?.Id, ContactId = Str(input, "contactId") is { Length: > 0 } c ? c : deal?.ContactId, CompanyId = Str(input, "companyId") is { Length: > 0 } k ? k : deal?.CompanyId,
            Currency = Str(input, "currency") is { Length: > 0 } cur ? cur : deal?.Currency ?? s.BaseCurrency,
            ValidUntil = DateTime.UtcNow.Date.AddDays(Int(input, "validDays", s.QuoteValidDays)), Terms = s.QuoteTerms, PreparedBy = author, CreatedBy = author,
            Notes = Str(input, "notes") is { Length: > 0 } n ? n : null,
        };
        q.Lines = await LinesFromAsync(Obj(input, "lines"), q.Currency);
        if (q.Lines.Count == 0) throw new ArgumentException("La cotización necesita al menos una línea");
        await _quoteRepo.InsertAsync(q);
        if (deal is not null) await _activities.LogAsync("deal", deal.Id!, deal.Title, ActivityKinds.System, $"📄 Creó la cotización {q.Number} ({q.Total:N2} {q.Currency})", author);
        return Ser(new { ok = true, id = q.Id, numero = q.Number, total = q.Total, q.Currency, enlace = $"cotizaciones/{q.Id}" });
    }

    private async Task<string> QuoteUpdateAsync(JsonElement input, string author)
    {
        var q = await FindQuoteAsync(Str(input, "id"));
        if (Str(input, "title") is { Length: > 0 } t) q.Title = t;
        if (input.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String) q.Notes = n.GetString();
        if (Str(input, "validUntil") is { Length: > 0 } v && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) q.ValidUntil = d;
        if (input.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array) q.Lines = await LinesFromAsync(lines, q.Currency);
        await _quotes.SaveAsync(q, author);
        var status = Str(input, "status");
        if (status.Length > 0 && status != q.Status)
        {
            if (QuoteStatuses.All.All(x => x.Id != status)) throw new ArgumentException("Estado no válido: " + status);
            await _quotes.SetStatusAsync(q, status, author);
        }
        return Ser(new { ok = true, id = q.Id, numero = q.Number, q.Status, total = q.Total, enlace = $"cotizaciones/{q.Id}" });
    }

    // ---------- documentos y adjuntos ----------
    private DocumentService Docs => _sp.GetRequiredService<DocumentService>();
    private AttachmentService Files => _sp.GetRequiredService<AttachmentService>();

    private async Task<string> DocsSearchAsync(string? texto, string? tipo, string? et, string? eid)
    {
        var q = (texto ?? "").Trim();
        var list = (await Docs.GetAllAsync()).Where(d => (string.IsNullOrEmpty(tipo) || d.Kind == tipo) && (string.IsNullOrEmpty(et) || d.EntityType == et) && (string.IsNullOrEmpty(eid) || d.EntityId == eid)
            && (q.Length == 0 || d.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || (d.EntityTitle ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || d.Content.Contains(q, StringComparison.OrdinalIgnoreCase))).Take(30);
        return Ser(list.Select(d => new { d.Id, d.Title, d.Kind, tipoNombre = DocumentKinds.Get(d.Kind).Label, d.Status, estadoNombre = DocumentStatuses.Get(d.Status).Label, version = d.Version, d.EntityType, d.EntityId, d.EntityTitle, d.UpdatedAt, d.Author, enlace = $"documentos/{d.Id}", word = $"documentos/{d.Id}/word" }));
    }

    private async Task<string> DocGetAsync(string id)
    {
        var d = await Docs.GetAsync(id) ?? throw new ArgumentException("Documento no encontrado");
        return Ser(new { d.Id, d.Title, d.Kind, d.Status, version = d.Version, d.EntityType, d.EntityId, d.EntityTitle, d.Author, d.UpdatedAt, contenido = d.Content, versiones = d.Versions.Select(v => new { v.Number, v.At, v.By, v.Note }), enlace = $"documentos/{d.Id}", word = $"documentos/{d.Id}/word" });
    }

    private async Task<string> DocCreateAsync(JsonElement input, string author)
    {
        var tipo = Str(input, "tipo"); if (tipo.Length == 0) tipo = "otro";
        var et = Str(input, "tipoRegistro"); var eid = Str(input, "idRegistro");
        var content = Str(input, "contenido");
        var tpl = Str(input, "plantilla");
        if (content.Length == 0)
        {
            var t = await Docs.FindTemplateAsync(tpl.Length > 0 ? tpl : tipo) ?? throw new ArgumentException("Indique contenido o una plantilla válida (nda, contrato, propuesta, carta, acta o el nombre de una plantilla propia)");
            content = t.Body;
        }
        content = await Docs.FillAsync(content, et.Length > 0 ? et : null, eid.Length > 0 ? eid : null, author.Split(" · ")[0], Str(input, "asunto") is { Length: > 0 } a ? a : null);
        var d = new CrmDocument { Title = Str(input, "titulo"), Kind = tipo, Content = content, EntityType = et.Length > 0 ? et : null, EntityId = eid.Length > 0 ? eid : null, Author = author };
        if (string.IsNullOrWhiteSpace(d.Title)) throw new ArgumentException("titulo es obligatorio");
        if (d.EntityType is not null && d.EntityId is not null && await Docs.EntityTitleAsync(d.EntityType, d.EntityId) is null) throw new ArgumentException("El registro indicado no existe");
        await Docs.CreateAsync(d, author);
        return Ser(new { ok = true, id = d.Id, version = d.Version, enlace = $"documentos/{d.Id}", word = $"documentos/{d.Id}/word", nota = "Documento creado. Indique al usuario el enlace para abrirlo, editarlo o descargarlo en Word." });
    }

    private async Task<string> DocUpdateAsync(JsonElement input, string author)
    {
        var d = await Docs.GetAsync(Str(input, "id")) ?? throw new ArgumentException("Documento no encontrado");
        if (d.Status == "signed" && Str(input, "contenido").Length > 0) throw new InvalidOperationException("Un documento firmado no se puede modificar; cree uno nuevo.");
        if (Str(input, "titulo") is { Length: > 0 } t) d.Title = t;
        if (Str(input, "estado") is { Length: > 0 } st) { if (DocumentStatuses.All.All(x => x.Id != st)) throw new ArgumentException("Estado no válido"); d.Status = st; }
        if (Str(input, "contenido") is { Length: > 0 } c) d.Content = c;
        await Docs.SaveAsync(d, author, Str(input, "nota") is { Length: > 0 } n ? n : "Actualizado por " + author);
        return Ser(new { ok = true, id = d.Id, version = d.Version, d.Status, enlace = $"documentos/{d.Id}" });
    }

    private async Task<string> DocImportAsync(JsonElement input, string author)
    {
        var a = await Files.GetAsync(Str(input, "adjuntoId")) ?? throw new ArgumentException("Adjunto no encontrado");
        await using var fs = File.OpenRead(Files.PathFor(a));
        var content = DocumentService.ImportToMarkdown(a.FileName, fs);
        var et = Str(input, "tipoRegistro"); var eid = Str(input, "idRegistro");
        var d = new CrmDocument { Title = Str(input, "titulo") is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(a.FileName), Kind = Str(input, "tipo") is { Length: > 0 } k ? k : "otro", Content = content, EntityType = et.Length > 0 ? et : null, EntityId = eid.Length > 0 ? eid : null, Author = author, Notes = "Importado de " + a.FileName };
        await Docs.CreateAsync(d, author);
        return Ser(new { ok = true, id = d.Id, caracteres = content.Length, enlace = $"documentos/{d.Id}" });
    }

    private async Task<string> AttachmentsAsync(string tipo, string id, ToolContext ctx)
    {
        if (tipo == "conversation" && ctx.Conversation is not null && id.Length == 0) id = ctx.Conversation.Id!;
        var list = await Files.GetForAsync(tipo, id);
        return Ser(list.Select(a => new { a.Id, a.FileName, a.ContentType, tamano = a.SizeText, a.CreatedAt, a.UploadedBy, enlace = $"files/{a.Id}" }));
    }

    private async Task<string> AttachmentReadAsync(string id)
    {
        var a = await Files.GetAsync(id) ?? throw new ArgumentException("Adjunto no encontrado");
        var text = await Docs.ExtractTextAsync(a);
        return Ser(new { a.Id, a.FileName, texto = text });
    }

    // ---------- mensajería y memoria ----------
    private static string Summarize(string tool, JsonElement input) => tool switch
    {
        "enviar_correo" => $"📧 Enviar correo a {Str(input, "para")} · asunto: \"{Str(input, "asunto")}\"{(Str(input, "documentoId").Length > 0 ? " · adjunto: documento " + Str(input, "documentoId") + (Str(input, "formato") == "word" ? " (Word)" : " (PDF)") : "")}{(Str(input, "adjuntoId").Length > 0 ? " · adjunto: archivo " + Str(input, "adjuntoId") : "")}\n\n{Str(input, "cuerpo")}",
        "enviar_mensaje" => $"💬 Enviar {Str(input, "canal").ToUpperInvariant()} a {Str(input, "numero")}\n\n{Str(input, "texto")}",
        _ => tool + " " + input.GetRawText()
    };

    private async Task<(string type, string id, string title)> EntityRefAsync(JsonElement input, string fallbackTitle)
    {
        var tipo = Str(input, "tipo"); var id = Str(input, "id");
        if (tipo.Length == 0 || id.Length == 0) return ("agent", "agent", fallbackTitle);
        return (tipo, id, await TitleOfAsync(tipo, id) ?? fallbackTitle);
    }

    private async Task<string> SendEmailAsync(JsonElement input, string author)
    {
        var email = _sp.GetRequiredService<EmailService>();
        var (type, id, title) = await EntityRefAsync(input, Str(input, "para"));
        var attachments = new List<EmailService.EmailAttachment>();
        if (Str(input, "documentoId") is { Length: > 0 } did)
        {
            var d = await Docs.GetAsync(did) ?? throw new ArgumentException("Documento no encontrado: " + did);
            var safe = string.Concat(d.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (Str(input, "formato") == "word") attachments.Add(new(safe + ".docx", await Docs.ToDocxAsync(d), "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
            else attachments.Add(new(safe + ".pdf", await Docs.ToPdfAsync(d, _sp.GetRequiredService<IWebHostEnvironment>().WebRootPath), "application/pdf"));
        }
        if (Str(input, "adjuntoId") is { Length: > 0 } aid)
        {
            var a = await Files.GetAsync(aid) ?? throw new ArgumentException("Adjunto no encontrado: " + aid);
            attachments.Add(new(a.FileName, await File.ReadAllBytesAsync(Files.PathFor(a)), a.ContentType));
        }
        var (sent, error) = await email.SendAsync(Str(input, "para"), Str(input, "asunto"), Str(input, "cuerpo"), type, id, title, author, attachments);
        if (error is not null) throw new InvalidOperationException(error);
        return Ser(new { ok = true, enviado = sent, adjuntos = attachments.Select(x => x.Name), nota = sent ? "Correo enviado." : "SMTP no configurado: el correo quedó registrado en la línea de tiempo pero no se envió." });
    }

    private async Task<string> SendMessageAsync(JsonElement input, string author)
    {
        var twilio = _sp.GetService<TwilioMessagingService>() ?? throw new InvalidOperationException("Mensajería no disponible");
        if (!twilio.IsConfigured) throw new InvalidOperationException("Twilio no está configurado (Twilio:AccountSid / AuthToken).");
        var (type, id, title) = await EntityRefAsync(input, Str(input, "numero"));
        var numero = TwilioMessagingService.NormalizePhone(Str(input, "numero")) ?? throw new ArgumentException("Número no válido");
        var (sent, error) = await twilio.SendAsync(Str(input, "canal") == "whatsapp" ? MessageChannels.WhatsApp : MessageChannels.Sms, numero, Str(input, "texto"), type, id, title, author);
        if (error is not null) throw new InvalidOperationException(error);
        return Ser(new { ok = sent });
    }

    private async Task<string> NotifyAsync(JsonElement input, string author)
    {
        var name = Str(input, "usuario");
        var user = (await _users.GetAllAsync()).FirstOrDefault(u => u.IsActive && (u.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || u.Email.Equals(name, StringComparison.OrdinalIgnoreCase)))
            ?? throw new ArgumentException("Usuario no encontrado: " + name);
        await _sp.GetRequiredService<NotificationService>().NotifyAsync(user.Name, NotificationKinds.System, Str(input, "texto"), Str(input, "enlace") is { Length: > 0 } l ? l : null, author);
        return Ser(new { ok = true, usuario = user.Name });
    }

    private async Task<string> RememberAsync(ToolContext ctx, string texto, string alcance)
    {
        if (ctx.Agent is null) throw new InvalidOperationException("Solo dentro de un agente");
        if (string.IsNullOrWhiteSpace(texto)) throw new ArgumentException("texto es obligatorio");
        var mem = await _sp.GetRequiredService<AgentService>().RememberAsync(ctx.Agent, alcance == "equipo" ? null : ctx.User, texto.Trim(), ctx.User);
        return Ser(new { ok = true, id = mem.Id });
    }

    private async Task<string> ForgetAsync(ToolContext ctx, string id)
    {
        if (ctx.Agent is null) throw new InvalidOperationException("Solo dentro de un agente");
        var ok = await _sp.GetRequiredService<AgentService>().ForgetAsync(ctx.Agent, id);
        return Ser(new { ok });
    }

    // ---------- agentes ----------
    private async Task<string> AskAgentAsync(ToolContext ctx, string agente, string mensaje)
    {
        if (ctx.Agent is null) throw new InvalidOperationException("Solo disponible dentro de un agente");
        if (!ctx.Agent.CanCollaborate) throw new InvalidOperationException("Este agente no tiene habilitada la comunicación con otros agentes");
        var svc = _sp.GetRequiredService<AgentService>();
        var reply = await svc.AgentToAgentAsync(ctx.Agent, agente, mensaje, ctx.User ?? ctx.Author);
        return reply;
    }

    private async Task<string?> TitleOfAsync(string tipo, string id) => tipo switch
    {
        "lead" => (await _leads.GetAsync(id))?.Title,
        "contact" => (await _contacts.GetByIdAsync(id))?.FullName,
        "company" => (await _companies.GetByIdAsync(id))?.Title,
        "deal" => (await _deals.GetAsync(id))?.Title,
        _ => null
    };

    /// <summary>Copia las propiedades del JSON (camelCase) sobre el objeto, respetando tipos. Ignora campos de sistema.</summary>
    private static void Apply(object target, JsonElement datos)
    {
        if (datos.ValueKind != JsonValueKind.Object) return;
        var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "createdAt", "updatedAt", "createdBy", "updatedBy", "deletedAt", "deletedBy", "history", "custom" };
        foreach (var p in datos.EnumerateObject())
        {
            if (ignore.Contains(p.Name)) continue;
            var prop = target.GetType().GetProperty(p.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop is null || !prop.CanWrite) continue;
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? v;
            try
            {
                if (p.Value.ValueKind == JsonValueKind.Null) v = null;
                else if (t == typeof(string)) v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                else if (t == typeof(decimal)) v = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDecimal() : decimal.Parse(p.Value.GetString() ?? "0", CultureInfo.InvariantCulture);
                else if (t == typeof(int)) v = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetInt32() : int.Parse(p.Value.GetString() ?? "0", CultureInfo.InvariantCulture);
                else if (t == typeof(bool)) v = p.Value.ValueKind == JsonValueKind.True || (p.Value.ValueKind == JsonValueKind.String && bool.TryParse(p.Value.GetString(), out var b) && b);
                else if (t == typeof(DateTime)) v = DateTime.Parse(p.Value.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                else if (t == typeof(List<string>)) v = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(x => x.ToString()).ToList() : (p.Value.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                else continue;
            }
            catch { continue; }
            if (v is null && !prop.PropertyType.IsValueType) prop.SetValue(target, null);
            else if (v is not null) prop.SetValue(target, v);
        }
    }

    // ---------- convertir / archivar / eliminar ----------
    private async Task<string> ConvertLeadAsync(JsonElement input, string author)
    {
        var lead = await _leads.GetAsync(Str(input, "id")) ?? throw new ArgumentException("Prospecto no encontrado");
        if (lead.Status == LeadStatuses.Converted) throw new ArgumentException("El prospecto ya está convertido");
        var pid = Str(input, "pipelineId");
        var opts = new ConvertOptions(
            Bool(input, "contacto", true),
            Bool(input, "compania", true) && !string.IsNullOrWhiteSpace(lead.CompanyName),
            Bool(input, "negociacion", true),
            string.IsNullOrEmpty(pid) ? null : pid);
        await _leads.ConvertAsync(lead, opts, author);
        return Ser(new { ok = true, contactId = lead.ConvertedContactId, companyId = lead.ConvertedCompanyId, dealId = lead.ConvertedDealId, enlaceNegociacion = lead.ConvertedDealId is null ? null : $"deals/{lead.ConvertedDealId}" });
    }

    private async Task<string> ArchiveAsync(string tipo, string id, bool archive, string author)
    {
        Entity? e = tipo switch
        {
            "lead" => await _leadRepo.GetByIdAsync(id),
            "deal" => await _dealRepo.GetByIdAsync(id),
            "contact" => await _contacts.GetByIdAsync(id),
            "company" => await _companies.GetByIdAsync(id),
            _ => throw new ArgumentException("Tipo no válido para archivar: " + tipo)
        };
        if (e is null) throw new ArgumentException("Registro no encontrado");
        e.IsArchived = archive; e.UpdatedBy = author;
        switch (e)
        {
            case Lead l: await _leadRepo.UpdateAsync(l); break;
            case Deal d: await _dealRepo.UpdateAsync(d); break;
            case Contact c: await _contacts.UpdateAsync(c); break;
            case Company k: await _companies.UpdateAsync(k); break;
        }
        return Ser(new { ok = true, archivado = archive });
    }

    private async Task<string> DeleteRecordAsync(string tipo, string id, string author)
    {
        switch (tipo)
        {
            case "lead": await _leadRepo.DeleteAsync(id, author); break;
            case "deal": await _dealRepo.DeleteAsync(id, author); break;
            case "contact": await _contacts.DeleteAsync(id, author); break;
            case "company": await _companies.DeleteAsync(id, author); break;
            case "product": await _products.DeleteAsync(id, author); break;
            default: throw new ArgumentException("Tipo no válido para eliminar: " + tipo);
        }
        return Ser(new { ok = true, eliminado = true, nota = "Enviado a la papelera (recuperable)." });
    }

    // ---------- facturación electrónica y enlaces de pago ----------
    private async Task<string> EmitInvoiceAsync(JsonElement input, string author)
    {
        var q = await _quotes.GetAsync(Str(input, "quoteId")) ?? throw new ArgumentException("Cotización no encontrada");
        var inv = new ElectronicInvoice { QuoteId = q.Id, DealId = q.DealId, ContactId = q.ContactId, CompanyId = q.CompanyId, Currency = q.Currency };
        var contact = string.IsNullOrEmpty(q.ContactId) ? null : await _contacts.GetByIdAsync(q.ContactId);
        var company = string.IsNullOrEmpty(q.CompanyId) ? null : await _companies.GetByIdAsync(q.CompanyId);
        inv.ReceiverName = company?.Title ?? contact?.FullName ?? "";
        var ced = Str(input, "cedulaReceptor");
        inv.ReceiverId = ced.Length > 0 ? ced : (company?.TaxId ?? "");
        inv.ReceiverEmail = contact?.Email ?? company?.Email;
        foreach (var ln in q.Lines)
        {
            var cabys = "";
            if (!string.IsNullOrEmpty(ln.ProductId)) { var p = await _quotes.GetProductAsync(ln.ProductId); cabys = p?.Cabys ?? ""; }
            inv.Lines.Add(new InvoiceLine { Cabys = cabys, Description = ln.Description, Quantity = ln.Quantity, UnitPrice = ln.UnitPrice, DiscountPercent = ln.DiscountPercent, TaxRate = ln.TaxRate });
        }

        var invoices = _sp.GetRequiredService<Dana.Services.Invoicing.InvoiceService>();
        var (sok, serr) = await invoices.SaveDraftAsync(inv);
        if (!sok) throw new ArgumentException(serr);
        if (!Bool(input, "emitir", true)) return Ser(new { ok = true, id = inv.Id, estado = "draft", total = inv.Total });
        var validation = Dana.Services.Invoicing.InvoiceService.Validate(inv);
        if (validation is not null) return Ser(new { ok = false, id = inv.Id, estado = "draft", error = validation });
        var (ok, err) = await invoices.SubmitAsync(inv.Id!);
        if (!ok) return Ser(new { ok = false, id = inv.Id, error = err });
        await invoices.RefreshStatusAsync(inv.Id!);
        var fresh = await invoices.GetAsync(inv.Id);
        return Ser(new { ok = true, id = inv.Id, estado = fresh?.Status, clave = fresh?.Clave, total = inv.Total, sandbox = invoices.Sandbox });
    }

    private async Task<string> CreatePaymentLinkAsync(JsonElement input, string author)
    {
        var tilopay = _sp.GetRequiredService<Dana.Services.Integrations.TilopayService>();
        var baseUrl = _sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["PublicBaseUrl"] ?? "http://localhost:5025";
        var moneda = Str(input, "moneda"); var deal = Str(input, "dealId"); var quote = Str(input, "quoteId"); var fact = Str(input, "facturaId");
        var link = await tilopay.CreateAsync(new PaymentLink
        {
            Amount = Dec(input, "monto"),
            Currency = moneda.Length > 0 ? moneda : "CRC",
            Description = Str(input, "descripcion"),
            DealId = deal.Length > 0 ? deal : null,
            QuoteId = quote.Length > 0 ? quote : null,
            InvoiceId = fact.Length > 0 ? fact : null,
        }, baseUrl, author);
        return Ser(new { ok = link.Url is not null, url = link.Url, estado = link.Status, sandbox = tilopay.Sandbox, error = link.LastError });
    }

    private async Task<string> PrioritizeAsync(string tipo, int limit)
    {
        limit = Math.Clamp(limit, 1, 30);
        var score = _sp.GetRequiredService<IScoringService>();
        if (tipo == "lead")
        {
            var leads = (await _leads.GetAllAsync()).Where(l => l.Status != LeadStatuses.Converted && l.Status != LeadStatuses.Junk && !l.IsArchived);
            return Ser(leads.Select(l => new { l, sc = score.ScoreLead(l) }).OrderByDescending(x => x.sc.Score).Take(limit)
                .Select(x => new { id = x.l.Id, titulo = x.l.Title, puntuacion = x.sc.Score, nivel = x.sc.Tier, siguienteAccion = x.sc.NextAction, enlace = $"leads/{x.l.Id}" }));
        }
        if (tipo == "deal")
        {
            var pipes = await _pipelines.GetAllAsync();
            var deals = (await _deals.GetAllAsync()).Where(d => !d.IsArchived && d.ClosedAt == null);
            return Ser(deals.Select(d => new { d, sc = score.ScoreDeal(d, pipes.FirstOrDefault(p => p.Id == d.PipelineId)) }).OrderByDescending(x => x.sc.Score).Take(limit)
                .Select(x => new { id = x.d.Id, titulo = x.d.Title, importe = x.d.Amount, puntuacion = x.sc.Score, nivel = x.sc.Tier, siguienteAccion = x.sc.NextAction, enlace = $"deals/{x.d.Id}" }));
        }
        throw new ArgumentException("tipo debe ser 'lead' o 'deal'");
    }

    private async Task<string> RequestSignatureAsync(JsonElement input, string author)
    {
        var docs = _sp.GetRequiredService<DocumentService>();
        var d = await docs.GetAsync(Str(input, "documentoId")) ?? throw new ArgumentException("Documento no encontrado");
        var correo = Str(input, "correo"); var rol = Str(input, "rol");
        var signer = await docs.RequestSignatureAsync(d, Str(input, "nombre"), correo.Length > 0 ? correo : null, rol.Length > 0 ? rol : null, author);
        return Ser(new { ok = true, firmanteId = signer.Id, nombre = signer.Name, enlace = $"firmar/{signer.Token}" });
    }

    private static string Ser(object o) => JsonSerializer.Serialize(o, Json);
    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()) : "";
    private static int Int(JsonElement e, string name, int def) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : def;
    private static JsonElement Obj(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    private static bool Bool(JsonElement e, string name, bool def) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => v.GetString()?.ToLowerInvariant() is "true" or "1" or "sí" or "si", _ => def } : def;
    private static decimal Dec(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;
}
