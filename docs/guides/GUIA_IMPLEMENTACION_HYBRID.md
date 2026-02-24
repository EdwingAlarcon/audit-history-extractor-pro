# 🏗️ GUÍA DE IMPLEMENTACIÓN - ARQUITECTURA HÍBRIDA
## Audit History Extractor Pro

**Fecha:** Febrero 17, 2026  
**Estrategia:** Carpetas XrmToolBox + Organización Clean interna  
**Tiempo Estimado:** 4-5 horas  
**Complejidad:** Media  
**Riesgo:** Bajo  

---

## 🎯 Objetivo Híbrido

```
                   EXTERIOR (XrmToolBox)
┌──────────────────────────────────────────────────────┐
│ App/    Models/    Services/    Helpers/    Forms/   │
│                                                      │
│                  INTERIOR (Clean)                    │
│              en Services/ subdirectorios              │
│              ├── Core/         [Domain logic]        │
│              ├── Infrastructure/ [Dataverse]         │
│              └── Utilities/    [Helpers]             │
└──────────────────────────────────────────────────────┘

Beneficio:
- ✅ Comunidad: Entiende estructura en 30 segundos
- ✅ Desarrollo: Puede escalar sin problemas
- ✅ Testing: Servicios bien organizados = fácil testear
- ✅ Mantenibilidad: Lo mejor de ambos mundos
```

---

## 📁 ESTRUCTURA FINAL

```
AuditHistoryExtractorPro/
│
├── Properties/
│   └── AssemblyInfo.cs
│
├── App/
│   ├── AuditHistoryExtractorPlugin.cs          [IXrmToolBoxPluginControl]
│   ├── Constants.cs                             [Constantes globales]
│   └── AppConfig.cs                             [Configuración app]
│
├── Models/                                      [Sin dependencias externas]
│   ├── AuditRecord.cs
│   ├── AuditFieldChange.cs
│   ├── ExtractionCriteria.cs
│   ├── ExportConfiguration.cs
│   ├── AuditActionCode.cs                       [Enum 30 opciones]
│   ├── AuditCategory.cs                         [Enum categorías]
│   └── DataCleaningConfiguration.cs
│
├── Services/                                    [Organización CLEAN]
│   ├── Core/                                    [Domain logic]
│   │   ├── IAuditExtractionService.cs           [Interface]
│   │   ├── AuditExtractionService.cs
│   │   ├── IAuditProcessor.cs
│   │   └── AuditProcessor.cs
│   │
│   ├── Infrastructure/                         [Dataverse + externos]
│   │   ├── IDataverseService.cs
│   │   ├── DataverseService.cs
│   │   ├── IAuthenticationService.cs
│   │   ├── AuthenticationService.cs
│   │   ├── IMetadataResolutionService.cs        [🆕 Caché de metadata]
│   │   └── MetadataResolutionService.cs
│   │
│   ├── Export/                                  [Exportadores específicos]
│   │   ├── IExportService.cs
│   │   ├── ExcelExportService.cs
│   │   ├── CsvExportService.cs
│   │   ├── PowerBIOptimizedCsvExportService.cs  [🆕 Power BI]
│   │   └── JsonExportService.cs
│   │
│   ├── Cache/                                   [Cache utilities]
│   │   ├── ICacheService.cs
│   │   └── MemoryCacheService.cs
│   │
│   └── Resilience/                              [Polly policies]
│       ├── IResiliencePolicy.cs
│       └── ResiliencePolicy.cs                  [🆕 429 handling]
│
├── Helpers/                                     [Utilities sin estado]
│   ├── DateTimeHelper.cs                        [Conversiones datetime]
│   ├── CsvExportHelper.cs
│   ├── ExcelExportHelper.cs
│   ├── ValidationHelper.cs
│   ├── ConfigurationHelper.cs
│   └── XmlParsingHelper.cs
│
├── Forms/
│   ├── Controls/
│   │   ├── DashboardControl.razor               [UI principal]
│   │   ├── ExtractionControl.razor
│   │   ├── AuditGridControl.razor
│   │   ├── FilterControl.razor
│   │   ├── ExportOptionsControl.razor
│   │   ├── SettingsControl.razor                [🆕 DataCleaningConfig]
│   │   └── *.razor.cs                           [Code-behind]
│   │
│   ├── Shared/
│   │   ├── MainLayout.razor
│   │   └── SimpleLayout.razor
│   │
│   └── Styles/
│       ├── app.css
│       └── _imports.razor
│
├── Resources/
│   ├── Icons/
│   ├── Images/
│   ├── Config/
│   │   └── DefaultConfig.yaml
│   ├── Localization/
│   │   └── es-ES.json
│   └── Scripts/
│       └── common.js
│
├── Tests/
│   ├── Services/
│   │   ├── AuditExtractionServiceTests.cs
│   │   ├── MetadataResolutionServiceTests.cs
│   │   └── ResiliencePolicyTests.cs
│   │
│   ├── Helpers/
│   │   └── DateTimeHelperTests.cs
│   │
│   └── Integration/
│       └── EndToEndTests.cs
│
└── AuditHistoryExtractorPro.csproj
    ├── OutputType: Library
    ├── TargetFramework: net8.0-windows
    └── Embedded resources: Icons, Config
```

