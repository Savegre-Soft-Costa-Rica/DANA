# syntax=docker/dockerfile:1.7
# ---------------------------------------------------------------------------
# Dana CRM · imagen de producción (multi-etapa)
#   docker build -t dana:latest .
#   docker run -p 8080:8080 -e Mongo__ConnectionString=mongodb://mongo:27017 dana:latest
# ---------------------------------------------------------------------------

# ---- 1) Restaurar dependencias (capa cacheable) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Dana.csproj ./
RUN dotnet restore Dana.csproj

# ---- 2) Compilar y publicar ----
FROM restore AS build
COPY . .
RUN dotnet publish Dana.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

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
