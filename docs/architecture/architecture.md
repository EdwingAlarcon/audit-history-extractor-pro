# Guía de Arquitectura - Audit History Extractor Pro

## Visión General

Audit History Extractor Pro está construido siguiendo los principios de **Clean Architecture**, lo que garantiza:

- ✅ Separación de responsabilidades
- ✅ Independencia de frameworks
- ✅ Testabilidad
- ✅ Mantenibilidad
- ✅ Escalabilidad

## Capas de la Arquitectura

### 1. Domain Layer (Capa de Dominio)

**Ubicación:** `src/AuditHistoryExtractorPro.Domain/`

**Responsabilidad:** Contiene la lógica de negocio pura y las reglas del dominio.

**Componentes:**
- **Entities:** Modelos de negocio (`AuditRecord`, `RecordComparison`, `AuditStatistics`)
- **Value Objects:** Objetos de valor inmutables (`ExtractionCriteria`, `ExportConfiguration`, `AuthenticationConfiguration`)
- **Interfaces:** Contratos para repositorios y servicios (`IAuditRepository`, `IExportService`, `IAuthenticationProvider`)

**Reglas:**
- ✅ No depende de ninguna otra capa
- ✅ Sin dependencias externas
- ✅ Contiene solo lógica de negocio pura

```
Domain/
├── Entities/
│   └── AuditRecord.cs
├── ValueObjects/
│   └── Configuration.cs
└── Interfaces/
    └── IRepositories.cs
```

### 2. Application Layer (Capa de Aplicación)

**Ubicación:** `src/AuditHistoryExtractorPro.Application/`

**Responsabilidad:** Orquesta el flujo de datos y coordina casos de uso.

**Componentes:**
- **Use Cases:** Casos de uso específicos implementados con MediatR
  - `ExtractAuditCommand`: Extracción de registros
  - `ExportAuditCommand`: Exportación de datos
  - `CompareRecordsQuery`: Comparación de versiones
- **DTOs:** Objetos de transferencia de datos
- **Validators:** Validación de entrada con FluentValidation

**Patrón Utilizado:** CQRS (Command Query Responsibility Segregation) con MediatR

```
Application/
├── UseCases/
│   ├── ExtractAudit/
│   │   └── ExtractAuditCommand.cs
│   ├── ExportAudit/
│   │   └── ExportAuditCommand.cs
│   └── CompareRecords/
│       └── CompareRecordsQuery.cs
```

**Reglas:**
- ✅ Depende solo de la capa de Dominio
- ✅ Define interfaces que la Infraestructura implementará
- ✅ Coordina casos de uso sin implementar detalles técnicos

### 3. Infrastructure Layer (Capa de Infraestructura)

**Ubicación:** `src/AuditHistoryExtractorPro.Infrastructure/`

**Responsabilidad:** Implementa detalles técnicos y se comunica con servicios externos.

**Componentes:**

#### 3.1 Authentication
Implementa múltiples estrategias de autenticación:
```
Authentication/
├── OAuth2AuthenticationProvider.cs
├── ClientSecretAuthenticationProvider.cs
├── CertificateAuthenticationProvider.cs
└── ManagedIdentityAuthenticationProvider.cs
```

#### 3.2 Repositories
```
Repositories/
└── DataverseAuditRepository.cs
```
- Conexión con Dataverse usando `ServiceClient`
- Manejo de paginación
- Políticas de reintento con Polly
- Gestión de throttling

#### 3.3 Services
```
Services/
├── ExportServices.cs
│   ├── ExcelExportService
│   ├── CsvExportService
│   ├── JsonExportService
│   └── CompositeExportService
└── SupportServices.cs
    ├── MemoryCacheService
    ├── AzureKeyVaultSecretManager
    └── AuditProcessor
```

**Tecnologías Utilizadas:**
- Microsoft.PowerPlatform.Dataverse.Client
- ClosedXML (Excel)
- CsvHelper (CSV)
- Azure.Identity & Azure.Security.KeyVault.Secrets
- Microsoft.Extensions.Caching.Memory
- Polly (Resilience patterns)

### 4. Presentation Layer (Capa de Presentación)

**Ubicación:** `src/AuditHistoryExtractorPro.CLI/` y `src/AuditHistoryExtractorPro.UI/`

