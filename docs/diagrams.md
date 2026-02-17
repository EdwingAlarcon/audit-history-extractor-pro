# Diagramas de Arquitectura - Audit History Extractor Pro

## Diagrama de Arquitectura de Capas

```mermaid
graph TB
    subgraph "Presentation Layer"
        CLI[CLI - Command Line]
        UI[UI - Blazor Web]
    end
    
    subgraph "Application Layer"
        UC1[ExtractAuditCommand]
        UC2[ExportAuditCommand]
        UC3[CompareRecordsQuery]
        MED[MediatR]
    end
    
    subgraph "Domain Layer"
        ENT[Entities]
        VO[Value Objects]
        INT[Interfaces]
    end
    
    subgraph "Infrastructure Layer"
        AUTH[Authentication<br/>Providers]
        REPO[Dataverse<br/>Repository]
        EXP[Export<br/>Services]
        CACHE[Cache<br/>Service]
        KV[Key Vault<br/>Manager]
    end
    
    subgraph "External Services"
        DV[Microsoft<br/>Dataverse]
        AKV[Azure<br/>Key Vault]
        BLOB[Azure<br/>Blob Storage]
    end
    
    CLI --> MED
    UI --> MED
    MED --> UC1
    MED --> UC2
    MED --> UC3
    
    UC1 --> INT
    UC2 --> INT
    UC3 --> INT
    
    INT -.implements.-> AUTH
    INT -.implements.-> REPO
    INT -.implements.-> EXP
    INT -.implements.-> CACHE
    INT -.implements.-> KV
    
    REPO --> DV
    AUTH --> DV
    KV --> AKV
    EXP --> BLOB
    
    style CLI fill:#e1f5ff
    style UI fill:#e1f5ff
    style UC1 fill:#fff3e0
    style UC2 fill:#fff3e0
    style UC3 fill:#fff3e0
    style ENT fill:#f3e5f5
    style VO fill:#f3e5f5
    style INT fill:#f3e5f5
    style AUTH fill:#e8f5e9
    style REPO fill:#e8f5e9
    style EXP fill:#e8f5e9
    style DV fill:#ffebee
    style AKV fill:#ffebee
```

## Flujo de Extracción de Auditoría

```mermaid
sequenceDiagram
    actor User
    participant CLI
    participant MediatR
    participant Handler as ExtractAuditHandler
    participant Repository as AuditRepository
    participant Processor as AuditProcessor
    participant Auth as AuthProvider
    participant Dataverse
    
    User->>CLI: execute extract command
    CLI->>MediatR: Send(ExtractAuditCommand)
    MediatR->>Handler: Handle(command)
    
    Handler->>Handler: Validate criteria
    
    alt Incremental Mode
        Handler->>Repository: GetLastExtractionDate()
        Repository-->>Handler: lastDate
    end
    
    Handler->>Repository: ExtractAuditRecordsAsync()
    
    Repository->>Auth: GetAccessTokenAsync()
    Auth->>Dataverse: Authenticate
    Dataverse-->>Auth: access token
    Auth-->>Repository: token
    
    loop For each page
        Repository->>Dataverse: Query audit records
        Dataverse-->>Repository: page of records
        Repository->>Handler: Progress update
    end
    
    Repository-->>Handler: raw audit records
    
    Handler->>Processor: NormalizeRecordsAsync()
    Processor-->>Handler: normalized records
    
    Handler->>Processor: EnrichRecordsAsync()
    Processor-->>Handler: enriched records
    
    alt Incremental Mode
        Handler->>Repository: SaveLastExtractionDate()
    end
    
    Handler-->>MediatR: ExtractAuditResponse
    MediatR-->>CLI: response
    CLI-->>User: Display results
```

## Diagrama de Clases del Dominio

