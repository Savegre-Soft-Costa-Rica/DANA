# Dana · CRM open source para startups y pymes

[![CI](https://img.shields.io/badge/CI-GitHub_Actions-2088FF?logo=githubactions&logoColor=white)](.github/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE) [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com) [![MongoDB](https://img.shields.io/badge/MongoDB-7-47A248?logo=mongodb&logoColor=white)](https://www.mongodb.com)

**Dana** es un CRM gratuito y de código abierto, pensado para equipos pequeños que necesitan vender con orden sin pagar por usuario: prospectos, contactos, compañías y negociaciones con embudos (estructura **Bitrix24**), una interfaz de tableros al estilo **monday.com**, automatizaciones, actividades, informes, cotizaciones, API y formularios de captación. Construido con **.NET 10 Blazor Server** y **MongoDB**, en español.

Licencia **MIT** © 2026 Steven Fabricio Gazo Maliaño. Úsalo, modifícalo y despliégalo libremente; las contribuciones son bienvenidas ([CONTRIBUTING.md](CONTRIBUTING.md)).

## Inicio rápido

```bash
# Opción A · Docker (producción en 2 comandos)
cp .env.example .env && nano .env        # al menos MONGO_PASSWORD
docker compose -f docker-compose.prod.yml up -d --build
# → http://localhost:8080  (el primer usuario que se registra es administrador)

# Opción B · Desarrollo
docker compose up -d                     # MongoDB + mongo-express (http://localhost:8081)
dotnet run                               # → http://localhost:5025
dotnet test                              # pruebas unitarias e integración (usa la base dana_test)
```

## Funcionalidades

| Módulo | Equivalente Bitrix | Qué hace |
|---|---|---|
| **Prospectos** (`/leads`) | Leads | Clientes potenciales con estado (Nuevo, En proceso, Calificado, No calificado, Convertido). Botón **Convertir** que crea Contacto + Compañía + Negociación. |
| **Contactos** (`/contacts`) | Contacts | Personas, vinculadas opcionalmente a una compañía. Muestra sus negociaciones. |
| **Compañías** (`/companies`) | Companies | Organizaciones (cliente, proveedor, socio…). Muestra sus contactos y negociaciones. |
| **Negociaciones** (`/deals`) | Deals | Kanban con **arrastrar y soltar** entre etapas, vista de lista, historial de cambios de etapa, cierre automático al llegar a una etapa Ganada/Perdida. |
| **Embudos** (`/pipelines`) | Deal pipelines / categorías | Varios embudos, cada uno con sus etapas (nombre, color, tipo: en proceso / ganada / perdida). Un embudo predeterminado. |
| **Panel** (`/`) | Dashboard | Totales, embudo visual por etapa, prospectos por estado, negociaciones recientes, carga de datos de ejemplo. |
| **Usuarios** (`/usuarios`) | Usuarios | Solo Admin: roles (Admin / Usuario), activar/desactivar, eliminar. |
| **Cotizaciones** (`/cotizaciones`) | Quotes | Cotizaciones con líneas del catálogo (precio, unidad, impuesto), subtotal/IVA/total, vigencia, estados (Borrador, Enviada, Aceptada, Rechazada) e impresión en PDF. |
| **Facturación** (`/facturacion`) | Invoices | Facturas electrónicas de Costa Rica (emisión desde cotización, envío a Hacienda vía proveedor, estados), **suscripciones** SaaS y **planes de pago** (formas de pago: contado, tractos, crédito). |
| **Suscripciones** (`/suscripciones`) | Recurring deals | Relación de cobro recurrente (mensual, anual…) con estados (Activa, En prueba, Pausada, Cancelada, Finalizada), MRR/ARR y ciclo de vida (pausar, reanudar, cancelar, generar la factura del periodo). |
| **Planes de pago** (`/planes-de-pago`) | — | Cómo se cobra una venta única: **contado**, **tractos** (cuotas) o **crédito** (neto X días), con una cuota por tracto, su factura y su enlace de pago. |
| **Contabilidad** (`/contabilidad`) | — | Plan de cuentas, asientos de partida doble, libro mayor, balance de comprobación, estados financieros y cuentas por cobrar (aging), con posteo automático u manual de facturas y cobros. |

## Interfaz tipo monday

- **Tableros** por módulo con grupos de colores, edición en línea en cada celda, columnas de Estado/Persona/Etiqueta, fila "+ Agregar elemento", resumen por grupo y ordenación.
- Vistas **Tabla principal** y **Kanban** (arrastrar entre grupos) en los cuatro módulos.
- **Panel lateral** para abrir un elemento sin salir del tablero, con pestaña de **Actualizaciones** (notas y eventos automáticos).
- Filtro por persona, búsqueda, selección masiva (eliminar / mover), búsqueda global en la barra superior.
- **Personalización** (Etapa 2): columnas personalizadas con el "+" del encabezado, etiquetas de estado editables, menú por columna (ordenar, mover, ocultar, renombrar, eliminar), filtros avanzados, vistas guardadas, duplicar y archivar. Todo se guarda en la colección `boards` (una configuración por tablero) y los valores personalizados en `item.custom`.
- **Actividades y comunicación** (Etapa 3): llamadas, reuniones, correos y tareas con fecha y responsable en cada ficha; vista **Mi trabajo**; menciones `@usuario` con campana de notificaciones; adjuntos en las actualizaciones; envío de correo con plantillas (`/plantillas`).
- **Automatizaciones e integración** (Etapa 4): recetas "cuando X → entonces Y", recordatorios automáticos, API pública para recibir el formulario de contacto de su sitio web, formularios públicos y tutorial interactivo.
- **Informes** (Etapa 5): panel configurable con gráficos (barras, línea, dona, embudo, tabla, números), previsión de ventas, rendimiento por vendedor, tiempo por etapa y conversión; vistas Calendario y Cronograma; exportar a Excel/CSV e importar con mapeo de columnas.
- **Colaboración y administración** (Etapa 6): permisos por tablero y equipos, papelera con restauración, auditoría de cambios, productos y cotizaciones imprimibles (PDF desde el navegador), multimoneda, API REST con tokens, refresco en tiempo real y modo oscuro.
- Panel de inicio con widgets. El plan de funcionalidades por etapas está en [ROADMAP.md](ROADMAP.md).

## Agentes de IA y oficina virtual

Dana incluye **agentes** (asistentes de IA con un rol) que trabajan sobre los datos reales de la organización usando **Claude (Anthropic)** o **ChatGPT (OpenAI)**.

1. Un administrador guarda las claves de API en **Configuración → Proveedores de IA** (`/configuracion/ia`). Las claves se cifran con Data Protection (en Docker conserve el volumen `/app/keys`). Desde esa página puede cargar la lista de modelos de cada cuenta y probar la conexión.
2. En **Agentes** (`/agentes`) se crean o editan los asistentes: nombre, rol, instrucciones (system prompt), proveedor y modelo, esfuerzo (Claude), herramientas habilitadas y si puede **modificar datos**. Se crean cuatro agentes de ejemplo: Dana (general), Sofía (analista de ventas), Leo (prospección) y Mía (redacción).
3. En la **Oficina virtual** (`/oficina`, estilo gather.town) cada usuario conectado aparece como un personaje: muévase con las flechas/WASD o haciendo clic, acérquese a un agente y pulse `E` (o haga clic en él) para abrir el chat. Los demás usuarios ven a los agentes "pensando" y su última respuesta en una burbuja. Los administradores pueden mover los escritorios con Shift + arrastrar.

**Proactividad.** Cada agente puede tener **rutinas** (pestaña "Proactividad" al configurarlo): un encargo que ejecuta solo cada N horas, a diario o semanalmente (zona horaria configurable en Proveedores de IA), con plantillas listas (informe matutino, negociaciones estancadas, prospectos nuevos). El resultado llega como notificación y como mensaje en el chat de cada destinatario, y el agente lo comenta en la oficina. Para reaccionar a eventos del CRM use la acción **"Consultar a un agente de IA"** en Automatizaciones (por ejemplo, al crearse un prospecto, Leo lo califica, avisa al responsable y anota la respuesta en el registro). Cualquier usuario puede crear y configurar agentes; eliminarlos requiere ser administrador o su creador.

**Plantillas, jefe y colaboración.** Al crear un agente se elige una plantilla (asistente general, analista, prospección, redacción, cotizador, cobranza, coordinador, atención al cliente, investigación de mercado, asesora legal, contabilidad, inventario, marketing, cierre de ventas, formación) o se parte en blanco. Un agente puede ser el **jefe del equipo** (👑, por defecto Dana): coordina, delega con la herramienta `consultar_agente` y recibe las escalaciones; cada agente puede tener habilitada la comunicación con otros (y con quiénes). Los agentes tienen un **estado de ánimo** (0-100) que sube con trabajo completado y 👍 y baja con errores y 👎; se ve en las tarjetas, en el chat y en la oficina, y matiza su tono. El agente de prospección puede **buscar en internet** (`buscar_web`, `leer_pagina`) usando Brave Search (clave opcional) o la búsqueda nativa de OpenAI/Anthropic. Hay una **burbuja flotante** en toda la aplicación para hablar con cualquier agente, y en la oficina los agentes teclean en su computadora, van por café, al baño, a reuniones, al sofá o a charlar con un colega.

**Alcance ampliado.** Las respuestas llegan en **streaming** (se ven escribiendo). Los agentes tienen **memoria de largo plazo**: guardan hechos y preferencias con `recordar` (por usuario o para todo el equipo), las usan en conversaciones futuras y se revisan en la pestaña "Memoria" del agente. Pueden **enviar correos y SMS/WhatsApp** (`enviar_correo`, `enviar_mensaje` vía Twilio) con una tarjeta de **confirmación** que el usuario aprueba o cancela antes de que salga nada, y **notificar** a usuarios dentro de la aplicación (`notificar_usuario`).

**Documentos.** En **Documentos** (`/documentos`, también desde cada negociación y prospecto) se crean contratos de servicios, acuerdos de confidencialidad (NDA), propuestas, cartas formales y actas a partir de plantillas con marcadores (empresa, contraparte, contacto, monto, fecha…) que se rellenan con los datos del registro ligado. El editor es Markdown con vista previa, cada guardado crea una **versión** restaurable, y el documento se puede **imprimir/guardar en PDF** o **descargar en Word** (.docx). Los agentes (Elena, Mía, Dana…) crean, leen y modifican documentos con sus herramientas (`documento_crear`, `documento_obtener`, `documento_actualizar`, `documentos_buscar`) y pueden **leer archivos adjuntos** (pdf, docx, xlsx, txt, html…) de un registro o subidos en el propio chat con el clip 📎 (`adjuntos_listar`, `adjunto_leer`).

**Word y PDF.** Cada documento se descarga en **Word** (.docx con tablas reales) o en **PDF** generado en el servidor (PDFsharp, fuentes DejaVu incluidas en `wwwroot/fonts`, funciona en Docker), además de imprimirse. Se pueden **importar** documentos existentes en Word o PDF (y .md/.txt) desde la lista de documentos, desde una negociación o prospecto, o reemplazando el contenido en el editor: el Word se convierte a Markdown conservando títulos, negritas, listas y tablas; el PDF se convierte a texto por páginas. Los agentes importan adjuntos con `documento_importar` y al enviar un documento por correo eligen PDF (por defecto) o Word.

**Plantillas propias y recordatorios.** En Documentos → Plantillas se crean plantillas de la empresa (o se duplican y adaptan las predefinidas) con los mismos marcadores; aparecen en todos los selectores y los agentes pueden usarlas por nombre (`documentos_plantillas`). Los firmantes que llevan 3 días sin firmar reciben un recordatorio por correo (si hay SMTP) y quien pidió la firma una notificación, como máximo cada 3 días. El inicio muestra un panel "Agentes y documentos" con acciones pendientes de confirmar, firmas pendientes, últimos informes de rutinas y documentos recientes. En el chat se puede **detener** una respuesta en curso y desplegar la entrada y el resultado de cada herramienta. Los endpoints públicos (prospectos, formularios de firma) tienen un límite de 60 peticiones por minuto por IP.

**Firma electrónica.** Desde el editor de un documento se añaden firmantes (nombre, correo, cargo); cada uno recibe un enlace público único (`/firmar/{token}`) donde lee el documento, dibuja su firma y acepta. Se registran fecha, hora e IP; la firma aparece en la vista previa, la impresión y el Word, y con todas las firmas el documento pasa a "Firmado". El enlace se copia o se envía por correo (si hay SMTP). Además: los agentes pueden adjuntar un documento en Word o un archivo al enviar correos (`enviar_correo` con `documentoId` / `adjuntoId`), las rutinas pueden enviar su informe también por correo, y la búsqueda global incluye documentos.

Herramientas disponibles para los agentes (función calling): `buscar`, `obtener`, `estadisticas`, `resumen_embudo`, `listar_embudos`, `listar_usuarios` (lectura) y `crear`, `actualizar`, `agregar_nota` (escritura, solo si el agente tiene permiso), `cotizaciones_buscar`, `cotizacion_obtener`, `cotizacion_crear`, `cotizacion_actualizar` (cotizaciones con precios del catálogo), `buscar_web`, `leer_pagina` (internet) y `consultar_agente` (otros agentes). Para facturación y contabilidad hay además herramientas opt-in que se otorgan a cada perfil: `facturas_buscar`, `factura_obtener`, `emitir_factura`, `crear_enlace_pago`, `suscripciones_buscar`, `suscripcion_crear`, `suscripcion_estado`, `suscripcion_generar` y la familia `contabilidad_*` (plan de cuentas, asientos, mayor, balance, estados financieros). Los agentes de ejemplo que las usan son **Nico** (cotizador), **Vera** (seguimiento y cobranza) y **Tomás** (contabilidad). Los registros creados por un agente quedan auditados como `usuario · agente`.

### Servidor MCP (conecta Claude con el CRM)

Todo ese catálogo de herramientas se expone además por **MCP** ([Model Context Protocol](https://modelcontextprotocol.io)): un servidor HTTP en `/mcp`, autenticado con los tokens de la API (`/configuracion/api`), que deja que **Claude Code, Claude Desktop** o cualquier cliente MCP opere el CRM completo desde fuera de Dana. Se configura con la sección `Mcp` de `appsettings.json` (`Enabled`, `AllowWrites`). Guía de conexión paso a paso en **[MCP.md](MCP.md)**.

```bash
# Claude Code, en un comando:
claude mcp add --transport http dana http://localhost:5025/mcp \
  --header "Authorization: Bearer dtk_tu_token"
```

## Facturación, suscripciones y formas de pago

Dana cobra dos naturalezas de ingreso que se combinan libremente en una misma negociación (p. ej. implementación + licencia mensual):

- **Venta única** (`BillingModel.OneTime` en el producto): sitio web, software a medida, un proyecto. Se cobra una sola vez según su **forma de pago**.
- **Suscripción** (`BillingModel.Recurring`, con una frecuencia): licencia o servicio SaaS que se factura por periodo y aporta **MRR** (ingreso recurrente mensual).

Son dos ejes ortogonales: la **naturaleza del ingreso** vive en el producto/línea; la **forma de pago** vive en la venta.

### Formas de pago (planes de pago · `/planes-de-pago`)

Una venta única se cobra con una de tres formas de pago (`PaymentTerm`), que genera un **plan de pago** con sus cuotas (`Installment`):

| Forma de pago | Cuotas que genera |
|---|---|
| **Contado** | Una sola cuota con vencimiento hoy. |
| **Tractos** | N cuotas mensuales (mínimo 2); la última cuadra el total al céntimo. En Costa Rica **cada tracto genera su propia factura** (no se usan anticipos). |
| **Crédito** | Una cuota a **neto X días** (30 / 60 / 90; 30 por defecto). |

Cada cuota avanza por los estados **Pendiente → Facturado → Pagado** (y **Vencido** si pasa su fecha sin pagarse), y guarda su factura electrónica y su enlace de cobro. Desde la página de planes de pago se construye el plan a partir de una cotización, se **factura una cuota** (crea la factura electrónica en borrador: una línea, impuesto 0 y CABYS en blanco, porque el detalle ya se facturó en la cotización), se **cobra** (genera un enlace de pago de Tilopay) y se **marca como pagada**.

### Suscripciones (`/suscripciones`)

Una suscripción es la relación de cobro recurrente que sobrevive a la negociación que la originó. Tiene estado (**Activa, En prueba, Pausada, Cancelada, Finalizada**), su **MRR** y un ciclo de vida: pausar, reanudar, cancelar y generar la factura del periodo. La lista muestra KPIs de **MRR/ARR**, titular, estado y filtros.

### Enlaces de pago (Tilopay)

`crear_enlace_pago` genera un **enlace de pago** (`/pagar/{token}`) para cobrar una factura o una cuota en línea mediante **Tilopay**. Cuando el pago se confirma, la cuota/factura se marca como pagada y se dispara el asiento contable del cobro.

### Materializar una venta

Al ganar una negociación, en su ficha (`/deals/{id}`) se desglosa el monto en parte **de una vez** y parte **recurrente**; el botón/banner **"Materializar venta"** abre `/deals/{id}/materializar`, que crea el **plan de pago** y/o la **suscripción** a partir de la negociación y su cotización. Si la compañía tiene **límite de crédito**, se avisa cuando la venta a crédito lo supera.

### Facturación electrónica (Hacienda CR)

Las facturas (`/facturas`) se emiten desde una cotización (`emitir_factura`). El envío a Hacienda y la consulta de estado se hacen a través de un proveedor (`IElectronicInvoiceProvider`); el adaptador **Senda** (facturación electrónica de Savegre) se configura en **Configuración → Integraciones**. Sin proveedor configurado, Dana solo permite **borradores**.

### Contabilidad y cuentas por cobrar (`/contabilidad`)

Módulo de contabilidad en partida doble: **plan de cuentas**, **asientos de diario** (un asiento solo se registra si los débitos igualan a los créditos), **libro mayor**, **balance de comprobación** y **estados financieros**. Las facturas y los cobros se contabilizan con asientos idempotentes (factura → CxC / Ingreso / IVA; cobro → Banco / CxC). El **posteo automático** se activa en **Configuración contable** (`/contabilidad/configuracion`, por defecto **desactivado**: el posteo es manual con el botón "Contabilizar"). **Cuentas por cobrar** (`/contabilidad/cxc`) muestra el aging a partir de las cuotas no pagadas de los planes de pago, y cada compañía puede tener un **límite de crédito**.

### Panel de ingresos (`/ingresos`)

Resumen de facturación con KPIs de **MRR/ARR**, suscripciones activas y **churn** del mes, montos **por cobrar / vencidos**, **renovaciones** de los próximos 30 días, cobros vencidos (días de atraso) y suscripciones por estado.

## Docker

- **`Dockerfile`** multi-etapa: restaura, compila y publica con el SDK, y la imagen final usa solo el runtime de ASP.NET con usuario sin privilegios, `HEALTHCHECK` sobre `/health`, zona horaria configurable y volúmenes para adjuntos (`/app/App_Data`) y claves de cookies (`/app/keys`). `docker build --target test .` ejecuta las pruebas dentro de la imagen.
- **`docker-compose.yml`** (desarrollo): MongoDB + mongo-express; con `--profile app` también la app en contenedor.
- **`docker-compose.prod.yml`** (producción): app + MongoDB con usuario/contraseña, sin puertos de la base expuestos, volúmenes persistentes, healthchecks, rotación de logs y un servicio de **copias de seguridad diarias** en `./backups`. Ponga delante un proxy con HTTPS (Caddy, Nginx, Traefik); la app respeta `X-Forwarded-*`.

## Pruebas

`tests/Dana.Tests` (xUnit): pruebas unitarias puras (columnas personalizadas, filtros, plantillas, cotizaciones, moneda, auditoría, importación, menú) y de integración contra MongoDB (embudos, conversión, automatizaciones, recordatorios, API de entrada, exportar/importar, papelera, permisos, cotizaciones, informes). Las de integración usan `DANA_TEST_MONGO` (por defecto `mongodb://localhost:27017`, base `dana_test`, que se vacía al iniciar) y se omiten si no hay servidor. El workflow de **GitHub Actions** (`.github/workflows/ci.yml`) compila, prueba con un servicio de Mongo y construye la imagen Docker.

## Stack

- .NET 10 · Blazor Web App con render interactivo **Server** (SignalR)
- MongoDB 7 (driver oficial `MongoDB.Driver`)
- Bootstrap 5 (incluido en la plantilla)
- Autenticación: cookie + usuarios en Mongo (`PasswordHasher` de ASP.NET Core Identity) + `Microsoft.AspNetCore.Authentication.Google`

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` → 10.x)
- Docker (para MongoDB) o una instancia de MongoDB accesible

## Puesta en marcha

```bash
# 1. MongoDB (y mongo-express en http://localhost:8081 para inspeccionar la base)
docker compose up -d

# 2. Ejecutar la app
dotnet run
# → http://localhost:5025  (perfil "http")
# → https://localhost:7173 (perfil "https": dotnet run --launch-profile https)
```

Al arrancar, la app crea los índices y el embudo predeterminado **"Ventas"** si no existe. En el panel aparece el botón **"Cargar datos de ejemplo"** mientras la base esté vacía.

> Si `dotnet` no está en el `PATH` pero fue instalado con el script oficial: `export PATH=$HOME/.dotnet:$PATH`.

## Configuración

Toda la configuración vive en `appsettings.json` y puede sobrescribirse con **user-secrets** (desarrollo) o **variables de entorno** (producción, usando `__` como separador). Ver `.env.example`.

### MongoDB

```json
"Mongo": {
  "ConnectionString": "mongodb://localhost:27017",
  "Database": "dana"
}
```

### Usuarios y acceso

Toda la aplicación exige sesión. Los usuarios se guardan en la colección `users` de Mongo con la contraseña **hasheada** (PBKDF2 vía `PasswordHasher`).

- **Registro**: `/cuenta/registro` (nombre, correo, contraseña de 8+ caracteres). El **primer usuario registrado es Admin**; los siguientes son *Usuario*.
- **Inicio de sesión**: `/cuenta/login` (correo + contraseña, "Recordarme" 7 días) y, si está configurado, botón **Continuar con Google**.
- **Mi cuenta**: `/cuenta/perfil` para cambiar nombre y contraseña (o definir una contraseña si entró con Google).
- **Administración**: `/usuarios` (solo Admin) para cambiar roles, desactivar o eliminar cuentas. Un usuario desactivado pierde la sesión de inmediato.
- `Authentication:AllowRegistration` (`true` por defecto). En `false` el formulario de registro se cierra (salvo para el primer usuario); los demás entran con Google o se registran antes de cerrar el registro.

Las páginas de `/cuenta/*` se renderizan en modo **estático (SSR)** porque necesitan `HttpContext` para emitir la cookie; el resto de la app es interactiva (Blazor Server). Ver `Components/App.razor`.

### API de prospectos (formulario de su sitio web)

1. Entre como administrador a **API de prospectos** (`/configuracion/api`) y cree un canal (p. ej. "Sitio web"). Copie la clave.
2. Configure cómo se crea el prospecto: fuente, estado inicial, a quién se asigna (una persona o rotación), plantilla del título, a quién notificar, orígenes permitidos y el mapeo de campos (acepta `name`/`nombre`, `email`/`correo`, `phone`/`telefono`, `company`/`empresa`, `message`/`mensaje`…).
3. Desde su sitio (React, WordPress, n8n…) haga un `POST`:

```js
await fetch("https://su-dana/api/public/leads", {
  method: "POST",
  headers: { "Content-Type": "application/json", "X-Api-Key": "dana_xxx" },
  body: JSON.stringify({ name: "María", lastName: "Rodríguez", email: "maria@acme.com", phone: "+506 8888-0000", company: "ACME", message: "Quiero una cotización" })
});
```

Respuestas: `201 {ok,id,title}` · `400` sin datos mínimos · `401` clave inválida · `403` canal deshabilitado u origen no permitido. CORS está habilitado. Los campos no mapeados se agregan a los comentarios del prospecto. Las automatizaciones (p. ej. "al crear un prospecto, asignar por rotación y programar llamada") se aplican también a los prospectos recibidos por la API.

### Catálogo de productos para su sitio web

Los productos (`/productos`) tienen categorías, etiquetas, imágenes y vista de lista o mosaico. Los marcados como *activos* y *publicados* se exponen en una API de solo lectura, autenticada con la clave de un canal de la API de prospectos que tenga activado "Catálogo de productos":

```
GET /api/public/products?key=<clave>&category=Servicios&tag=oferta&q=texto
GET /api/public/products/{id}?key=<clave>
GET /catalog/images/{imageId}          (imágenes públicas, sin clave)
```

Cada producto devuelve `id, name, sku, description, category, tags, unit, price, currency, taxRate, stock, image, images[]`. Para crear o editar productos use `/api/v1/products` con token.

### API REST (`/api/v1`)

Cree un token en **API de prospectos → API REST** (solo Admin) y envíe `Authorization: Bearer <token>`. Recursos: `leads`, `contacts`, `companies`, `deals`, `tasks`, `products` (y `GET /product-categories`) (GET lista con `?q=&limit=&skip=&archived=`, GET `/{id}`, POST, PUT parcial, DELETE a papelera), `POST /deals/{id}/stage`, `GET|POST /{recurso}/{id}/notes`, `GET /pipelines`, `GET /users`, `GET /me`. Todo cambio queda auditado con el usuario dueño del token.

### Permisos, papelera y auditoría

- **Usuarios** (Admin): rol Admin/Usuario, y por cada tablero el nivel *Editar todo*, *Su equipo*, *Solo lo suyo*, *Solo lectura* o *Sin acceso*. Los **equipos** definen quién es "compañero" para el nivel *Su equipo*.
- Eliminar envía a la **papelera** (`/papelera`), desde donde se restaura o se borra definitivamente. La **auditoría** (`/auditoria`) muestra cada cambio campo por campo.

### Formularios públicos y su API JSON

Cada formulario de `/formularios` tiene una página pública (`/formulario/{slug}`), un iframe para incrustar (con vista previa en la misma pantalla) y un endpoint JSON sin clave para formularios propios:

```js
await fetch("https://su-dana/api/public/forms/{slug}", {
  method: "POST", headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ name: "María", lastName: "Rodríguez", email: "maria@acme.com", phone: "", company: "ACME", message: "Hola" })
});
// 201 { ok: true, id, message } · 400 validación · 403 deshabilitado · 404 no existe
```

Aplica la configuración del formulario (fuente, responsable, exigir correo o teléfono) y un campo oculto `website` como honeypot anti-spam. Para mapeo de campos, rotación de responsables u orígenes permitidos use un canal de la API de prospectos.

### Correo saliente (SMTP, opcional)

```json
"Email": { "Smtp": { "Host": "smtp.gmail.com", "Port": 587, "User": "usuario@dominio", "Password": "contraseña-de-aplicación", "From": "usuario@dominio", "FromName": "Dana CRM", "UseStartTls": true } }
```

Guarde `User` y `Password` con `dotnet user-secrets set "Email:Smtp:Password" "..."` o variables de entorno (`Email__Smtp__Password`). Sin SMTP configurado, el botón **Enviar correo** registra el mensaje en la línea de tiempo y ofrece abrirlo en su cliente de correo. Los adjuntos se guardan en `App_Data/uploads` (excluido del repositorio).

### Google OAuth2 (opcional)

Si hay credenciales, aparece el botón de Google en el login. La cuenta Google se enlaza por **correo** con el usuario local (se crea si no existe, con rol *Usuario*, o *Admin* si es el primero).

1. En [Google Cloud Console](https://console.cloud.google.com/apis/credentials) cree un proyecto (o use uno existente).
2. **APIs y servicios → Pantalla de consentimiento OAuth**: tipo *Interno* (solo cuentas de su Google Workspace) o *Externo*. Alcances: `email`, `profile`, `openid`.
3. **Credenciales → Crear credenciales → ID de cliente de OAuth 2.0**, tipo *Aplicación web*.
   - Orígenes autorizados de JavaScript: `http://localhost:5025`, `https://localhost:7173` y el dominio de producción.
   - **URIs de redireccionamiento autorizados** (ruta fija `/signin-google`):
     - `http://localhost:5025/signin-google`
     - `https://localhost:7173/signin-google`
     - `https://<su-dominio>/signin-google`
4. Guarde el **ID de cliente** y el **Secreto de cliente** **fuera del repositorio**:

```bash
# Desarrollo (user-secrets, ya inicializado en el .csproj)
dotnet user-secrets set "Authentication:Google:ClientId" "XXXX.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "GOCSPX-XXXX"

# Opcional: permitir solo cuentas Google de ciertos dominios
dotnet user-secrets set "Authentication:Google:AllowedDomains:0" "grupomecsa.net"
```

```bash
# Producción (variables de entorno)
export Authentication__Google__ClientId="XXXX.apps.googleusercontent.com"
export Authentication__Google__ClientSecret="GOCSPX-XXXX"
export Authentication__Google__AllowedDomains__0="grupomecsa.net"
export Authentication__AllowRegistration=false
```

Rutas de sesión: `/cuenta/login`, `/cuenta/registro`, `/cuenta/perfil`, `/cuenta/login-google`, `POST /cuenta/logout`, `/cuenta/acceso-denegado`.

## Estructura del proyecto

```
Dana/
├── Models/          Entidades: Lead, Contact, Company, Deal, Pipeline(+Stage), User, Catalogs
├── Data/            MongoSettings, MongoContext (convenciones BSON), IRepository<T> / MongoRepository<T>
├── Services/        PipelineService, DealService (Kanban/historial), LeadService (conversión),
│                    DashboardService, DemoDataService, UserService (registro/login/roles),
│                    MongoInitializer, AuthOptions/GoogleAuthOptions
├── Components/
│   ├── Layout/      MainLayout, NavMenu (usuario, Mi cuenta, Cerrar sesión), EmptyLayout (páginas de cuenta)
│   ├── Shared/      Board, BoardPage, BoardToolbar, Updates, TaskList, EmailComposer, PageHeader, Badge, …
│   └── Pages/       Home, MyWork, Reports, CalendarPage, TimelinePage, ImportPage, Automations, LeadForms, PublicForm, Quotes/,
│                    ApiChannels, Tutorial, EmailTemplates, Search, Leads/, Contacts/, Companies/, Deals/, Pipelines/,
│                    Account/ (Login, Register, Profile, AccessDenied, UserList)
├── Program.cs       DI, Mongo, cookie auth + Google, endpoints /cuenta/login-google y POST /cuenta/logout
├── appsettings.json
├── docker-compose.yml   MongoDB 7 + mongo-express
└── .env.example
```

## Modelo de datos (colecciones MongoDB)

- `pipelines` — `{ name, isDefault, sort, stages: [{ _id, name, color, sort, kind: Process|Success|Failure }] }`
- `deals` — `{ title, pipelineId, stageId, contactId?, companyId?, leadId?, amount, currency, probability, expectedCloseDate?, closedAt?, type, source, assignedTo, comments, history: [{ at, fromStageId, fromStageName, toStageId, toStageName }] }`
- `leads` — `{ title, name, lastName, companyName, position, phone, email, source, status, opportunity, currency, convertedContactId?, convertedCompanyId?, convertedDealId? }`
- `contacts` — `{ name, lastName, position, phone, email, companyId?, type, source, address, assignedTo, comments }`
- `companies` — `{ title, type, industry, taxId, phone, email, web, address, annualRevenue?, employees?, assignedTo, comments }`
- `users` — `{ email (único), name, picture?, passwordHash?, hasGoogle, role: Admin|Usuario, isActive, lastLoginAt? }`
- `activities` — `{ entityType, entityId, entityTitle, kind: note|created|stage|converted, text, author }`
- `boards` — `{ boardKey, customColumns[], columnOrder[], hiddenColumns[], labelOverrides{}, views[] }`
- `tasks` — `{ title, kind: call|meeting|email|task, isDone, dueAt?, durationMinutes?, assignedTo, createdBy, notes, completedAt?, entityType?, entityId?, entityTitle? }`
- `notifications` — `{ recipient, kind: mention|assigned|due|system, text, link?, actor, isRead }`
- `attachments` — `{ entityType, entityId, activityId?, fileName, contentType, size, storedName, uploadedBy }`
- `emailtemplates` — `{ name, subject, body }`
- `automations` — `{ name, boardKey, isEnabled, trigger{type,params}, actions[{type,params}], runCount, lastRunAt, lastError }`
- `apichannels` — `{ name, key, isEnabled, source, status, assignTo, titleTemplate, notifyUsers[], allowedOrigins[], fieldMap{}, receivedCount, lastPayload }`
- `leadforms` — `{ name, slug, title, description, successMessage, fields…, submitCount }`
- `reportdashboards` — `{ name, widgets[{ type, board, metric, groupBy, period, scope, person, width }] }`
- `audit` — `{ user, action, collection, entityId, title, changes[{ field, old, new }] }`
- `settings` — `{ companyName, taxId, address…, baseCurrency, rates{}, quotePrefix, nextQuoteNumber, quoteValidDays, defaultTaxRate, quoteTerms }`
- `teams` — `{ name, color, members[] }` · `products` — `{ name, sku, price, currency, taxRate, unit, isActive, billingModel: OneTime|Recurring, recurringFrequency? }` · `quotes` — `{ number, title, status, dealId, contactId, companyId, currency, lines[], validUntil, notes, terms }` · `apitokens` — `{ name, key, userName, isAdmin, isEnabled }`
- `paymentplans` — `{ title, term: Contado|Tractos|Credito, totalAmount, currency, netDays?, dealId?, quoteId?, contactId?, companyId?, installments: [{ label, dueDate, amount, status: pending|invoiced|paid|overdue, invoiceId?, paymentLinkId?, paidAt? }] }`
- `recurringinvoices` (suscripciones) — `{ title, status: Active|Trialing|Paused|Cancelled|Ended, mrr, currency, frequency, dealId?, contactId?, companyId?, nextRunAt?, … }`
- `invoices` (facturas electrónicas) — `{ number, status, dealId?, quoteId?, companyId?, lines[{ description, cabys, quantity, unitPrice, taxRate }], currency, hacienda{clave, estado} }`
- `paymentlinks` — `{ token, amount, currency, status, invoiceId?, payerName?, payerEmail?, paidAt? }` (Tilopay)
- `accounts` — `{ code, name, type: Activo|Pasivo|Patrimonio|Ingreso|Gasto, isPostable, isActive, currency }` · `journalentries` — `{ number, date, description, reference?, status: draft|posted, source: manual|invoice|payment, sourceId?, currency, lines[{ accountCode, accountName, debit, credit }] }`

Todas las entidades tienen además `createdBy`, `updatedBy`, `deletedAt`/`deletedBy` (papelera), `isArchived` y `custom`.



Las etapas van **embebidas** en el embudo (se leen siempre juntas); las negociaciones referencian embudo/etapa por id, igual que Bitrix (`CATEGORY_ID` / `STAGE_ID`).

## Reglas de negocio implementadas

- Solo puede haber **un embudo predeterminado**; al marcar otro se desmarca el anterior.
- No se puede eliminar un embudo con negociaciones ni una etapa que tenga negociaciones.
- Al cambiar una negociación de embudo, se ubica en la **primera etapa** del nuevo embudo.
- Mover a una etapa **Ganada** fija probabilidad 100 % y fecha de cierre; **Perdida** fija 0 % y fecha de cierre; volver a una etapa en proceso reabre la negociación.
- Un prospecto solo puede **convertirse una vez**; la negociación creada queda enlazada al contacto, la compañía y el prospecto de origen.
- Un **asiento contable** solo se registra si los débitos igualan a los créditos (y el total es mayor que cero).
- Los asientos generados desde una factura o un cobro son **idempotentes** (se identifican por `source` + `sourceId`): no se duplican al reintentar el posteo.
- El **posteo automático** de facturas y cobros está **desactivado** por defecto; el posteo manual siempre está disponible.
- En **tractos**, cada cuota se factura por separado (una factura por tracto); la última cuota cuadra el total para evitar diferencias por redondeo.

## Licencia

[MIT](LICENSE) © 2026 Steven Fabricio Gazo Maliaño.
