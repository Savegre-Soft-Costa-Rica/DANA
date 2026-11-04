# Dana · Hoja de ruta (inspirada en monday CRM)

Revisión de las funcionalidades de **monday.com / monday CRM** y plan para incorporarlas por etapas.
Cada etapa deja la app usable y probada. ✅ hecho · 🔧 en curso · ⬜ pendiente.

## Etapa 1 — Tableros estilo monday ✅
- ✅ Shell visual monday: sidebar de espacio de trabajo, barra superior con búsqueda global, menú de usuario, tipografía Figtree, paleta y radios.
- ✅ Componente **Tablero** genérico: grupos de colores plegables, filas con celdas editables en línea (texto, número, dinero, fecha, correo, enlace, texto largo).
- ✅ Columnas **Estado** (pastilla de color con selector tipo monday), **Persona** (avatar + lista de usuarios), **Selección/etiqueta** (contacto, compañía, fuente, tipo…).
- ✅ Fila **+ Agregar elemento** por grupo, fila de **resumen** (sumas, barra de distribución de estados, rango de fechas), ordenación por columna.
- ✅ Vistas **Tabla principal** y **Kanban** (arrastrar y soltar entre grupos) en Prospectos, Negociaciones, Contactos y Compañías.
- ✅ Barra de herramientas: Nuevo, Buscar, filtro por **Persona**, "Solo abiertas" (negociaciones).
- ✅ **Selección masiva**: eliminar o mover de grupo varios elementos.
- ✅ **Panel lateral** del elemento (drawer) sin salir del tablero.
- ✅ **Actualizaciones** (Updates) por elemento: notas de usuario + eventos automáticos (creación, cambio de etapa, conversión).
- ✅ **Panel de inicio** con widgets: números, embudo, batería de estados de prospectos, próximos cierres, actividad reciente.
- ✅ Búsqueda global (`/buscar`).

## Etapa 2 — Personalización de tableros ✅
- ✅ **Columnas personalizadas** por tablero (texto, texto largo, número, dinero, fecha, estado, desplegable, persona, casilla, enlace, correo, teléfono) desde el botón "+" del encabezado. Definición en la colección `boards`, valores en `item.custom`.
- ✅ **Etiquetas editables**: nombre y color de cada estado/etiqueta desde el menú de columna ("Editar etiquetas"); las etiquetas del sistema se pueden renombrar pero no borrar. Las etapas de negociación se editan en Embudos.
- ✅ **Grupos personalizados** en Contactos y Compañías (los grupos son las etiquetas de la columna Tipo, así que agregar/renombrar etiquetas crea/renombra grupos).
- ✅ Menú por columna: ordenar, mover a izquierda/derecha, ocultar, renombrar y eliminar (personalizadas). "Columnas ocultas" en la barra para volver a mostrarlas.
- ✅ **Filtros avanzados** (varias condiciones AND: es, no es, contiene, no contiene, vacío, no vacío, mayor que, menor que) sobre cualquier columna, incluidas las personalizadas.
- ✅ **Vistas guardadas** por tablero (vista Tabla/Kanban, búsqueda, persona, filtros, archivados) como pestañas junto a "Tabla principal".
- ✅ **Duplicar** y **Archivar / Restaurar** elementos (desde la selección masiva o la ficha); los archivados no cuentan en el panel ni en búsquedas.
- ⬜ Pendiente para más adelante: redimensionar columnas arrastrando, filtros con OR, vistas privadas por usuario, mover negociaciones entre embudos en masa.