```mermaid
classDiagram
    class AuditRecord {
        +Guid AuditId
        +DateTime CreatedOn
        +string EntityName
        +Guid RecordId
        +string Operation
        +string UserId
        +string UserName
        +Dictionary~string, AuditFieldChange~ Changes
        +string? TransactionId
    }
    
    class AuditFieldChange {
        +string FieldName
        +string? OldValue
        +string? NewValue
        +string FieldType
        +bool HasChanged
        +GetChangeDescription() string
    }
    
    class RecordComparison {
        +Guid RecordId
        +string EntityName
        +AuditRecord? PreviousVersion
        +AuditRecord CurrentVersion
        +List~FieldDifference~ Differences
        +DateTime ComparisonDate
    }
    
    class FieldDifference {
        +string FieldName
        +object? OldValue
        +object? NewValue
        +DifferenceType Type
        +string Description
    }
    
    class ExtractionCriteria {
        +List~string~ EntityNames
        +List~string~? FieldNames
        +DateTime? FromDate
        +DateTime? ToDate
        +List~string~? UserIds
        +List~OperationType~? Operations
        +bool IncrementalMode
        +Validate()
    }
    
    class ExportConfiguration {
        +ExportFormat Format
        +string OutputPath
        +string FileName
        +bool CompressOutput
        +ExportDestination? Destination
    }
    
    AuditRecord "1" *-- "*" AuditFieldChange
    RecordComparison "1" *-- "2" AuditRecord
    RecordComparison "1" *-- "*" FieldDifference
```

## Patrón Strategy para Autenticación

```mermaid
classDiagram
    class IAuthenticationProvider {
        <<interface>>
        +GetAccessTokenAsync() Task~string~
        +ValidateConnectionAsync() Task~bool~
        +GetAuthenticationType() AuthenticationType
    }
    
    class OAuth2Provider {
        -IPublicClientApplication _clientApp
        +GetAccessTokenAsync() Task~string~
        +ValidateConnectionAsync() Task~bool~
    }
    
    class ClientSecretProvider {
        -IConfidentialClientApplication _clientApp
        -ISecretManager _secretManager
        +GetAccessTokenAsync() Task~string~
        +ValidateConnectionAsync() Task~bool~
    }
    
    class CertificateProvider {
        -X509Certificate2 _certificate
        +GetAccessTokenAsync() Task~string~
        +ValidateConnectionAsync() Task~bool~
    }
    
    class ManagedIdentityProvider {
        -DefaultAzureCredential _credential
        +GetAccessTokenAsync() Task~string~
        +ValidateConnectionAsync() Task~bool~
    }
    
    class AuthProviderFactory {
        +Create(config) IAuthenticationProvider
    }
    
    IAuthenticationProvider <|.. OAuth2Provider
    IAuthenticationProvider <|.. ClientSecretProvider
    IAuthenticationProvider <|.. CertificateProvider
    IAuthenticationProvider <|.. ManagedIdentityProvider
    
    AuthProviderFactory ..> IAuthenticationProvider : creates
```

## Patrón Composite para Exportación

```mermaid
classDiagram
    class IExportService {
        <<interface>>
        +ExportAsync(records, config) Task~string~
        +SendToDestinationAsync(filePath, destination) Task~bool~
        +SupportsFormat(format) bool
    }
    
    class ExcelExportService {
        +ExportAsync() Task~string~
        +SupportsFormat() bool
    }
    
    class CsvExportService {
        +ExportAsync() Task~string~
        +SupportsFormat() bool
    }
    
    class JsonExportService {
        +ExportAsync() Task~string~
        +SupportsFormat() bool
    }
    
    class SqlExportService {
        +ExportAsync() Task~string~
        +SupportsFormat() bool
    }
    
    class CompositeExportService {
        -Dictionary~ExportFormat, IExportService~ _exporters
        +ExportAsync() Task~string~
        +SupportsFormat() bool
    }
    
    IExportService <|.. ExcelExportService
    IExportService <|.. CsvExportService
    IExportService <|.. JsonExportService
    IExportService <|.. SqlExportService
    IExportService <|.. CompositeExportService
    
    CompositeExportService o-- IExportService : delegates to
```

## Flujo de Datos Completo