---

## 🔄 MAPEO DE MIGRACION

### FROM (Actual - 5 proyectos) → TO (Híbrido - 1 proyecto)

| Actual | Nuevo | Ubicación | Notas |
|--------|-------|-----------|-------|
| **AuditHistoryExtractorPro.Domain** | | | |
| Entities/AuditRecord.cs | Models/AuditRecord.cs | `/Models` | Sin cambios |
| Entities/AuditFieldChange.cs | Models/AuditFieldChange.cs | `/Models` | Sin cambios |
| ValueObjects/Configuration.cs | Models/AuditActionCode.cs + DataCleaningConfiguration.cs | `/Models` | Dividido en 2 archivos |
| Interfaces/IRepositories.cs | Services/Core/IAuditExtractionService.cs + Infrastructure/IDataverseService.cs | `/Services` | Refactorizado por concern |
| | | | |
| **AuditHistoryExtractorPro.Infrastructure** | | | |
| Repositories/DataverseAuditRepository.cs | Services/Core/AuditExtractionService.cs | `/Services/Core` | Renombrado (no es un repo) |
| Services/ExportServices.cs | Services/Export/*.cs | `/Services/Export` | Dividido por exportador |
| Services/MetadataResolutionService.cs | Services/Infrastructure/MetadataResolutionService.cs | `/Services/Infrastructure` | Movido |
| Services/ResiliencePolicy.cs | Services/Resilience/ResiliencePolicy.cs | `/Services/Resilience` | Movido |
| Authentication/AuthenticationProviders.cs | Services/Infrastructure/AuthenticationService.cs | `/Services/Infrastructure` | Refactorizado |
| | | | |
| **AuditHistoryExtractorPro.Application** | | | |
| UseCases/ExtractAudit/ExtractAuditCommand.cs | Services/Core/AuditExtractionService.cs | `/Services/Core` | Lógica integrada |
| UseCases/ExportAudit/ExportAuditCommand.cs | Services/Export/ExportOrchestrator.cs | `/Services/Export` | Nuevo orquestador |
| UseCases/CompareRecords/CompareRecordsQuery.cs | Services/Core/ComparisonService.cs | `/Services/Core` | Nuevo servicio |
| | | | |
| **AuditHistoryExtractorPro.UI** | | | |
| Pages/Index.razor | Forms/Controls/DashboardControl.razor | `/Forms/Controls` | Renombrado |
| Pages/Extract.razor | Forms/Controls/ExtractionControl.razor | `/Forms/Controls` | Renombrado |
| Pages/Export.razor | Forms/Controls/ExportOptionsControl.razor | `/Forms/Controls` | Renombrado |
| Pages/History.razor | Forms/Controls/AuditGridControl.razor | `/Forms/Controls` | Renombrado |
| Pages/Settings.razor | Forms/Controls/SettingsControl.razor | `/Forms/Controls` | Renombrado |
| Shared/MainLayout.razor | Forms/Shared/MainLayout.razor | `/Forms/Shared` | Movido |
| wwwroot/css/app.css | Forms/Styles/app.css | `/Forms/Styles` | Movido |
| | | | |
| **AuditHistoryExtractorPro.CLI** | | | |
| Program.cs | App/CliProgram.cs | `/App` | Integrado o referencia |
| Commands/Commands.cs | CLI/Commands.cs | `/CLI` (nuevo si se mantiene) | Opcional |

---

## 🚀 PLAN DE EJECUCIÓN (4-5 horas)

### FASE 0: Preparación (30 min)

```powershell
# 1. Crear rama feature
cd c:\Users\bdp_u\Documents\Repos\audit-history-extractor-pro
git checkout -b refactor/hybrid-architecture
git pull origin main

# 2. Crear backup
Copy-Item -Path ".\src\" -Destination ".\src_backup\" -Recurse

# 3. Ver estado inicial
git status
```

### FASE 1: Crear Estructura de Carpetas (30 min)

```powershell
# Estamos en raíz del repo
Push-Location src

# 1. Crear carpetas principales
@(
    "App",
    "Models", 
    "Services/Core",
    "Services/Infrastructure",
    "Services/Export",
    "Services/Cache",
    "Services/Resilience",
    "Helpers",
    "Forms/Controls",
    "Forms/Shared",
    "Forms/Styles",
    "Resources/Icons",
    "Resources/Images",
    "Resources/Config",
    "Resources/Localization",
    "Resources/Scripts",
    "Tests/Services",
    "Tests/Helpers",
    "Tests/Integration",
    "CLI"
) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
    Write-Host "✓ Created: $_"
}

# 2. Crear .gitkeep en carpetas vacías (para que git las rastree)
Get-ChildItem -Directory -Recurse | ForEach-Object {
    if ((Get-ChildItem $_ -Force | Measure-Object).Count -eq 0) {
        New-Item -Path "$_\.gitkeep" -ItemType File -Force | Out-Null
    }
}

Pop-Location

Write-Host "✅ Estructura de carpetas creada"
```

### FASE 2: Mover Archivos Principales (1.5 horas)

#### 2.1 Mover Models (15 min)

```powershell
Push-Location src

# Copiar entidades a Models/
Copy-Item "AuditHistoryExtractorPro.Domain\Entities\*.cs" -Destination "Models\" -Force
Copy-Item "AuditHistoryExtractorPro.Domain\ValueObjects\*.cs" -Destination "Models\" -Force

# Verificar
Get-ChildItem Models/ -Filter "*.cs" | Select-Object Name

Pop-Location
```

#### 2.2 Mover Services (45 min)

```powershell
Push-Location src

# Core Services (Audit extraction, processing)
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Repositories\DataverseAuditRepository.cs" `
    -Destination "Services\Core\AuditExtractionService.cs" -Force
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Services\SupportServices.cs" `
    -Destination "Services\Core\AuditProcessor.cs" -Force

# Infrastructure Services (Dataverse, Auth, Metadata)
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Authentication\*.cs" `
    -Destination "Services\Infrastructure\" -Force
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Services\MetadataResolutionService.cs" `
    -Destination "Services\Infrastructure\" -Force

# Export Services
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Services\Export\*.cs" `
    -Destination "Services\Export\" -Force
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Services\ExportServices.cs" `
    -Destination "Services\Export\ExportOrchestrator.cs" -Force

# Resilience & Cache
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Services\ResiliencePolicy.cs" `
    -Destination "Services\Resilience\" -Force
Copy-Item "AuditHistoryExtractorPro.Infrastructure\Repositories\*.cs" `
    -Destination "Services\Cache\" -Filter "*Cache*" -Force

Write-Host "✅ Services movidos"
Pop-Location
```

#### 2.3 Mover UI (30 min)

```powershell
Push-Location src

# Componentes Blazor → Controls
Copy-Item "AuditHistoryExtractorPro.UI\Pages\Index.razor*" `
    -Destination "Forms\Controls\DashboardControl.razor" -Force
Copy-Item "AuditHistoryExtractorPro.UI\Pages\Extract.razor*" `
    -Destination "Forms\Controls\ExtractionControl.razor" -Force
Copy-Item "AuditHistoryExtractorPro.UI\Pages\Export.razor*" `
    -Destination "Forms\Controls\ExportOptionsControl.razor" -Force
Copy-Item "AuditHistoryExtractorPro.UI\Pages\History.razor*" `
    -Destination "Forms\Controls\AuditGridControl.razor" -Force
Copy-Item "AuditHistoryExtractorPro.UI\Pages\Settings.razor*" `
    -Destination "Forms\Controls\SettingsControl.razor" -Force

# Layouts
Copy-Item "AuditHistoryExtractorPro.UI\Shared\*.razor" -Destination "Forms\Shared\" -Force

# Estilos
Copy-Item "AuditHistoryExtractorPro.UI\wwwroot\css\*.css" -Destination "Forms\Styles\" -Force

Write-Host "✅ UI components movidos"
Pop-Location
```

### FASE 3: Actualizar Namespaces (1.5 horas)

#### 3.1 Reemplazos Globales en VS Code

**Patrón 1: Domain Entities**
```
Find:    using AuditHistoryExtractorPro.Domain.Entities;
Replace: // Models en mismo namespace
```

**Patrón 2: Infrastructure Services**
```
Find:    using AuditHistoryExtractorPro.Infrastructure.Services;
Replace: using AuditHistoryExtractorPro.Services.Core;  // o Infrastructure o Export según contexto
```

**Patrón 3: Namespaces en archivos .cs**
```csharp
// Ejemplo: AuditRecord.cs
// FROM:
namespace AuditHistoryExtractorPro.Domain.Entities
{
    public class AuditRecord { ... }
}

// TO:
namespace AuditHistoryExtractorPro.Models
{
    public class AuditRecord { ... }
}
```

#### 3.2 Script de Reemplazo Global (PowerShell)

```powershell
Push-Location src

# Función auxiliar
function Replace-InFile {
    param([string]$Path, [string]$Find, [string]$Replace)
    
    $files = Get-ChildItem -Path $Path -Filter "*.cs" -Recurse
    
    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw
        
        if ($content -contains $Find) {
            $newContent = $content -replace [regex]::Escape($Find), $Replace
            Set-Content -Path $file.FullName -Value $newContent -Encoding UTF8
            Write-Host "✓ Updated: $($file.Name)"
        }
    }
}

# Mapeo de namespaces
$replacements = @(
    @{ Find = "namespace AuditHistoryExtractorPro.Domain.Entities"; Replace = "namespace AuditHistoryExtractorPro.Models" },
    @{ Find = "namespace AuditHistoryExtractorPro.Domain.ValueObjects"; Replace = "namespace AuditHistoryExtractorPro.Models" },
    @{ Find = "using AuditHistoryExtractorPro.Domain.Entities;"; Replace = "using AuditHistoryExtractorPro.Models;" },
    @{ Find = "namespace AuditHistoryExtractorPro.Infrastructure.Repositories"; Replace = "namespace AuditHistoryExtractorPro.Services.Core" },
    @{ Find = "namespace AuditHistoryExtractorPro.Infrastructure.Services"; Replace = "namespace AuditHistoryExtractorPro.Services.Core" },
    @{ Find = "namespace AuditHistoryExtractorPro.Infrastructure.Authentication"; Replace = "namespace AuditHistoryExtractorPro.Services.Infrastructure" },
    @{ Find = "using AuditHistoryExtractorPro.Infrastructure.Services;"; Replace = "using AuditHistoryExtractorPro.Services.Core;" },
    @{ Find = "using AuditHistoryExtractorPro.Infrastructure.Repositories;"; Replace = "using AuditHistoryExtractorPro.Services.Core;" },
)

foreach ($replacement in $replacements) {
    Replace-InFile -Path "." -Find $replacement.Find -Replace $replacement.Replace
}

Write-Host "✅ Namespaces actualizados"
Pop-Location
```

### FASE 4: Actualizar .csproj (30 min)

**NUEVO AuditHistoryExtractorPro.csproj:**

```xml
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">
    <PropertyGroup>
        <TargetFramework>net8.0-windows</TargetFramework>
        <UseWPF>false</UseWPF>
        <OutputType>Library</OutputType>
        <RootNamespace>AuditHistoryExtractorPro</RootNamespace>
        <AssemblyName>AuditHistoryExtractorPro</AssemblyName>
        <Version>2.0.0</Version>
        <Authors>Development Team</Authors>
        <Description>Enterprise-Grade Dataverse Audit Extraction Tool</Description>
        <LangVersion>latest</LangVersion>
        <Nullable>enable</Nullable>
    </PropertyGroup>

    <!-- NuGet Dependencies -->
    <ItemGroup>
        <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.26" />
        <PackageReference Include="Polly" Version="8.4.0" />
        <PackageReference Include="CsvHelper" Version="33.0.1" />
        <PackageReference Include="NPOI" Version="2.7.1" />
        <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
        <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
        <PackageReference Include="Serilog" Version="4.0.0" />
        
        <!-- Testing -->
        <PackageReference Include="xunit" Version="2.7.0" />
        <PackageReference Include="Moq" Version="4.20.70" />
        <PackageReference Include="FluentAssertions" Version="6.12.0" />
    </ItemGroup>

    <!-- Embedded Resources -->
    <ItemGroup>
        <EmbeddedResource Include="Resources/Config/**/*" />
        <EmbeddedResource Include="Resources/Localization/**/*" />
        <EmbeddedResource Include="Resources/Icons/**/*" />
    </ItemGroup>

    <!-- Include Razor Components -->
    <ItemGroup>
        <None Update="Forms/**/*.razor" CopyToOutputDirectory="Never" />
        <None Update="Forms/**/*.css" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