## Etapa 3 — Actividades y comunicación ✅
- ✅ **Actividades** tipadas (📞 llamada, 📅 reunión, ✉ correo, ☑ tarea) con fecha y hora, duración, responsable, notas y estado hecha/pendiente, ligadas a prospectos, contactos, compañías y negociaciones (tarjeta "Actividades" en cada ficha). Programar y completar se registran en la línea de tiempo.
- ✅ Vista **"Mi trabajo"** (`/mi-trabajo`): vencidas, hoy, esta semana, más adelante, sin fecha y hechas; filtro por persona; crear actividades sueltas. Widget "Mis actividades" en el inicio.
- ✅ **Menciones @usuario** en las actualizaciones (lista desplegable al escribir @) y **campana de notificaciones** en la barra superior (menciones y asignaciones; sondeo cada 30 s; marcar leídas; enlace al elemento).
- ✅ **Adjuntos** en las actualizaciones (hasta 15 MB por archivo, imágenes con miniatura), guardados en `App_Data/uploads` y servidos solo con sesión en `/files/{id}`.
- ✅ **Correo** desde la ficha (contacto, prospecto, compañía y negociación → contacto): redacción con **plantillas** y marcadores (`{{nombre}}`, `{{compañía}}`…), envío por SMTP (`Email:Smtp`, MailKit) y registro en la línea de tiempo; sin SMTP se registra y se puede abrir en el cliente de correo (mailto).
- ✅ Gestión de plantillas en `/plantillas`.
- ⬜ Pendiente para más adelante: recepción de correo (IMAP/Gmail) y sincronización de hilos, calendario compartido de reuniones, recordatorios por correo (Etapa 4).

## Etapa 4 — Automatizaciones e integración ✅
- ✅ **Motor de automatizaciones** (`/automatizaciones`): disparadores *se crea un elemento*, *cambia a una etapa*, *cambia a un estado*, *cambia el responsable*, *vence la fecha de cierre*, *se completa una actividad*; acciones *notificar*, *asignar (persona o rotación)*, *mover a etapa*, *cambiar estado*, *crear actividad*, *enviar correo con plantilla*, *webhook saliente*, *archivar*. Recetas listas para usar, contador de ejecuciones, registro en la línea de tiempo y protección contra bucles.
- ✅ **Recordatorios en segundo plano** (cada minuto): aviso 15 min antes de una actividad, aviso de actividad vencida, aviso + disparador de cierre previsto vencido.
- ✅ **API pública de prospectos** (`/configuracion/api`): canales con clave propia, CORS, orígenes permitidos, mapeo de campos (inglés/español), fuente, estado inicial, asignación (persona o rotación), plantilla de título, notificaciones, solicitud de prueba y ejemplos de código (React fetch y curl). Endpoint `POST /api/public/leads` (alias `/api/webhooks/leads`), JSON o formulario.
- ✅ **Formularios públicos** (`/formularios` → `/formulario/{slug}`): campos configurables, color, mensaje de éxito, honeypot anti-spam, iframe para incrustar.
- ✅ **Tutorial** (`/tutorial`): flujo prospecto → contacto/compañía → negociación, uso de tableros, checklist que se marca sola con los datos reales y preguntas frecuentes. Banner de bienvenida en el inicio y botón "?" en la barra.
- ✅ Sidebar **colapsable** (botón « / ») con tooltips; la preferencia se recuerda.
- ⬜ Pendiente para más adelante: automatizaciones con condiciones adicionales (y/o), programación por fecha ("cada lunes"), recordatorios por correo.