```mermaid
flowchart TD
    Start([User Action]) --> Input[User Input:<br/>Entities, Dates, Filters]
    
    Input --> Validate{Valid<br/>Input?}
    Validate -->|No| Error1[Display Error]
    Validate -->|Yes| Auth[Authenticate]
    
    Auth --> AuthType{Auth<br/>Type?}
    AuthType -->|OAuth2| OAuth[Interactive Login]
    AuthType -->|Secret| Secret[Get from KeyVault]
    AuthType -->|Cert| Cert[Load Certificate]
    AuthType -->|MI| MI[Use Managed Identity]
    
    OAuth --> Token[Access Token]
    Secret --> Token
    Cert --> Token
    MI --> Token
    
    Token --> Connect[Connect to Dataverse]
    
    Connect --> Check{Incremental<br/>Mode?}
    Check -->|Yes| GetLast[Get Last<br/>Extraction Date]
    Check -->|No| Query
    GetLast --> Query[Build Query]
    
    Query --> Loop{More<br/>Pages?}
    Loop -->|Yes| Fetch[Fetch Page]
    Fetch --> Process[Process Records]
    Process --> UpdateProgress[Update Progress]
    UpdateProgress --> Loop
    
    Loop -->|No| Normalize[Normalize Data]
    Normalize --> Enrich[Enrich Data]
    Enrich --> SaveDate{Incremental?}
    SaveDate -->|Yes| SaveLast[Save Last Date]
    SaveDate -->|No| Export
    SaveLast --> Export[Export Data]
    
    Export --> Format{Export<br/>Format?}
    Format -->|Excel| Excel[Generate Excel]
    Format -->|CSV| CSV[Generate CSV]
    Format -->|JSON| JSON[Generate JSON]
    Format -->|SQL| SQL[Generate SQL]
    
    Excel --> Compress{Large<br/>File?}
    CSV --> Compress
    JSON --> Compress
    SQL --> Compress
    
    Compress -->|Yes| Zip[Compress File]
    Compress -->|No| Dest
    Zip --> Dest{Send to<br/>Destination?}
    
    Dest -->|Yes| Send{Destination<br/>Type?}
    Dest -->|No| Complete
    
    Send -->|Blob| Blob[Upload to Blob]
    Send -->|Email| Email[Send Email]
    Send -->|Share| Share[Upload to SharePoint]
    
    Blob --> Complete([Complete])
    Email --> Complete
    Share --> Complete
    
    Error1 --> End([End])
    Complete --> Display[Display Results]
    Display --> End
    
    style Start fill:#4CAF50
    style End fill:#4CAF50
    style Complete fill:#2196F3
    style Error1 fill:#F44336
    style Token fill:#FF9800
    style Export fill:#9C27B0
```

## Arquitectura de Despliegue

```mermaid
graph TB
    subgraph "User Environment"
        DEV[Developer Machine]
        USER[End User]
    end
    
    subgraph "Azure Cloud"
        subgraph "App Services"
            UI_APP[Blazor UI<br/>App Service]
            CLI_FUNC[CLI Functions<br/>Azure Functions]
        end
        
        subgraph "Storage"
            BLOB[Blob Storage<br/>Exports]
            LOGS[Log Analytics]
        end
        
        subgraph "Security"
            KV[Key Vault<br/>Secrets]
            MI[Managed<br/>Identity]
        end
        
        subgraph "Monitoring"
            APPINS[Application<br/>Insights]
        end
    end
    
    subgraph "Microsoft 365"
        DV[Dataverse<br/>Environment]
        AAD[Azure Active<br/>Directory]
    end
    
    DEV -->|Deploy| UI_APP
    DEV -->|Deploy| CLI_FUNC
    USER -->|Access| UI_APP
    USER -->|Run| CLI_FUNC
    
    UI_APP -->|Authenticate| MI
    CLI_FUNC -->|Authenticate| MI
    MI -->|Get Token| AAD
    AAD -->|Authorize| DV
    
    UI_APP -->|Get Secrets| KV
    CLI_FUNC -->|Get Secrets| KV
    
    UI_APP -->|Extract| DV
    CLI_FUNC -->|Extract| DV
    
    UI_APP -->|Store| BLOB
    CLI_FUNC -->|Store| BLOB
    
    UI_APP -->|Log| LOGS
    CLI_FUNC -->|Log| LOGS
    UI_APP -->|Telemetry| APPINS
    CLI_FUNC -->|Telemetry| APPINS
    
    style DEV fill:#e1f5ff
    style USER fill:#e1f5ff
    style DV fill:#ffebee
    style KV fill:#fff3e0
    style BLOB fill:#e8f5e9
```