</Project>
```

**Pasos:**

```powershell
Push-Location src

# Eliminar viejos .csproj
Remove-Item "AuditHistoryExtractorPro.Domain\AuditHistoryExtractorPro.Domain.csproj" -Force
Remove-Item "AuditHistoryExtractorPro.Infrastructure\AuditHistoryExtractorPro.Infrastructure.csproj" -Force
Remove-Item "AuditHistoryExtractorPro.Application\AuditHistoryExtractorPro.Application.csproj" -Force
Remove-Item "AuditHistoryExtractorPro.UI\AuditHistoryExtractorPro.UI.csproj" -Force
Remove-Item "AuditHistoryExtractorPro.CLI\AuditHistoryExtractorPro.CLI.csproj" -Force

# El NUEVO csproj va en raíz de src/
# (Copiar contenido XML de arriba)

Pop-Location
```

### FASE 5: Validación y Testing (30 min)

```powershell
Push-Location src

# 1. Compilar solución
dotnet build

# 2. Ejecutar tests
dotnet test

# 3. Verificar referencias rota
dotnet build --verbosity detailed

# 4. Check namespaces
grep -r "AuditHistoryExtractorPro\.Domain" *.cs
grep -r "AuditHistoryExtractorPro\.Infrastructure" *.cs
# (Debería estar vacío)

