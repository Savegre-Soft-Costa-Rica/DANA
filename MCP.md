# Servidor MCP de Dana

Dana incluye un **servidor MCP** ([Model Context Protocol](https://modelcontextprotocol.io)) que expone **todo el catálogo de herramientas de los agentes** (el mismo que usan Dana, Sofía, Nico, Vera, Tomás…) a cualquier cliente MCP: **Claude Code**, **Claude Desktop**, el SDK de Anthropic, etc.

Con él, Claude puede operar el CRM completo —prospectos, contactos, compañías, negociaciones, cotizaciones, facturación electrónica, contabilidad, suscripciones, documentos y proyectos— **directamente contra tu instancia de Dana**, sin pasar por la interfaz web.

> Es la misma lógica que ya ejecutan los agentes internos ([`AgentTools`](Services/Ai/AgentTools.cs)); el servidor MCP ([`DanaMcp`](Services/Ai/DanaMcp.cs)) solo la publica por el protocolo estándar. Cada herramienta nueva que se añada al catálogo de agentes aparece automáticamente en MCP.

## Cómo funciona

| | |
|---|---|
| **Transporte** | HTTP "Streamable" (sin estado) |
| **Ruta** | `POST /mcp` |
| **URL (desarrollo)** | `http://localhost:5025/mcp` |
| **URL (Docker/producción)** | `http://localhost:8080/mcp` (o tu dominio: `https://tu-dana.com/mcp`) |
| **Autenticación** | Token personal de la API REST (`Authorization: Bearer <token>` o `X-Api-Key: <token>`) |
| **Herramientas** | Todo el catálogo de [`AgentTools`](Services/Ai/AgentTools.cs) (buscar, crear, actualizar, cotizaciones, facturas, contabilidad, documentos, proyectos…) |

### 1. Obtener un token

1. Entra en Dana → **Configuración → API** (`/configuracion/api`).
2. Crea un token. Cópialo (empieza por `dtk_…`); solo se muestra al crearlo.
3. El token actúa **en nombre del usuario** con el que se creó y queda auditado así. Si es de un administrador, puede hacer lo mismo que ese administrador.

### 2. Configuración del servidor (`appsettings.json`)

```jsonc
"Mcp": {
  "Enabled": true,      // false = no se publica el endpoint /mcp
  "AllowWrites": true   // false = solo lectura (bloquea crear/actualizar/emitir/enviar…)
}
```

- Con `AllowWrites: false`, Claude puede **consultar** el CRM pero no modificar nada: útil para dar acceso de solo lectura.
- Con `AllowWrites: true`, las acciones que normalmente piden confirmación en el chat (enviar correo, SMS/WhatsApp) se ejecutan directamente, porque **el propio cliente MCP ya te pide aprobar cada llamada** antes de ejecutarla.

## Conectar con Claude Code

Con un comando (reemplaza la URL y el token):

```bash
claude mcp add --transport http dana http://localhost:5025/mcp \
  --header "Authorization: Bearer dtk_tu_token_aqui"
```

Luego, en una sesión de Claude Code:

```
/mcp            # muestra el estado de la conexión "dana"
```

Y ya puedes pedir cosas como *"busca los prospectos nuevos de esta semana y créame una tarea de seguimiento para cada uno"* o *"dame el balance de comprobación del mes"*.

### Alternativa: `.mcp.json` en el proyecto

Para compartir la configuración con tu equipo (sin el token), copia [`.mcp.json.example`](.mcp.json.example) a `.mcp.json` y pon el token en una variable de entorno `DANA_MCP_TOKEN`:

```json
{
  "mcpServers": {
    "dana": {
      "type": "http",
      "url": "http://localhost:5025/mcp",
      "headers": { "Authorization": "Bearer ${DANA_MCP_TOKEN}" }
    }
  }
}
```

> No cometas `.mcp.json` con el token en claro; usa la variable de entorno.

## Conectar con Claude Desktop

Claude Desktop admite servidores MCP remotos como **conectores personalizados**:

1. **Configuración → Conectores → Añadir conector personalizado**.
2. URL: `https://tu-dana.com/mcp` (necesita ser accesible desde tu equipo; para producción, HTTPS).
3. Añade la cabecera `Authorization: Bearer dtk_tu_token`.

Si tu versión de Claude Desktop no deja añadir cabeceras, usa el puente **`mcp-remote`** en `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "dana": {
      "command": "npx",
      "args": [
        "-y", "mcp-remote",
        "http://localhost:5025/mcp",
        "--header", "Authorization: Bearer dtk_tu_token_aqui"
      ]
    }
  }
}
```

Reinicia Claude Desktop y el servidor **dana** aparecerá con todas sus herramientas.

## Probar sin cliente (curl)

Sin token debe responder **401**:

```bash
curl -i http://localhost:5025/mcp -X POST \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
# → 401 {"error":"Falta el token..."}
```

Con token debe listar las herramientas:

```bash
curl -s http://localhost:5025/mcp -X POST \
  -H "Authorization: Bearer dtk_tu_token_aqui" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## Seguridad

- El endpoint `/mcp` **exige siempre un token válido y habilitado**; sin él responde 401.
- Un token da el mismo alcance que su usuario. Para acceso de solo lectura, crea el token con un usuario no administrador **y/o** pon `Mcp:AllowWrites: false`.
- En producción, publica `/mcp` **solo por HTTPS** y trata los tokens como contraseñas (puedes deshabilitarlos o borrarlos en `/configuracion/api`).
- Para desactivar por completo el servidor MCP, pon `Mcp:Enabled: false`.
