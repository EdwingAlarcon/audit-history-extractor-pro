// Interfaces migradas a archivos individuales (2026-02-27).
// ELIMINACIÓN PROGRAMADA: 2026-04-30 — verificar antes que ningún código referencia
// este archivo directamente (búsqueda: "IRepositories").
//
//   IAuthenticationProvider.cs
//   IAuditRepository.cs
//   IExportService.cs
//   IAuditProcessor.cs
//   ICacheService.cs
//   ISecretManager.cs
//   ISyncStateStore.cs
//   ExtractionProgress.cs
//
// ILogger<T> eliminado: usar Microsoft.Extensions.Logging.ILogger<T> directamente.
