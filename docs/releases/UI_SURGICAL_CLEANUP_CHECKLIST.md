# UI Surgical Cleanup Checklist

## Objetivo
Restaurar y modernizar la UI sin romper funcionalidad, ejecutando cambios pequeños, verificables y reversibles.

## Reglas de trabajo
- Un cambio funcional por commit.
- Mantener `main` siempre compilable.
- Ejecutar build de solución antes de cada push.
- No mezclar refactor de servicios con cambios visuales en el mismo commit.

## Fase 0 — Baseline y seguridad
- [x] Confirmar rama de trabajo: `refactor/ui-surgical-cleanup`.
- [x] Confirmar rama de respaldo existente (`backup/pre-cleanup-2026-02-18`).
- [x] Ejecutar: `dotnet build AuditHistoryExtractorPro.sln`.
- [x] Ejecutar: `dotnet run --project src/AuditHistoryExtractorPro.UI/AuditHistoryExtractorPro.UI.csproj`.
- [ ] Capturar evidencia visual (pantallas de Index, Extract, Export, History, Settings).

Estado Fase 0:
- Reporte técnico generado en `docs/UI_BASELINE_REPORT_2026-02-18.md`.
- Pendiente: capturas visuales manuales para anexar evidencia UI.

## Fase 1 — Configuración UI sin secretos
- [x] Mantener `appsettings.example.json` como plantilla versionada.
- [x] Mantener `appsettings.json` y `appsettings.Development.json` fuera de control de versiones.
- [x] Validar lectura de `Dataverse.EnvironmentUrl` en arranque.
- [x] Documentar setup local en README y guía de usuario (ya realizado).

Estado Fase 1:
- `Program.cs` robustecido con fallback seguro para `Dataverse.EnvironmentUrl` y parse tolerante de configuración (`Type`, `UseManagedIdentity`).
- Arranque UI validado en smoke test (`/` => 200) sin depender de variables de entorno manuales.

## Fase 2 — Limpieza visual mínima (sin cambiar lógica)
- [x] Revisar estilos duplicados en `src/AuditHistoryExtractorPro.UI/wwwroot/css/app.css`.
- [x] Eliminar solo CSS muerto/no referenciado.
- [x] Validar layout en `MainLayout` y `SimpleLayout`.
- [x] Verificar navegación entre páginas sin errores.

Estado Fase 2:
- Limpieza aplicada en `Shared/MainLayout.razor` eliminando repetición de estilos inline (sin cambios de lógica).
- Build de solución exitoso y smoke test de rutas UI (`/`, `/extract`, `/export`, `/history`, `/settings`) con `200`.

## Fase 3 — Corrección de eventos y estados de UI
- [x] Revisar `Pages/Extract.razor` (validaciones y estados de carga).
- [x] Revisar `Pages/Export.razor` (habilitado/deshabilitado según datos).
- [x] Revisar `Pages/History.razor` (render y paginación).
- [x] Mantener contratos de servicios sin cambios en esta fase.

Estado Fase 3 (avance actual):
- `Pages/Extract.razor` robustecido con:
	- validación de fechas (`FromDate <= ToDate`),
	- validación de GUID en `RecordId`,
	- validación de rango de `PageSize` (1..10000),
	- progreso real con `IProgress<ExtractionProgress>`,
	- cancelación por usuario con `CancellationTokenSource`.
- Build y smoke test de rutas (`/`, `/extract`, `/export`) validados con `200`.
- `Pages/Export.razor` robustecido con:
	- controles deshabilitados durante exportación,
	- guardas cuando no hay registros para exportar,
	- validación de nombre de archivo,
	- feedback informativo sin invocar exportación inválida.
- `Pages/History.razor` robustecido con:
	- paginación real sobre resultados filtrados,
	- validaciones de filtros (rango de fechas y GUID),
	- mensajes de estado/error en la vista,
	- deshabilitado de acciones de filtro durante carga.

Estado Fase 4 (inicio controlado):
- Integración mínima de flujo UI agregada con estado de sesión en memoria (`Extract` publica registros, `History` y `Export` consumen), sin cambios en contratos `Domain/Application`.

## Fase 4 — Integración progresiva con lógica
- [ ] Alinear cada vista con un caso de uso concreto de `Application`.
- [ ] Evitar lógica de negocio en componentes Razor.
- [x] Mover helpers de presentación a clases dedicadas si aplica.
- [ ] Revalidar que CLI no se vea impactado.

Estado Fase 4 (avance):
- `History` delega filtrado, paginación y cálculo de estadísticas en `HistoryViewService`, reduciendo lógica dentro de `History.razor`.
- `Extract` y `Export` delegan validación/mapeo de entrada en `ExtractViewService` y `ExportViewService`, reduciendo reglas en componentes Razor.
- `History`, `Extract` y `Export` usan modelos de estado explícitos de vista (`*PageViewModel`) para centralizar estado UI por página.
- `Extract` y `Export` encapsulan la orquestación de casos de uso en coordinadores UI (`ExtractPageCoordinator`, `ExportPageCoordinator`) en lugar de invocar MediatR directamente desde Razor.
- `History` también usa un coordinador UI (`HistoryPageCoordinator`) para carga/sincronización de sesión y aplicación de filtros/paginación.

## Fase 5 — Hardening antes de merge
- [x] `dotnet build AuditHistoryExtractorPro.sln`.
- [x] `dotnet test`.
- [x] Prueba manual end-to-end UI: Extract -> History -> Export.
- [x] Actualizar changelog/README con cambios visibles.
- [ ] Abrir PR hacia `main` con checklist marcado.

Estado Fase 5 (avance):
- Build de solución completado en verde.
- Suite de tests completada en verde (`passed=30`, `failed=0`).
- Verificación operativa de flujo UI realizada con app en ejecución (`/extract`, `/history`, `/export` => `200`).
- Documentación actualizada para handoff de merge (`README`, `CHANGELOG.md`, resumen de PR en `docs/PR_SUMMARY_UI_SURGICAL_CLEANUP.md`).

## Convención de commits sugerida
- `ui: fix startup config bootstrap`
- `ui: simplify shared layout styles`
- `ui: stabilize extract page state handling`
- `ui: refine export workflow guards`
- `docs: update ui setup and troubleshooting`

## Criterio de éxito
- UI inicia sin pasos manuales frágiles.
- Flujo principal de extracción y exportación usable.
- Sin regresiones en compilación ni pruebas de solución.