## Etapa 5 — Informes y paneles ✅
- ✅ **Panel de informes configurable** (`/informes`): widgets de número, barras, línea (por mes), dona, embudo y tabla; medida (cantidad, importe, ponderado), agrupación (etapa, estado, responsable, fuente, tipo, mes, embudo, industria, compañía), periodo (30/90 días, año, todo), alcance (abiertas/ganadas/perdidas) y filtro por persona. Mover, redimensionar, configurar y quitar widgets; panel predeterminado restaurable.
- ✅ Widgets especializados: **previsión de ventas** (importe × probabilidad por mes de cierre, 6 meses), **rendimiento por vendedor** (ganado/abierto/perdido + tasa de éxito), **tiempo promedio por etapa** (a partir del historial), **conversión de prospectos** por fuente.
- ✅ Gráficos SVG propios con tooltip al pasar el ratón, leyendas y paleta categórica validada (los colores de etapas/estados siguen a la entidad).
- ✅ Vista **Calendario** (`/calendario`): cierres previstos y actividades por día, filtro "solo lo mío", abre la negociación en el panel lateral.
- ✅ Vista **Cronograma** (`/cronograma`): barra por negociación desde su creación hasta el cierre previsto o real, agrupada por etapa o responsable, línea de hoy y vencidas resaltadas, zoom 4–52 semanas.
- ✅ **Exportar** a Excel (.xlsx) y CSV desde cada tablero (incluye columnas personalizadas). **Importar** (`/importar`) desde Excel o CSV con mapeo de columnas sugerido, vista previa y creación de compañías referidas por nombre.
- ✅ Formularios públicos: pestañas Enlace / Insertar (iframe con **vista previa en vivo**) / **Enviar por API (JSON)** con endpoint `POST /api/public/forms/{slug}` sin clave y ejemplos React y curl.
- ⬜ Pendiente para más adelante: varios paneles con nombre y compartidos, programar envío de informes por correo, exportar gráficos a imagen/PDF.

## Etapa 6 — Colaboración y administración ✅
- ✅ **Permisos por tablero** (Usuarios → Permisos): por cada tablero, *Editar todo*, *Su equipo*, *Solo lo suyo*, *Solo lectura* o *Sin acceso*. Los administradores siempre tienen acceso total. **Equipos** con miembros para el nivel "Su equipo".
- ✅ **Papelera** (`/papelera`): el borrado es lógico; se puede restaurar o borrar definitivamente (admin). **Auditoría** (`/auditoria`, admin): quién creó, modificó (campo por campo, antes → después), eliminó o restauró cada elemento; filtros por usuario y tipo; también registra cambios hechos por la API y automatizaciones.
- ✅ **Productos** (`/productos`) y **cotizaciones** (`/cotizaciones`): se crean desde la negociación, líneas con cantidad, precio, descuento e impuesto, totales, estados (borrador, enviada, aceptada, rechazada, vencida), numeración automática; al aceptar se actualiza el importe de la negociación. Vista de impresión limpia para **imprimir o guardar como PDF** con los datos de la empresa.
- ✅ **Multimoneda**: moneda por negociación/prospecto/producto; moneda base y tipos de cambio en **Empresa y moneda** (`/configuracion/empresa`); el inicio y los informes convierten a la moneda base.
- ✅ **API REST** `/api/v1` con **tokens personales** (API de prospectos → API REST): leads, contacts, companies, deals, tasks (GET lista con búsqueda, GET id, POST, PUT parcial, DELETE a papelera), `POST /deals/{id}/stage`, notas en la línea de tiempo, pipelines, users, me. Documentación y ejemplos en la misma página.
- ✅ **Tiempo real**: los tableros abiertos por otros usuarios se refrescan solos al guardar cambios (bus de cambios en memoria).
- ✅ **Modo oscuro** (menú de usuario → Modo oscuro), recordado por navegador.
- ⬜ Pendiente para más adelante: permisos por campo, PDF generado en servidor (hoy: impresión del navegador), tipos de cambio automáticos, API con paginación por cursor y webhooks de salida por recurso.

## Extras (30 oct 2026) ✅
- ✅ Menú lateral **personalizable** por usuario (ocultar, renombrar, reordenar, mover de sección, enlaces propios, restablecer).
- ✅ Diálogos propios de confirmar/pedir texto (los navegadores incrustados bloquean `confirm`/`prompt`) y **error boundary** por pantalla y en el panel lateral.
- ✅ **Productos**: categorías gestionables, etiquetas editables en la vista, imágenes (la primera es la principal), vista lista/mosaico, stock opcional, y **API pública de catálogo** para webs externas (`/api/public/products`, imágenes públicas) más CRUD por `/api/v1/products`.
- ✅ Proyecto **open source** (MIT © Steven Fabricio Gazo Maliaño): `tests/Dana.Tests` (xUnit, 30 pruebas), `Dockerfile` multi-etapa, `docker-compose.yml` (dev) y `docker-compose.prod.yml` (prod con backups), CI en GitHub Actions, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, `/health`.

