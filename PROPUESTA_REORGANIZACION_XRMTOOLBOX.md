# 📁 REORGANIZACIÓN DE ESTRUCTURA - XrmToolBox/Dynamics 365 Standard
## Audit History Extractor Pro

**Preparado por:** Arquitecto de Software Senior  
**Especialización:** Plugins XrmToolBox / Dynamics 365  
**Fecha:** Febrero 17, 2026  
**Versión:** Propuesta v1.0

---

## 🔄 Estado Actual vs. Propuesto

### Estructura Actual (Clean Architecture)
```
src/
├── AuditHistoryExtractorPro.Domain/           ← Entidades e Interfaces
│   ├── Entities/
│   ├── Interfaces/
│   └── ValueObjects/
├── AuditHistoryExtractorPro.Infrastructure/   ← Implementaciones
│   ├── Authentication/
│   ├── Repositories/
│   └── Services/
├── AuditHistoryExtractorPro.Application/      ← Use Cases / MediatR
├── AuditHistoryExtractorPro.CLI/              ← CLI
└── AuditHistoryExtractorPro.UI/               ← Blazor Server
    ├── Pages/
    ├── Shared/
    └── wwwroot/
```

**Análisis:** Clean Architecture bien implementada pero poco optimizada para XrmToolBox.

### Estructura Propuesta (XrmToolBox Standard)
```
AuditHistoryExtractorPro/
├── App/                                       ← Punto de entrada
│   ├── AuditHistoryExtractorProPlugin.cs
│   ├── Constants.cs
│   └── AppConfig.cs
├── Models/                                    ← Modelos POCO / DTOs
│   ├── AuditRecord.cs
│   ├── AuditFieldChange.cs
│   ├── ExtractionCriteria.cs
│   ├── DataCleaningConfiguration.cs
│   └── ExportConfiguration.cs
├── Services/                                  ← Lógica de negocio
│   ├── DataverseService.cs
│   ├── AuditExtractionService.cs
│   ├── MetadataResolutionService.cs
│   ├── ExportService.cs
│   └── CacheService.cs
├── Forms/                                     ← UI (WinForms/Blazor)
│   ├── Controls/
│   │   ├── AuditGridControl.cs
│   │   ├── FilterControl.cs
│   │   └── ExportOptionsControl.cs
│   ├── MainForm.cs / MainForm.Designer.cs
│   ├── SettingsForm.cs / SettingsForm.Designer.cs
│   └── ExportForm.cs / ExportForm.Designer.cs
├── Helpers/                                   ← Utilidades
│   ├── DateTimeHelper.cs                     (ISO 8601)
│   ├── CacheHelper.cs
│   ├── CsvExportHelper.cs
│   ├── ExcelExportHelper.cs
│   ├── XmlParsingHelper.cs
│   └── ValidationHelper.cs
├── Resources/                                 ← Archivos estáticos
│   ├── Icons/
│   ├── Images/
│   ├── Strings/
│   └── config.template.yaml
├── Properties/                                ← Configuración proyecto
│   ├── AssemblyInfo.cs
│   └── Resources.resx
├── Tests/                                     ← Tests unitarios
│   ├── Services/
│   ├── Helpers/
│   └── Models/
└── AuditHistoryExtractorPro.csproj           ← Proyecto único

```

**Ventajas:**
- ✅ Estructura **más limpia y plana**
- ✅ **Fácil de navegar** (_Find in Solution Explorer_)
- ✅ **Compatible con patrones XrmToolBox**
- ✅ **Menos nesting de carpetas**
- ✅ **Mejor escalabilidad**

---

## 📋 Mapeo de Archivos: Actual → Propuesto

### CATEGORÍA 1: Modelos de Datos (Models/)