#### 4.1 CLI (Command Line Interface)
```
CLI/
├── Program.cs
├── Commands/
│   └── Commands.cs
│       ├── ExtractCommand
│       ├── ExportCommand
│       ├── CompareCommand
│       ├── ConfigCommand
│       └── ValidateCommand
```

**Tecnologías:**
- System.CommandLine
- Spectre.Console (UI de consola)

**Comandos Disponibles:**
```bash
audit-extractor extract --entity account --from 2024-01-01 --format excel
audit-extractor export --input data.json --format csv
audit-extractor compare --entity account --recordid <guid>
audit-extractor config init
audit-extractor validate
```

#### 4.2 UI (Web Interface)
```
UI/
├── Program.cs
├── Pages/
│   ├── Index.razor (Dashboard)
│   ├── Extract.razor
│   ├── Compare.razor
│   └── Settings.razor
└── Shared/
    ├── MainLayout.razor
    └── NavMenu.razor
```

**Tecnologías:**
- Blazor Server
- MudBlazor (Components UI)

## Flujo de Datos

### Flujo de Extracción de Auditoría

```
┌─────────┐         ┌─────────────┐         ┌──────────────┐         ┌───────────┐
│   CLI   │────────▶│  MediatR    │────────▶│  Application │────────▶│  Domain   │
│   UI    │         │  Handler    │         │   UseCase    │         │  Entities │
└─────────┘         └─────────────┘         └──────────────┘         └───────────┘
                            │                        │
                            │                        ▼
                            │                ┌──────────────┐
                            │                │Infrastructure│
                            │                │  Repository  │
                            │                └──────────────┘
                            │                        │
                            │                        ▼
                            │                ┌──────────────┐
                            └───────────────▶│  Dataverse   │
                                             │     API      │
                                             └──────────────┘
```

### Secuencia Completa de Extracción y Exportación

1. **Usuario ejecuta comando o acción en UI**
2. **CLI/UI crea comando MediatR** (`ExtractAuditCommand`)
3. **MediatR enruta al handler apropiado** (`ExtractAuditCommandHandler`)
4. **Handler valida criterios de extracción**
5. **Handler invoca `IAuditRepository.ExtractAuditRecordsAsync()`**
6. **Repository:**
   - Obtiene token de autenticación (`IAuthenticationProvider`)
   - Conecta con Dataverse (`ServiceClient`)
   - Ejecuta consultas con paginación
   - Maneja throttling y reintentos (Polly)
   - Retorna registros de auditoría
7. **Handler procesa registros:**
   - Normaliza (`IAuditProcessor.NormalizeRecordsAsync()`)
   - Enriquece (`IAuditProcessor.EnrichRecordsAsync()`)
8. **Handler retorna resultado**
9. **CLI/UI crea comando de exportación** (`ExportAuditCommand`)
10. **Export handler selecciona exportador apropiado** (`CompositeExportService`)
11. **Exportador genera archivo** (Excel/CSV/JSON)
12. **Opcionalmente envía a destino** (Azure Blob, Email)
13. **Retorna path del archivo generado**

## Patrones de Diseño Aplicados

### 1. Repository Pattern
- Abstrae acceso a datos de Dataverse
- Permite cambiar implementación sin afectar lógica de negocio

### 2. Strategy Pattern
- Múltiples estrategias de autenticación (`IAuthenticationProvider`)
- Múltiples exportadores (`IExportService`)

### 3. Factory Pattern
- `AuthenticationProviderFactory`: Crea el proveedor apropiado según configuración

### 4. Adapter Pattern
- `SerilogAdapter<T>`: Adapta Serilog a nuestra interfaz `ILogger<T>`

### 5. CQRS Pattern
- Comandos para modificaciones (`ExtractAuditCommand`, `ExportAuditCommand`)
- Queries para consultas (`CompareRecordsQuery`)

### 6. Composite Pattern
- `CompositeExportService`: Delega a exportadores específicos

## Gestión de Dependencias

### Inyección de Dependencias

Todos los servicios se registran en el contenedor DI:

```csharp
services.AddSingleton<ICacheService, MemoryCacheService>();
services.AddTransient<IExportService, CompositeExportService>();
services.AddTransient<IAuditRepository, DataverseAuditRepository>();
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));
```