## Etapa 7 — Mensajería (Twilio) y facturación electrónica CR 🔧
Base construida (sin UI); el contrato de API de Senda y la documentación de Confluence aún no están disponibles, así que lo pendiente depende de ellos.
- ✅ **Twilio**: `Services/Messaging/TwilioMessagingService.cs` envía SMS y WhatsApp con el SDK oficial (`Twilio` 8.1.0), valida el número en E.164 y registra cada envío en la línea de tiempo. Configuración en `Twilio:*` (credenciales en User Secrets).
- ✅ **Webhook de estado** `POST /api/webhooks/twilio/status`: valida la firma `X-Twilio-Signature` y registra los mensajes no entregados.
- ✅ **Facturas electrónicas (base)**: modelo `ElectronicInvoice` con líneas que incluyen CABYS, interfaz `IElectronicInvoiceProvider` y `InvoiceService` (borrador, validación previa a envío, envío, consulta de estado). Sin proveedor configurado, solo permite borradores.
- ✅ Pruebas unitarias de normalización de teléfonos, validación de facturas y comportamiento sin proveedor (`tests/Dana.Tests/MessagingAndInvoicingTests.cs`).
- ✅ **Telegram — hablar con los agentes**: bot con long polling (`Services/Telegram/`, sin URL pública). Cada chat se vincula a un usuario de Dana con un código de 6 dígitos que vence en 10 min (`/agentes/telegram`) y el agente actúa con los permisos de ese usuario. Comandos: `/vincular`, `/agentes`, `/agente NOMBRE|NUMERO`, `/nueva`, `/desvincular`; cualquier otro texto va al agente activo. Configuración en `Telegram:BotToken` (User Secrets) y `Telegram:BotUsername`.
- ⬜ **Telegram — pruebas en vivo**: crear el bot con @BotFather, probar vinculación, conversación y permisos de escritura con un usuario de prueba.
- ⬜ **Telegram — mejoras**: respuestas en streaming (editando el mensaje), enviar fotos y archivos adjuntos, aprobación antes de acciones de escritura, y webhook en lugar de long polling cuando haya URL pública.
- ⬜ **Twilio — interfaz**: botón "Enviar SMS / WhatsApp" en la ficha de contacto, prospecto y negociación; selección de plantilla.
- ⬜ **Twilio — WhatsApp**: registrar el remitente de WhatsApp Business y las plantillas aprobadas por Meta (sin plantilla aprobada no se puede iniciar conversación).
- ⬜ **Twilio — consentimiento**: campo de aceptación/baja por contacto antes de enviar mensajes.
- ⬜ **Twilio — mensajes entrantes**: webhook de entrada que registre respuestas en la línea de tiempo y notifique al responsable.
- ⬜ **Twilio — pruebas en vivo**: probar envío y webhook con el sandbox de Twilio y una URL pública (`Twilio:PublicBaseUrl`).
- ✅ **Facturación — adaptador Senda**: `SendaInvoiceProvider : IElectronicInvoiceProvider` emite (`POST /comprobante/factura/enviar`) y consulta estado (`GET /comprobante/{clave}`). Se activa al configurar Senda en **Configuración → Integraciones** (datos fiscales del emisor, auth `Bearer`); mapea el `codigoTarifa` por tarifa. Falta probar contra el sandbox real de Hacienda.
- ⬜ **Facturación — adaptador GTI** (facturaelectronica.cr): alternativa si se elige GTI. Bloqueado: no publica documentación de API; hay que pedirla a ventas@gticr.com.
- ✅ **Facturación — interfaz**: facturas (`/facturas`) emitidas desde una cotización, listado con estados, enlaces de pago (Tilopay) y botón "Contabilizar"; emisión y consulta de estado vía el proveedor configurado.
- ⬜ **Facturación — documentos**: guardar XML y PDF como adjuntos, con retención de al menos 5 años; numeración consecutiva; anulación con nota de crédito; auditoría de emisiones.
- ⬜ **Facturación — validación**: pruebas de integración contra el ambiente de pruebas del proveedor antes de producción.
- ⬜ **Documentación Confluence** (`confluence.savegresoft.com/documentacion`): obtener la URL correcta o acceso; la página actual es una landing de mensajería sin contenido técnico.

