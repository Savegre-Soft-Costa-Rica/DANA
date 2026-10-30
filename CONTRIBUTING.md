# Contribuir a Dana

¡Gracias por tu interés! Dana es un CRM open source pensado para startups y pymes. Toda ayuda es bienvenida: reportar errores, proponer funciones, mejorar la documentación o enviar código.

## Antes de empezar

- Lee el [README](README.md) para instalar y ejecutar el proyecto, y el [ROADMAP](ROADMAP.md) para ver qué hay hecho y qué viene.
- Busca si ya existe un issue parecido antes de abrir uno nuevo.

## Reportar un error

Abre un issue con: pasos para reproducirlo, qué esperabas, qué ocurrió, versión (commit) y navegador. Si puedes, adjunta captura y el fragmento relevante del log de la app.

## Proponer una función

Describe el problema que resuelve y cómo lo usaría una pyme. Si encaja con una etapa del roadmap, menciónala.

## Enviar código

1. Haz un fork y crea una rama desde `main`: `feat/nombre-corto` o `fix/nombre-corto`.
2. Ejecuta `docker compose up -d` (MongoDB) y `dotnet run`.
3. Añade o ajusta pruebas en `tests/Dana.Tests` y comprueba que pasan: `dotnet test`.
4. Mantén el estilo del proyecto: C# con `Nullable` activado, componentes Razor en español, sin dependencias nuevas salvo que sean necesarias.
5. Abre un pull request explicando el cambio. Un PR pequeño y enfocado se revisa más rápido.

## Estructura rápida

```
Models/      entidades (Mongo)            Services/   lógica de negocio
Data/        repositorio genérico          Components/ páginas y componentes Blazor
tests/       pruebas xUnit (unitarias + integración con Mongo)
```

## Código de conducta

Este proyecto sigue el [Código de conducta](CODE_OF_CONDUCT.md). Sé amable.

## Licencia

Al contribuir aceptas que tu aportación se publique bajo la [licencia MIT](LICENSE).
