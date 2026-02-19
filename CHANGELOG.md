# Changelog

## 2026-02-18 - UI Surgical Cleanup + Hardening

### Added
- Estado de sesión UI compartido para flujo `Extract -> History -> Export`.
- Servicios de presentación para validación y mapeo:
  - `ExtractViewService`
  - `ExportViewService`
  - `HistoryViewService`
- Modelos de estado por página:
  - `ExtractPageViewModel`
  - `ExportPageViewModel`
  - `HistoryPageViewModel`
- Coordinadores de página para encapsular orquestación UI:
  - `ExtractPageCoordinator`
  - `ExportPageCoordinator`
  - `HistoryPageCoordinator`

### Changed
- `Extract.razor`, `Export.razor` y `History.razor` simplificados para delegar reglas de presentación y coordinación a servicios dedicados.
- `Program.cs` actualizado para registrar servicios/coordinadores de UI.
- Checklist de limpieza UI actualizado con avances por fase y validaciones de hardening.

### Validation
- `dotnet build AuditHistoryExtractorPro.sln` en verde.
- `dotnet test` en verde (`passed=30`, `failed=0`).
- Verificación operativa de rutas UI en ejecución:
  - `/extract` = 200
  - `/history` = 200
  - `/export` = 200