## Etapa 8 — Ventas, suscripciones y formas de pago + contabilidad (3 nov 2026) ✅
Dos ejes ortogonales: la **naturaleza del ingreso** (venta única vs suscripción) vive en el producto/línea; la **forma de pago** (contado / tractos / crédito) vive en la venta. Un mismo Deal puede generar venta única **y** suscripción.
- ✅ **Modelo de datos**: `BillingModel` (OneTime/Recurring) en Product; desglose `OneTimeAmount`/`RecurringAmount`/`RecurringFrequency`/`AnnualContractValue` en Deal; `PaymentPlan` + `Installment` + `PaymentTerm` (colección `paymentplans`).
- ✅ **Suscripciones**: `RecurringInvoice` evolucionado con `SubscriptionStatus` (Activa/En prueba/Pausada/Cancelada/Finalizada), `Mrr` y ciclo de vida (pausar/reanudar/cancelar/generar factura del periodo). Página **Suscripciones** (`/suscripciones`, `/facturacion/recurrentes`) con KPIs MRR/ARR, titular, estados y filtros.
- ✅ **Planes de pago (formas de pago)**: `PaymentPlanService` arma las cuotas por **contado** (una cuota hoy), **tractos** (N cuotas mensuales, una factura por tracto) o **crédito** (neto X días). Página **Planes de pago** (`/facturacion/planes`, `/planes-de-pago`): construir desde cotización, facturar cuota (factura borrador de línea única, impuesto 0, CABYS en blanco), cobrar (enlace Tilopay) y marcar pagado.
- ✅ **Enlaces de pago (Tilopay)**: `TilopayService` crea el enlace (`/pagar/{token}`) para cobrar una factura o cuota; al confirmarse el pago, marca pagado y dispara el asiento del cobro.
- ✅ **Materializar venta**: desglose en la ficha de la negociación y botón/banner **"Materializar venta"** (`/deals/{id}/materializar`) que crea el plan de pago y/o la suscripción desde el deal y su cotización; avisa si supera el **límite de crédito** de la compañía.
- ✅ **Contabilidad**: plan de cuentas, asientos de partida doble, libro mayor, balance de comprobación y estados financieros (`/contabilidad`). `AccountingService` contabiliza facturas (CxC/Ingreso/IVA) y cobros (Banco/CxC) de forma idempotente; `AutoPost` configurable en **Configuración contable** (por defecto **OFF**, posteo manual con "Contabilizar"). **Cuentas por cobrar** (`/contabilidad/cxc`) con aging desde las cuotas no pagadas; `CreditLimit` por compañía.
- ✅ **Panel de ingresos** (`/ingresos`): MRR/ARR, suscripciones activas y churn del mes, por cobrar/vencido, renovaciones a 30 días, cobros vencidos y suscripciones por estado.
- ⬜ **Mejoras futuras**: posteo automático de los asientos de tractos, reconocimiento de ingresos de prepagos anuales (ingreso diferido), histórico de MRR y confirmar el tratamiento fiscal de los tractos con contador.

## Ideas futuras
- Integración de calendario (Google/Outlook), llamadas VoIP, WhatsApp, firma electrónica de cotizaciones, app móvil (PWA), IA para resumir fichas y sugerir siguientes pasos.
- **Centro de configuración** (`/configuracion`): estado del sistema (Mongo, SMTP, Google, registro), contadores y accesos a todas las secciones de administración y preferencias personales.
- Sidebar: la lista del menú tiene su propio scroll y el pie "Personalizar menú" queda fijo abajo.