### Principio de Inversión de Dependencias

Las capas superiores definen interfaces, las inferiores las implementan.

## Resiliencia y Rendimiento

### Políticas de Reintento (Polly)

```csharp
_retryPolicy = Policy
    .Handle<FaultException>()
    .Or<TimeoutException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
    );
```

### Throttling
- Detección automática de límites de Dataverse
- Espera y reintento con backoff exponencial

### Paginación
- Procesamiento por páginas configurables (default: 5000)
- Manejo de `PagingCookie` para grandes volúmenes

### Paralelización
- Múltiples entidades pueden procesarse en paralelo
- Configurable: `max_parallel_requests`

### Caché
- Caché en memoria para tokens de autenticación
- Caché de última fecha de extracción (modo incremental)
- Configurable: `cache_duration_minutes`

## Seguridad

### Gestión de Secretos
- Integración con Azure Key Vault
- Formato: `kv://vault-name/secret-name`
- No se almacenan secretos en texto plano

### Autenticación Robusta
- Soporte para Managed Identity (entornos Azure)
- Certificados para autenticación sin secretos
- OAuth2 con refresh token automático

### Logging Sin Información Sensible
- Tokens nunca se loggean
- Secretos enmascarados en logs

## Escalabilidad

### Horizontal
- Stateless: Puede ejecutarse en múltiples instancias
- Cache distribuido posible (Redis)

### Vertical
- Paralelización configurable
- Procesamiento por lotes

### Grandes Volúmenes
- Streaming de datos (no carga todo en memoria)
- Compresión automática de archivos grandes
- Exportación por lotes

## Testing

### Arquitectura Testeable

Gracias a Clean Architecture:
- **Domain**: Tests unitarios puros
- **Application**: Tests de casos de uso con mocks
- **Infrastructure**: Tests de integración

### Estructura de Tests

```
tests/
├── AuditHistoryExtractorPro.Domain.Tests/
├── AuditHistoryExtractorPro.Application.Tests/
└── AuditHistoryExtractorPro.Infrastructure.Tests/
```

## Diagramas

Ver [diagrams.md](./diagrams.md) para diagramas detallados de:
- Arquitectura de capas
- Flujo de datos
- Secuencia de operaciones
- Diagrama de clases

## Tecnologías y Frameworks

| Capa | Tecnología | Versión | Propósito |
|------|-----------|---------|-----------|
| Todas | .NET | 8.0 | Framework base |
| Domain | - | - | Sin dependencias |
| Application | MediatR | 12.2.0 | CQRS pattern |
| Application | FluentValidation | 11.9.0 | Validación |
| Infrastructure | Dataverse Client | 1.1.14 | Conexión Dataverse |
| Infrastructure | Polly | 8.2.1 | Resiliencia |
| Infrastructure | ClosedXML | 0.102.1 | Exportación Excel |
| Infrastructure | CsvHelper | 30.0.1 | Exportación CSV |
| Infrastructure | Azure Identity | 1.10.4 | Autenticación Azure |
| Infrastructure | Azure KeyVault | 4.5.0 | Gestión secretos |
| Infrastructure | Serilog | 3.1.1 | Logging |
| CLI | System.CommandLine | 2.0.0-beta4 | Parsing comandos |
| CLI | Spectre.Console | 0.48.0 | UI consola |
| UI | Blazor Server | 8.0 | Web UI |
| UI | MudBlazor | 6.11.2 | UI Components |

## Próximos Pasos y Mejoras

### Corto Plazo
- [ ] Implementar SQL Exporter
- [ ] Agregar envío por email
- [ ] Dashboard con gráficos reales
- [ ] Filtros avanzados en UI

### Mediano Plazo
- [ ] Cache distribuido (Redis)
- [ ] Azure Blob Storage integration
- [ ] Scheduler para extracciones automáticas
- [ ] Webhooks para notificaciones

### Largo Plazo
- [ ] Machine Learning para detección de anomalías
- [ ] API REST para integración
- [ ] Soporte multi-tenant
- [ ] Exportación a Power BI

---

**Última actualización:** 17 de febrero de 2026  
**Versión:** 1.0.0