## Modelo de Estados de Extracción

```mermaid
stateDiagram-v2
    [*] --> Initialized: Create Command
    
    Initialized --> Validating: Validate Input
    Validating --> Error: Invalid Input
    Validating --> Authenticating: Valid Input
    
    Authenticating --> Error: Auth Failed
    Authenticating --> Connecting: Auth Success
    
    Connecting --> Error: Connection Failed
    Connecting --> CheckingMode: Connected
    
    CheckingMode --> GettingLastDate: Incremental Mode
    CheckingMode --> Querying: Normal Mode
    GettingLastDate --> Querying
    
    Querying --> Extracting: Query Built
    
    Extracting --> Processing: Records Retrieved
    Extracting --> Extracting: More Pages
    
    Processing --> Normalizing: Processed
    Normalizing --> Enriching: Normalized
    Enriching --> SavingDate: Enriched
    
    SavingDate --> Exporting: Incremental Mode
    Enriching --> Exporting: Normal Mode
    
    Exporting --> Compressing: Large File
    Exporting --> SendingToDestination: Small File
    Compressing --> SendingToDestination
    
    SendingToDestination --> Completed: Destination Configured
    Exporting --> Completed: No Destination
    SendingToDestination --> Completed: Sent
    
    Completed --> [*]
    Error --> [*]
```

## Diagrama de Componentes

```mermaid
graph LR
    subgraph "Frontend"
        CLI_COMP[CLI Component]
        UI_COMP[UI Component]
    end
    
    subgraph "Core"
        APP_CORE[Application Core<br/>Use Cases & Business Logic]
        DOM_CORE[Domain Core<br/>Entities & Interfaces]
    end
    
    subgraph "Infrastructure"
        AUTH_INFRA[Authentication<br/>Infrastructure]
        DATA_INFRA[Data Access<br/>Infrastructure]
        EXP_INFRA[Export<br/>Infrastructure]
        CACHE_INFRA[Caching<br/>Infrastructure]
    end
    
    subgraph "External"
        DV_EXT[Dataverse API]
        KV_EXT[Key Vault]
        BLOB_EXT[Blob Storage]
    end
    
    CLI_COMP --> APP_CORE
    UI_COMP --> APP_CORE
    
    APP_CORE --> DOM_CORE
    
    APP_CORE --> AUTH_INFRA
    APP_CORE --> DATA_INFRA
    APP_CORE --> EXP_INFRA
    APP_CORE --> CACHE_INFRA
    
    AUTH_INFRA --> DV_EXT
    AUTH_INFRA --> KV_EXT
    DATA_INFRA --> DV_EXT
    EXP_INFRA --> BLOB_EXT
    
    style CLI_COMP fill:#90CAF9
    style UI_COMP fill:#90CAF9
    style APP_CORE fill:#FFE082
    style DOM_CORE fill:#CE93D8
    style AUTH_INFRA fill:#A5D6A7
    style DATA_INFRA fill:#A5D6A7
    style EXP_INFRA fill:#A5D6A7
    style CACHE_INFRA fill:#A5D6A7
```

---

**Nota:** Estos diagramas pueden ser visualizados usando herramientas que soporten Mermaid, como:
- GitHub (visualización automática)
- VS Code con extensión Mermaid
- draw.io con plugin Mermaid
- Sitio web mermaid.live