## Agentes de IA y oficina virtual (30 oct 2026) ✅
- Proveedores de IA (`/configuracion/ia`): claves cifradas de Anthropic y OpenAI, modelos por defecto (lista cargada desde la API), prueba de conexión, límite de herramientas por respuesta.
- Agentes (`/agentes`): CRUD con rol, instrucciones, proveedor/modelo, esfuerzo, herramientas y permiso de escritura; 4 agentes de ejemplo; chat con historial de conversaciones por usuario.
- Herramientas sobre el CRM (`Services/Ai/AgentTools.cs`): buscar/obtener/estadísticas/embudos/usuarios y crear/actualizar/nota.
- Oficina virtual (`/oficina`): mapa 2D en canvas, personajes pixel art, movimiento por teclado o clic (BFS), presencia en tiempo real, burbujas de "pensando" y respuestas, chat lateral.
- Proactividad: rutinas por agente (cada N horas / diaria / semanal / manual, destinatarios, "Ejecutar ahora", worker `AgentRoutineWorker`), acción de automatización "Consultar a un agente de IA" (prompt con variables, aviso al responsable/todos, nota en el elemento), entrega por notificación + chat + burbuja en la oficina.
- Plantillas (15), jefe del equipo, comunicación entre agentes (`consultar_agente`, profundidad máxima 2), estado de ánimo con feedback 👍/👎, herramientas de cotizaciones y productos, búsqueda web (Brave / OpenAI Responses / Anthropic web_search) y lectura de páginas, burbuja flotante global, oficina rediseñada por zonas (cocina, sala de reuniones acristalada, lounge, recepción, WC) con puestos asignados automáticamente, monitores animados, reloj real y agentes que teclean y se mueven (café, WC, reunión, sofá, charlas).
- Alcance ampliado: streaming en Claude (CreateStreaming) y OpenAI (SSE), memoria de largo plazo (`agentmemories`, herramientas recordar/olvidar, pestaña Memoria), acciones con confirmación (correo, SMS/WhatsApp) con tarjeta aprobar/cancelar, notificaciones internas desde el agente.
- Documentos legales/comerciales (`documents`): plantillas NDA/contrato/propuesta/carta/acta con marcadores, editor Markdown con vista previa y tablas, versiones, exportación a Word (Open XML) e impresión; tarjeta en negociaciones y prospectos; herramientas de documentos y lectura de adjuntos (PdfPig, Open XML, ClosedXML) para los agentes; carga de archivos en el chat.
- Firma electrónica simple (DocumentSigner con token, página pública /firmar/{token} con canvas, POST /api/firmar/{token}, evidencia fecha/IP, bloque de firmas en vista previa/impresión/Word), adjuntos en correos (EmailService con MimeKit Multipart; enviar_correo con documentoId/adjuntoId), informes de rutinas por correo, documentos en la búsqueda global, auditoría de documentos y agentes.
- Word y PDF: exportación a PDF en servidor (`Services/DocumentPdf.cs`, PDFsharp 6.1.1 MIT con resolutor de fuentes DejaVu embebidas), tablas reales en Word, importación docx/pdf/md/txt → Markdown (lista, tarjeta y editor), herramienta `documento_importar`, correo con documento en PDF o Word.
- Plantillas de documentos propias (`documenttemplates`, gestor en /documentos, fusión con predefinidas, herramienta `documentos_plantillas`), recordatorios de firma (DocumentService.SendSignatureRemindersAsync desde ReminderWorker a las 14:00 UTC, `PublicBaseUrl` en configuración para los enlaces), widget de inicio "Agentes y documentos", detener respuesta y detalle de herramientas en el chat, rate limiting (60/min por IP) en endpoints públicos.
- Ideas siguientes: voz, firma con certificado digital, plantillas de correo generadas por agentes.