Pop-Location
```

---

## 🎯 VENTAJAS HYBRID

### ✅ Al Implementar Este Enfoque

| Aspecto | Beneficio |
|---------|-----------|
| **Estructura Externa** | XrmToolBox Standard ✅ Comunidad entiende |
| **Organización Interna** | Clean Architecture ✅ Servicios bien segregados |
| **Compilación** | 1 .csproj ✅ Build rápido |
| **Testing** | Services/Core & Infrastructure ✅ Fácil de testear |
| **DI Container** | Simple ✅ 10-15 líneas de registro |
| **Escalabilidad** | Services/* crece sin problemas ✅ |
| **Navegación** | Find-in-Explorer instantáneo ✅ |
| **Nuevos Devs** | Entienden en 30 min vs. 1 semana ✅ |

---

## ⚠️ CHECKLIST PRE-EJECUCIÓN

- [ ] Rama creada: `git checkout -b refactor/hybrid-architecture`
- [ ] Backup realizado: `.\src_backup\` existe
- [ ] Tengo 4-5 horas disponibles sin interrupciones
- [ ] No hay cambios en progreso (git status limpio)
- [ ] Tengo acceso a PowerShell v5+
- [ ] IDE preparado (VS o VS Code)

---

## 🔄 ROLLBACK (si algo falla)

```powershell
# Opción 1: Git revert (seguro)
git reset --hard origin/main

