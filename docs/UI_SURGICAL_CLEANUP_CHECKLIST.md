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
- [ ] Revisar estilos duplicados en `src/AuditHistoryExtractorPro.UI/wwwroot/css/app.css`.
- [ ] Eliminar solo CSS muerto/no referenciado.
- [ ] Validar layout en `MainLayout` y `SimpleLayout`.
- [ ] Verificar navegación entre páginas sin errores.

## Fase 3 — Corrección de eventos y estados de UI
- [ ] Revisar `Pages/Extract.razor` (validaciones y estados de carga).
- [ ] Revisar `Pages/Export.razor` (habilitado/deshabilitado según datos).
- [ ] Revisar `Pages/History.razor` (render y paginación).
- [ ] Mantener contratos de servicios sin cambios en esta fase.

## Fase 4 — Integración progresiva con lógica
- [ ] Alinear cada vista con un caso de uso concreto de `Application`.
- [ ] Evitar lógica de negocio en componentes Razor.
- [ ] Mover helpers de presentación a clases dedicadas si aplica.
- [ ] Revalidar que CLI no se vea impactado.

## Fase 5 — Hardening antes de merge
- [ ] `dotnet build AuditHistoryExtractorPro.sln`.
- [ ] `dotnet test`.
- [ ] Prueba manual end-to-end UI: Extract -> History -> Export.
- [ ] Actualizar changelog/README con cambios visibles.
- [ ] Abrir PR hacia `main` con checklist marcado.

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
