# Release Notes — v1.0.0-qa-fix

Fecha: 2026-02-18
Tag: `v1.0.0-qa-fix`
Commit: `ce7d435`

## Resumen
Esta versión corrige hallazgos críticos de QA en el flujo de Settings y en la consulta de extracción hacia Dataverse.

## Cambios incluidos
- Se reemplazó la validación simulada de conexión en Settings por validación real de autenticación/token y creación de cliente Dataverse.
- Se añadió carga de metadatos desde Settings (entidades, atributos y acciones auditables).
- Se aplicó el filtro `recordId` al `QueryExpression` de extracción en el repositorio Dataverse.
- Se evitó fallo en `/settings` por inicialización temprana de proveedor OAuth2 sin credenciales completas (creación bajo demanda).

## Archivos clave
- `src/AuditHistoryExtractorPro.UI/Pages/Settings.razor`
- `src/AuditHistoryExtractorPro.Infrastructure/Repositories/DataverseAuditRepository.cs`

## Validación ejecutada
- Build solución: OK (`dotnet build .\AuditHistoryExtractorPro.sln`)
- Tests: 30 passed, 0 failed
- Smoke UI: `/`, `/settings`, `/extract`, `/history`, `/export` con HTTP 200

## Impacto
- Mejora la confiabilidad del flujo de conexión y metadata en UI.
- Asegura que el filtro de `recordId` sea efectivo en extracción real.
- Reduce errores de arranque/renderizado en Settings cuando faltan parámetros OAuth2.