# Opción 2: Restaurar backup
Remove-Item -Path ".\src" -Recurse -Force
Copy-Item -Path ".\src_backup" -Destination ".\src" -Recurse
```

---

## ✅ CHECKLIST POST-IMPLEMENTACIÓN

- [ ] `dotnet build` sin errores
- [ ] `dotnet test` 100% green
- [ ] Namespaces actualizados correctamente
- [ ] .gitignore updated (eliminar viejos .csproj)
- [ ] Solution Explorer es limpio (1 proyecto)
- [ ] Intellisense funciona en todos los archivos
- [ ] Git diff muestra cambios esperados
- [ ] PR creado y revisado

---

## 📋 PRÓXIMOS PASOS (POST-REFACTOR)

1. **Completar DI Registration**
   - Actualizar Program.cs o Startup.cs
   - Registrar todos los Services

2. **Integración de Servicios Empresariales**
   - Activar MetadataResolutionService
   - Activar ResiliencePolicy
   - Activar PowerBIOptimizedCsvExportService

3. **Testing & Validación**
   - Tests de servicios críticos
   - Manual test en Dataverse sandbox

4. **Documentación**
   - Actualizar README.md con nueva estructura
   - Crear DEVELOPMENT.md para nuevos devs

---

## 🎬 ¿LISTO PARA COMENZAR?

**Confirma que deseas empezar y especifica:**

1. ¿Debo ejecutar FASE 0-1 ahora (preparación + crear carpetas)?
2. ¿O prefieres que primero cree los archivos .cs de servicios refactorizados?
3. ¿Tienes CLI que quieras mantener o puedo integrar al App/?

Estoy listo para comenzar. Solo necesito tu confirmación.