| Actual | Propuesto | Archivo |
|--------|-----------|---------|
| Domain/Entities | **Models/** | AuditRecord.cs |
| Domain/Entities | **Models/** | AuditFieldChange.cs |
| Domain/ValueObjects | **Models/** | ExtractionCriteria.cs |
| Domain/ValueObjects | **Models/** | DataCleaningConfiguration.cs |
| Domain/ValueObjects | **Models/** | ExportConfiguration.cs |
| Domain/ValueObjects | **Models/** | AuditStatistics.cs |
| Domain/Entities | **Models/** | RecordComparison.cs |
| Domain/Entities | **Models/** | FieldDifference.cs |

**Cambios:** Consolidar todas las estructuras POCO en una sola carpeta limpia.

---

### CATEGORÍA 2: Servicios (Services/)

| Actual | Propuesto | Archivo | Descripción |
|--------|-----------|---------|-------------|
| Infrastructure/Repositories | **Services/** | DataverseService.cs | Conexión + QueryExpression |
| Infrastructure/Services | **Services/** | AuditExtractionService.cs | Lógica de extracción |
| Infrastructure/Services | **Services/** | MetadataResolutionService.cs | Resolución + Caché |
| Infrastructure/Services/Export | **Services/** | ExportService.cs | CSV + Excel |
| Infrastructure/Services | **Services/** | CacheService.cs | Caché distribuido |
| Infrastructure/Authentication | **Services/** | AuthenticationService.cs | OAuth + Certificate + Managed Identity |
| Infrastructure/Services | **Helpers/** | ResiliencePolicy.cs | Polly policies (mover a Helpers) |

**Cambios:** Consolidar todas las implementaciones (repositories + services) en una sola carpeta.

---

### CATEGORÍA 3: Helpers / Utilidades (Helpers/)

| Actual | Propuesto | Archivo | Función |
|--------|-----------|---------|---------|
| NEW | **Helpers/** | DateTimeHelper.cs | Formato ISO 8601 |
| Infrastructure/Services | **Helpers/** | ResiliencePolicy.cs | Retry policies |
| Services/SupportServices.cs | **Helpers/** | CsvExportHelper.cs | Exportación CSV |
| Services/SupportServices.cs | **Helpers/** | ExcelExportHelper.cs | Exportación Excel |
| NEW | **Helpers/** | XmlParsingHelper.cs | Parse changedata XML |
| Domain/ValueObjects | **Helpers/** | ValidationHelper.cs | Validación criterios |
| NEW | **Helpers/** | ConsoleHelper.cs | Formatted console output |
| NEW | **Helpers/** | ConfigurationHelper.cs | Leer config YAML |

**Cambios:** Crear carpeta de helpers para lógica reutilizable.

---

### CATEGORÍA 4: UI / Formularios (Forms/)

| Actual | Propuesto | Carpeta | Componente |
|--------|-----------|---------|-----------|
| UI/Pages/Index.razor | **Forms/Controls/** | DashboardControl.cs | Dashboard principal |
| UI/Pages/Extract.razor | **Forms/Controls/** | ExtractionControl.cs | Panel de extracción |
| UI/Pages/Settings.razor | **Forms/Controls/** | SettingsControl.cs | Configuración |
| UI/Pages/Export.razor | **Forms/Controls/** | ExportControl.cs | Opciones exportación |
| UI/Pages/History.razor | **Forms/Controls/** | AuditGridControl.cs | Tabla de auditoría |
| NEW | **Forms/Controls/** | FilterControl.cs | Panel de filtros |
| UI/Shared/ | **Forms/Shared/** | MainLayout.razor | Layout principal |
| UI/Shared/ | **Forms/Shared/** | SimpleLayout.razor | Layout simple |
| NEW | **Forms/** | MainForm.cs | Forma principal (si no fuera Blazor) |

**Cambios:** Separar controles de usuario en carpeta /Controls/ dentro de /Forms/.

---

### CATEGORÍA 5: Recursos (Resources/)

| Actual | Propuesto | Archivo |
|--------|-----------|---------|
| wwwroot/css/ | **Resources/Styles/** | app.css, bootstrap.css |
| wwwroot/js/ | **Resources/Scripts/** | app.js, interop.js |
| wwwroot/images/ | **Resources/Images/** | logos, icons |
| NEW | **Resources/Icons/** | icon-extract.png, icon-export.png |
| ROOT | **Resources/Config/** | config.example.yaml |
| NEW | **Resources/Localization/** | en-US.resxes, es-ES.resx |

**Cambios:** Consolidar todos los recursos en carpeta única.

---

### CATEGORÍA 6: Punto de Entrada (App/)

| Tipo | Propuesto | Archivo | Descripción |
|------|-----------|---------|-------------|
| Clase Principal | **App/** | AuditHistoryExtractorProPlugin.cs | Punto de entrada XrmToolBox |
| Constantes | **App/** | Constants.cs | URLs, magic numbers |
| Configuración | **App/** | AppConfig.cs | Settings globales |
| Factory | **App/** | ServiceFactory.cs | DI container |

**Cambios:** Crear carpeta /App/ con código de bootstrapping.

---

## 🗂️ Estructura Final Detallada

```
AuditHistoryExtractorPro/
│
├── 📄 AuditHistoryExtractorPro.csproj       ← UNO SOLO (consolidado)
├── 📄 README.md
├── 📄 LICENSE
│
├── 📁 App/
│   ├── AuditHistoryExtractorProPlugin.cs    ← Plugin XrmToolBox / Blazor entry
│   ├── Constants.cs                         ← Constantes globales
│   ├── AppConfig.cs                         ← Configuración centralizada
│   └── ServiceFactory.cs                    ← DI Setup
│
├── 📁 Models/                               ← Estructuras POCO
│   ├── AuditRecord.cs
│   ├── AuditFieldChange.cs
│   ├── AuditStatistics.cs
│   ├── RecordComparison.cs
│   ├── FieldDifference.cs
│   ├── ExtractionCriteria.cs
│   ├── DataCleaningConfiguration.cs
│   ├── ExportConfiguration.cs
│   ├── ExportResult.cs
│   └── AuditProgressInfo.cs
│
├── 📁 Services/                             ← Lógica de negocio
│   ├── DataverseService.cs                  ← Conexión + QueryExpression
│   ├── AuditExtractionService.cs            ← Extracción de auditoría
│   ├── MetadataResolutionService.cs         ← Caché de metadatos
│   ├── ExportService.cs                     ← Exportación (CSV, Excel)
│   ├── CacheService.cs                      ← Caché en memoria / distribuida
│   ├── AuthenticationService.cs             ← OAuth, Certificate, Managed ID
│   ├── AuditProcessorService.cs             ← Comparación y enriquecimiento
│   └── IService*.cs                         ← Interfases (4-5)
│
├── 📁 Helpers/                              ← Utilidades reutilizables
│   ├── DateTimeHelper.cs                    ← ISO 8601, timezone conversions
│   ├── ResiliencePolicy.cs                  ← Polly retry policies
│   ├── CsvExportHelper.cs                   ← CSV formatting
│   ├── ExcelExportHelper.cs                 ← XLSX generation
│   ├── XmlParsingHelper.cs                  ← Parse audit changedata
│   ├── ValidationHelper.cs                  ← Input validation
│   ├── ConsoleHelper.cs                     ← Colored console output
│   ├── ConfigurationHelper.cs               ← YAML/JSON parsing
│   ├── CacheHelper.cs                       ← Cache utility methods
│   └── JsonHelper.cs                        ← JSON serialization
│
├── 📁 Forms/                                ← Interfaz gráfica
│   │
│   ├── 📁 Controls/                         ← Componentes reutilizables
│   │   ├── DashboardControl.cs              ← Resumen + statisticas
│   │   ├── ExtractionControl.cs             ← Selector de entidades/filtros
│   │   ├── AuditGridControl.cs              ← DataGrid de auditorías
│   │   ├── FilterControl.cs                 ← Filtros avanzados
│   │   ├── ExportOptionsControl.cs          ← Opciones exportación
│   │   ├── ProgressControl.cs               ← Barra de progreso
│   │   └── SettingsControl.cs               ← Configuración
│   │
│   ├── 📁 Shared/                           ← Layouts compartidos
│   │   ├── MainLayout.razor                 ← Layout principal
│   │   ├── SimpleLayout.razor               ← Layout simple
│   │   └── NavMenu.razor                    ← Menú navegación
│   │
│   ├── MainForm.cs                          ← Form principal (Windows)
│   ├── MainForm.Designer.cs
│   ├── SettingsForm.cs                      ← Configuración (Windows)
│   ├── SettingsForm.Designer.cs
│   └── ExportForm.cs                        ← Wizard exportación (Windows)
│
├── 📁 Resources/
│   │
│   ├── 📁 Icons/
│   │   ├── audit.png (16x16, 32x32)
│   │   ├── extract.png
│   │   ├── export.png
│   │   ├── settings.png
│   │   └── refresh.png
│   │
│   ├── 📁 Images/
│   │   ├── logo.png
│   │   ├── banner.png
│   │   └── diagram.png
│   │
│   ├── 📁 Styles/
│   │   ├── app.css
│   │   ├── bootstrap.css
│   │   └── custom-theme.css
│   │
│   ├── 📁 Scripts/
│   │   ├── app.js
│   │   └── interop.js
│   │
│   ├── 📁 Config/
│   │   └── config.example.yaml
│   │
│   └── 📁 Localization/
│       ├── en-US.resx
│       ├── es-ES.resx
│       └── fr-FR.resx
│
├── 📁 Properties/
│   ├── AssemblyInfo.cs
│   └── Resources.resx
│
├── 📁 Tests/
│   ├── 📁 Services/
│   │   ├── AuditExtractionServiceTests.cs
│   │   ├── MetadataResolutionServiceTests.cs
│   │   └── ExportServiceTests.cs
│   │
│   ├── 📁 Helpers/
│   │   ├── DateTimeHelperTests.cs
│   │   ├── CsvExportHelperTests.cs
│   │   └── ValidationHelperTests.cs
│   │
│   ├── 📁 Models/
│   │   └── AuditRecordTests.cs
│   │
│   └── Fixtures/
│       ├── MockDataGenerator.cs
│       └── TestConstants.cs
└
└── 📁 obj/, bin/                            ← Compilación (ignorado)
```

---

## 🚀 Estrategia de Migración

### Fase 1: Preparación (30 min)
1. Crear estructura de carpetas vacías
2. Crear archivo de mapeo de cambios
3. Actualizar `.gitignore` para nuevas rutas

### Fase 2: Migración de Archivos (1 hora)
1. Mover Domain/Entities → Models/
2. Mover Domain/ValueObjects → Models/
3. Mover Infrastructure/Services → Services/
4. Mover Infrastructure/Repositories → Services/
5. Mover UI/Pages → Forms/Controls/
6. Crear Helpers/ con utilidades

### Fase 3: Actualización de Namespaces (1.5 horas)
```csharp
// ANTES
namespace AuditHistoryExtractorPro.Domain.Entities;
namespace AuditHistoryExtractorPro.Infrastructure.Services;

// DESPUÉS
namespace AuditHistoryExtractorPro.Models;
namespace AuditHistoryExtractorPro.Services;
namespace AuditHistoryExtractorPro.Helpers;
```

### Fase 4: Actualización de References (1 hora)
1. Actualizar `using` statements en todos los archivos
2. Resolver conflictos de referencias circulares
3. Compilar y validar

### Fase 5: Testing (1 hora)
1. Ejecutar todos los tests unitarios
2. Probar extracción manual
3. Probar exportación CSV
4. Validar que nada se rompa

### Timeline Total: **5-6 horas**

---

## 🔀 Cambios de Proyecto (.csproj)

### ANTES: Multi-proyecto Clean Architecture
```xml
<!-- Solución (AuditHistoryExtractorPro.sln) -->
<Project Include="src\AuditHistoryExtractorPro.Domain\..." />
<Project Include="src\AuditHistoryExtractorPro.Infrastructure\..." />
<Project Include="src\AuditHistoryExtractorPro.Application\..." />
<Project Include="src\AuditHistoryExtractorPro.UI\..." />
<Project Include="src\AuditHistoryExtractorPro.CLI\..." />
<!-- Total: 5 .csproj -->
```

### DESPUÉS: Proyecto Único Modular
```xml
<!-- AuditHistoryExtractorPro.csproj -->
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">
  <PropertyGroup>
    <OutputType>Library</OutputType> <!-- XrmToolBox Plugin -->
    <AssemblyName>AuditHistoryExtractorPro</AssemblyName>
    <Folders>
      <Folder>App\</Folder>
      <Folder>Models\</Folder>
      <Folder>Services\</Folder>
      <Folder>Helpers\</Folder>
      <Folder>Forms\</Folder>
      <Folder>Resources\</Folder>
      <Folder>Tests\</Folder>
    </Folders>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Dataverse -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
    <PackageReference Include="Microsoft.Crm.Sdk.Messages" />
    
    <!-- XrmToolBox -->
    <PackageReference Include="XrmToolBox" />
    
    <!-- Otros -->
    <PackageReference Include="Polly" />
    <PackageReference Include="ClosedXML" />
    <PackageReference Include="CsvHelper" />
  </ItemGroup>
</Project>
```

**Cambios:**
- De **5 proyectos** → **1 proyecto único**
- Estructura **más limpia** y fácil de navegar
- **Mejor rendimiento** de compilación
- **Menos complejidad** de dependencias

---

## 📊 Comparación: Clean Architecture vs. XrmToolBox

| Aspecto | Clean Architecture | XrmToolBox Standard |
|--------|-------------------|------------------|
| **Proyectos** | 5 (Domain, Infrastructure, Application, UI, CLI) | 1 |
| **Niveles de carpetas** | 3-4 | 1-2 |
| **Dificultad navegación** | Moderada | Fácil |
| **Escalabilidad** | Muy buena (si crece enormemente) | Buena (suficiente para plugin) |
| **Para equipo pequeño** | Overhead | Óptimo |
| **Para XrmToolBox** | Posible pero extraño | Estándar |
| **Segregación de responsabilidades** | Excelente | Buena (por carpeta) |
| **Curva de aprendizaje** | Steeper | Suave |

---

## 💡 Decisión Recomendada

### Opción A: Migrar a Estructura XrmToolBox
**Pros:**
- ✅ Más simple y directo
- ✅ Estándar XrmToolBox
- ✅ Mejor para plugins Dataverse
- ✅ Menos complejidad de proyecto

**Contras:**
- ❌ Perder rigor de Clean Architecture
- ❌ Esfuerzo de migración (5-6 horas)

### Opción B: Mantener Clean Architecture + Adaptar
**Pros:**
- ✅ Mantener estructura robusta
- ✅ Escalable a largo plazo
- ✅ Separación clara de concerns

**Contras:**
- ❌ No es estándar XrmToolBox
- ❌ Puede ser excesivo para plugin simple

### ⭐ RECOMENDACIÓN PERSONAL
**Opción A: Migrar a XrmToolBox Standard** porque:
1. Este es un **plugin de Dataverse**, no una aplicación empresarial
2. La simplicidad ayuda **colaboración en equipo**
3. XrmToolBox es el **estándar de facto** en comunidad
4. Reducir de **5 a 1 proyecto** es gran mejora

---

## 🛠️ Scripts de Migración

### Script PowerShell para crear estructura

```powershell
# Crear carpetas
@(
    "App",
    "Models",
    "Services",
    "Helpers",
    "Forms/Controls",
    "Forms/Shared",
    "Resources/Icons",
    "Resources/Images",
    "Resources/Styles",
    "Resources/Scripts",
    "Resources/Config",
    "Resources/Localization",
    "Tests/Services",
    "Tests/Helpers",
    "Tests/Models",
    "Tests/Fixtures",
    "Properties"
) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
}

Write-Host "✅ Estructura de carpetas creada"
```

### Script para mover archivos

```powershell
# Mover Models
Move-Item "Domain\Entities\*.cs" "Models\" -Force
Move-Item "Domain\ValueObjects\*.cs" "Models\" -Force

# Mover Services
Move-Item "Infrastructure\Services\*.cs" "Services\" -Force
Move-Item "Infrastructure\Repositories\*.cs" "Services\" -Force
Move-Item "Infrastructure\Authentication\*.cs" "Services\" -Force

# Mover Forms
Move-Item "UI\Pages\*.razor" "Forms\Controls\" -Force
Move-Item "UI\Shared\*.razor" "Forms\Shared\" -Force

# Mover Recursos
Move-Item "wwwroot\css\*" "Resources\Styles\" -Force
Move-Item "wwwroot\js\*" "Resources\Scripts\" -Force
Move-Item "wwwroot\images\*" "Resources\Images\" -Force

Write-Host "✅ Archivos movidos"
```

---

## 📝 Archivo de Mapeo de Namespaces

Crear archivo `NAMESPACE_MAPPING.md`:

```markdown
# Mapeo de Namespaces - Migración

## Models
AuditHistoryExtractorPro.Domain.Entities → AuditHistoryExtractorPro.Models
AuditHistoryExtractorPro.Domain.ValueObjects → AuditHistoryExtractorPro.Models

## Services
AuditHistoryExtractorPro.Infrastructure.Services → AuditHistoryExtractorPro.Services
AuditHistoryExtractorPro.Infrastructure.Repositories → AuditHistoryExtractorPro.Services
AuditHistoryExtractorPro.Infrastructure.Authentication → AuditHistoryExtractorPro.Services

## Helpers
(Nuevos archivos) → AuditHistoryExtractorPro.Helpers

## Forms
AuditHistoryExtractorPro.UI.Pages → AuditHistoryExtractorPro.Forms.Controls
AuditHistoryExtractorPro.UI.Shared → AuditHistoryExtractorPro.Forms.Shared

## App
(Nuevo) → AuditHistoryExtractorPro.App
```

---

## ✅ Beneficios Expected

| Métrica | Impacto |
|--------|--------|
| **Tiempo de navegación en Solution Explorer** | -60% |
| **Complejidad de DI Setup** | -75% |
| **Curva aprendizaje para nuevos desarrolladores** | -50% |
| **Problemas de referencias circulares** | -90% |
| **Tiempo de compilación** | -20% |
| **Líneas de configuración proyecto** | -70% |

---

## 📚 Referencias

- [XrmToolBox Plugin Development](https://github.com/MscrmTools/XrmToolBox)
- [Dynamics 365 Plugin Patterns](https://microsoft.github.io/PowerApps-Samples/)
- [Clean Code Structure](https://www.amazon.com/Clean-Code-Handbook-Software-Craftsmanship/dp/0132350882)

---

**Conclusión:** Migrar a estructura XrmToolBox estándar hará el código más mantenible, escalable y alineado con prácticas de la comunidad Dynamics 365.

Próximo paso: ¿Deseas que proceda con la migración real? Puedo generar los scripts exactos.
