# syntax=docker/dockerfile:1.7
# ---------------------------------------------------------------------------
# Dana CRM · imagen de producción (multi-etapa)
#   docker build -t dana:latest .
#   docker run -p 8080:8080 -e Mongo__ConnectionString=mongodb://mongo:27017 dana:latest
# ---------------------------------------------------------------------------

# ---- 1) Restaurar dependencias (capa cacheable) ----
# SDK pineado a la versión que genera correctamente los static assets del framework de Blazor
# (el tag flotante :10.0 producía un manifiesto sin /_framework → blazor.web.js 404).
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS restore
WORKDIR /src
COPY Dana.csproj ./
RUN dotnet restore Dana.csproj

# ---- 2) Compilar y publicar ----
# IMPORTANTE: sin --no-restore. El stage 'restore' restaura solo con Dana.csproj (sin wwwroot
# ni Components), lo que NO registra los static assets del framework de Blazor; con --no-restore
# ese obj/ incompleto se reutilizaba y blazor.web.js no se publicaba (404). Al publicar con
# restauración completa (proyecto entero presente) se generan wwwroot/_framework/*.
FROM restore AS build
COPY . .
RUN dotnet publish Dana.csproj -c Release -o /app/publish /p:UseAppHost=false
# Diagnóstico temporal: confirma en el log que el framework quedó publicado.
RUN echo "=== wwwroot/_framework ===" && ls -la /app/publish/wwwroot/_framework/ 2>&1 | head -20 || echo "NO existe wwwroot/_framework"

# ---- 3) Pruebas (opcional: docker build --target test .) ----
FROM build AS test
WORKDIR /src/tests/Dana.Tests
RUN dotnet test -c Release --logger "trx;LogFileName=results.trx"

# ---- 4) Imagen final: solo runtime, usuario sin privilegios ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Zona horaria e idioma (fechas en español en informes y correos)
ENV TZ=America/Costa_Rica \
    LANG=es_ES.UTF-8 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DisableHttpsRedirect=true \
    DataProtection__KeysPath=/app/keys

RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data/uploads /app/keys \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/publish ./

USER app
EXPOSE 8080
VOLUME ["/app/App_Data", "/app/keys"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Dana.dll"]
