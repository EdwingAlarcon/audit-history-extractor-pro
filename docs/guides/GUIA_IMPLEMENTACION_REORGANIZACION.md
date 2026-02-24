# 🔧 GUÍA DE IMPLEMENTACIÓN - Reorganización XrmToolBox
## Audit History Extractor Pro

**Tiempo estimado:** 5-6 horas  
**Riesgo:** Bajo (migración mecánica)  
**Requiere testing:** Sí, 1 hora

---

## 📋 Pre-requisitos

- ✅ Git clone del repositorio
- ✅ Visual Studio/Rider
- ✅ Todos los cambios commiteados a rama `main`
- ✅ Tests pasando (green build)
- ✅ Backup o rama separada para rollback

```bash
# Crear rama de trabajo
git checkout -b refactor/reorganize-xrmtoolbox
```

---

## 🎯 FASE 1: Preparación (30 minutos)

### 1.1 Crear Estructura de Carpetas

```powershell
# Ejecutar desde raíz del proyecto
$carpetas = @(
    "App",
    "Models",
    "Services",
    "Helpers",
    "Forms",
    "Forms/Controls",
    "Forms/Shared",
    "Resources",
    "Resources/Icons",
    "Resources/Images",
    "Resources/Styles",
    "Resources/Scripts",
    "Resources/Config",
    "Resources/Localization",
    "Tests",
    "Tests/Services",
    "Tests/Helpers",
    "Tests/Models",
    "Tests/Fixtures",
    "Properties"
)

foreach ($carpeta in $carpetas) {
    if (!(Test-Path $carpeta)) {
        New-Item -ItemType Directory -Path $carpeta -Force | Out-Null
        Write-Host "✅ Creada: $carpeta"
    }
}

Write-Host "`n✅ Estructura de carpetas lista"
```

### 1.2 Crear Archivos de Placeholder

```powershell
# Crear archivos principales en carpetas clave
@"
namespace AuditHistoryExtractorPro.App;

/// <summary>
/// Punto de entrada del plugin XrmToolBox
/// </summary>
public class AuditHistoryExtractorProPlugin
{
    // TODO: Implementar IXrmToolBoxPluginControl
}
"@ | Set-Content "App/AuditHistoryExtractorProPlugin.cs"

Write-Host "✅ Archivos placeholder creados"
```

### 1.3 Crear Archivo de Mapeo

```powershell
@"
# Mapeo de Migración - Creado: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

## Archivos por migrar

### Models (Domain/Entities → Models/)
- [ ] AuditRecord.cs
- [ ] AuditFieldChange.cs
- [ ] AuditStatistics.cs
- [ ] RecordComparison.cs
- [ ] FieldDifference.cs

### Models (Domain/ValueObjects → Models/)
- [ ] ExtractionCriteria.cs
- [ ] AuditActionCode.cs (enum)
- [ ] DataCleaningConfiguration.cs
- [ ] ExportConfiguration.cs
- [ ] AuthenticationConfiguration.cs

### Services (Infrastructure/Services → Services/)
- [ ] ExportServices.cs
- [ ] SupportServices.cs
- [ ] MetadataResolutionService.cs
- [ ] ResiliencePolicy.cs → Helpers/

### Services (Infrastructure/Repositories → Services/)
- [ ] DataverseAuditRepository.cs

### Services (Infrastructure/Authentication → Services/)
- [ ] AuthenticationProviders.cs

### Helpers (New)
- [ ] DateTimeHelper.cs (new)
- [ ] CsvExportHelper.cs (refactor)
- [ ] ValidationHelper.cs (refactor)
- [ ] ConfigurationHelper.cs (new)

### Forms/Controls (UI/Pages → Forms/Controls/)
- [ ] Extract.razor → ExtractionControl.cs
- [ ] Export.razor → ExportControl.cs
- [ ] History.razor → AuditGridControl.cs
- [ ] Settings.razor → SettingsControl.cs
- [ ] Index.razor → DashboardControl.cs

### Forms/Shared (UI/Shared → Forms/Shared/)
- [ ] MainLayout.razor
- [ ] SimpleLayout.razor

### Resources
- [ ] config.example.yaml → Resources/Config/
- [ ] Icons → Resources/Icons/
- [ ] Images → Resources/Images/
"@ | Set-Content "MIGRATION_CHECKLIST.md"

Write-Host "✅ Checklist de migración creado"
```

---

## 🚀 FASE 2: Migración de Archivos (1-1.5 horas)

### 2.1 Migrar Models

**Paso 1: Copiar archivos**
```powershell
# Domain/Entities → Models/
Copy-Item "src/AuditHistoryExtractorPro.Domain/Entities/AuditRecord.cs" "Models/" -Force
Copy-Item "src/AuditHistoryExtractorPro.Domain/Entities/AuditFieldChange.cs" "Models/" -Force
Copy-Item "src/AuditHistoryExtractorPro.Domain/Entities/FieldDifference.cs" "Models/" -Force
Copy-Item "src/AuditHistoryExtractorPro.Domain/Entities/RecordComparison.cs" "Models/" -Force
Copy-Item "src/AuditHistoryExtractorPro.Domain/Entities/AuditStatistics.cs" "Models/" -Force

# Domain/ValueObjects → Models/
Copy-Item "src/AuditHistoryExtractorPro.Domain/ValueObjects/Configuration.cs" "Models/" -Force

Write-Host "✅ Models copiados"
```

**Paso 2: Actualizar namespaces en Models**

Para cada archivo `Models/*.cs`:
```csharp
// ANTES
namespace AuditHistoryExtractorPro.Domain.Entities;
namespace AuditHistoryExtractorPro.Domain.ValueObjects;

// DESPUÉS
namespace AuditHistoryExtractorPro.Models;
```

Script de actualización:
```powershell
Get-ChildItem "Models/*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    
    # Reemplazar namespaces
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Domain\.Entities;',`
        'namespace AuditHistoryExtractorPro.Models;'
    
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Domain\.ValueObjects;',`
        'namespace AuditHistoryExtractorPro.Models;'
    
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Domain;',`
        'namespace AuditHistoryExtractorPro.Models;'
    
    Set-Content $_.FullName $content
    Write-Host "✅ Actualizado: $($_.Name)"
}
```

### 2.2 Migrar Services

**Paso 1: Copiar archivos**
```powershell
# Infrastructure/Services → Services/
Copy-Item "src/AuditHistoryExtractorPro.Infrastructure/Services/*.cs" "Services/" -Exclude "Export" -Force
Copy-Item "src/AuditHistoryExtractorPro.Infrastructure/Repositories/DataverseAuditRepository.cs" "Services/" -Force
Copy-Item "src/AuditHistoryExtractorPro.Infrastructure/Authentication/AuthenticationProviders.cs" "Services/" -Force

# Subcarpeta Export
Copy-Item "src/AuditHistoryExtractorPro.Infrastructure/Services/Export/*.cs" "Services/" -Force

Write-Host "✅ Services copiados"
```

**Paso 2: Actualizar namespaces**
```powershell
Get-ChildItem "Services/*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    
    # Reemplazar namespaces
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Infrastructure\.Services;',`
        'namespace AuditHistoryExtractorPro.Services;'
    
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Infrastructure\.Repositories;',`
        'namespace AuditHistoryExtractorPro.Services;'
    
    $content = $content -replace `
        'namespace AuditHistoryExtractorPro\.Infrastructure\.Authentication;',`
        'namespace AuditHistoryExtractorPro.Services;'
    
    Set-Content $_.FullName $content
    Write-Host "✅ Actualizado: $($_.Name)"
}
```

### 2.3 Crear Helpers

**ResiliencePolicy.cs → Helpers/**
```powershell
Move-Item "Services/ResiliencePolicy.cs" "Helpers/" -Force
Write-Host "✅ ResiliencePolicy movido a Helpers/"
```

**Crear nuevos helpers:**
```csharp
// Helpers/DateTimeHelper.cs
namespace AuditHistoryExtractorPro.Helpers;

public static class DateTimeHelper
{
    public static string ToIso8601(DateTime dateTime)
    {
        return dateTime.ToUniversalTime().ToString("O");
    }
    
    public static string ToIso8601Date(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd");
    }
}
```

### 2.4 Migrar UI/Forms

**Paso 1: Copiar componentes Blazor**
```powershell
copy "src/AuditHistoryExtractorPro.UI/Pages/Extract.razor" "Forms/Controls/ExtractionControl.cs"
copy "src/AuditHistoryExtractorPro.UI/Pages/Export.razor" "Forms/Controls/ExportControl.cs"
copy "src/AuditHistoryExtractorPro.UI/Pages/History.razor" "Forms/Controls/AuditGridControl.cs"
copy "src/AuditHistoryExtractorPro.UI/Pages/Settings.razor" "Forms/Controls/SettingsControl.cs"
copy "src/AuditHistoryExtractorPro.UI/Pages/Index.razor" "Forms/Controls/DashboardControl.cs"

copy "src/AuditHistoryExtractorPro.UI/Shared/*.razor" "Forms/Shared/"

Write-Host "✅ Componentes UI copiados"
```

**Paso 2: Actualizar referencias**
```csharp
// En cada .razor que usa @using
@using AuditHistoryExtractorPro.Models;
@using AuditHistoryExtractorPro.Services;
@using AuditHistoryExtractorPro.Helpers;
```

### 2.5 Migrar Recursos

```powershell
# Estilos
copy "src/AuditHistoryExtractorPro.UI/wwwroot/css/*" "Resources/Styles/" -Recurse

# Scripts
copy "src/AuditHistoryExtractorPro.UI/wwwroot/js/*" "Resources/Scripts/" -Recurse

# Configuración
copy "config.example.yaml" "Resources/Config/"

Write-Host "✅ Recursos copiados"
```

---

## 🔄 FASE 3: Actualización de Referencias (1.5 horas)

### 3.1 Buscar y Reemplazar Globales

En Visual Studio (Ctrl+H):

| Buscar | Reemplazar | Tipo |
|--------|-----------|------|
| `using AuditHistoryExtractorPro.Domain.Entities;` | `using AuditHistoryExtractorPro.Models;` | Regex |
| `using AuditHistoryExtractorPro.Domain.ValueObjects;` | `using AuditHistoryExtractorPro.Models;` | Regex |
| `using AuditHistoryExtractorPro.Domain.Interfaces;` | `using AuditHistoryExtractorPro.Services;` | Regex |
| `using AuditHistoryExtractorPro.Infrastructure.Services;` | `using AuditHistoryExtractorPro.Services;` | Regex |
| `using AuditHistoryExtractorPro.Infrastructure.Repositories;` | `using AuditHistoryExtractorPro.Services;` | Regex |
| `using AuditHistoryExtractorPro.Infrastructure.Authentication;` | `using AuditHistoryExtractorPro.Services;` | Regex |
| `using AuditHistoryExtractorPro.UI;` | `using AuditHistoryExtractorPro.Forms;` | Regex |
| `using AuditHistoryExtractorPro.Infrastructure.Services.Export;` | `using AuditHistoryExtractorPro.Services;` | Regex |

### 3.2 Actualizar .csproj

**Archivo único (consolidado):**
```xml
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>false</UseWPF>
    <OutputType>Library</OutputType>
    <AssemblyName>AuditHistoryExtractorPro</AssemblyName>
    <RootNamespace>AuditHistoryExtractorPro</RootNamespace>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\shared\Whatever.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="*" />
    <PackageReference Include="Microsoft.Xrm.Sdk" Version="*" />
    <PackageReference Include="Polly" Version="*" />
    <PackageReference Include="ClosedXML" Version="*" />
    <PackageReference Include="CsvHelper" Version="*" />
    <PackageReference Include="Newtonsoft.Json" Version="*" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Resources\**\*" />
  </ItemGroup>

</Project>
```

### 3.3 Actualizar Solución

Eliminar proyectos antiguos del .sln:
```xml
<!-- ANTES -->
Project("{...}") = "AuditHistoryExtractorPro.Domain", ...
Project("{...}") = "AuditHistoryExtractorPro.Infrastructure", ...
Project("{...}") = "AuditHistoryExtractorPro.Application", ...
Project("{...}") = "AuditHistoryExtractorPro.UI", ...
Project("{...}") = "AuditHistoryExtractorPro.CLI", ...

<!-- DESPUÉS -->
Project("{...}") = "AuditHistoryExtractorPro", ...
```

---

## ✅ FASE 4: Validación (1 hora)

### 4.1 Compilación

```powershell
# Limpiar solución anterior
dotnet clean

# Restaurar
dotnet restore

# Compilar
dotnet build

# Si hay errores
dotnet build --verbose
```

### 4.2 Búsqueda de Problemas Comunes

```powershell
# Buscar namespaces antiguos no actualizados
Select-String -Path "**/*.cs" -Pattern "AuditHistoryExtractorPro\.Domain\." -Recurse

# Buscar referencias circulares
Select-String -Path "**/*.cs" -Pattern "namespace AuditHistoryExtractorPro\..*" -Recurse | Group-Object Path | Where-Object { $_.count -gt 1 }
```

### 4.3 Tests Unitarios

```powershell
# Ejecutar todos los tests
dotnet test

# Si fallan, revisar:
# 1. Namespaces imports
# 2. Rutas de archivos
# 3. Mocks/Fixtures de tests
```

### 4.4 Validación Manual

Checklist de sanidad:

- [ ] Solution compila sin errores
- [ ] Solution compila sin warnings
- [ ] Tests pasan (100% green)
- [ ] Intellisense funciona en todos los .cs
- [ ] No hay referencias rotas en Solution Explorer
- [ ] Se puede navegar entre archivos con F12
- [ ] Buscar referencias (Ctrl+K, Ctrl+R) funciona

---

## 🧹 FASE 5: Limpieza (30 minutos)

### 5.1 Eliminar Carpetas Antiguas

```powershell
# AFTER validación exitosa

# Eliminar proyectos antiguos
Remove-Item "src/AuditHistoryExtractorPro.Domain" -Recurse -Force
Remove-Item "src/AuditHistoryExtractorPro.Infrastructure" -Recurse -Force
Remove-Item "src/AuditHistoryExtractorPro.Application" -Recurse -Force
Remove-Item "src/AuditHistoryExtractorPro.UI" -Recurse -Force
Remove-Item "src/AuditHistoryExtractorPro.CLI" -Recurse -Force

# Eliminar carpeta src/ vacía
Remove-Item "src" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "✅ Carpetas antiguas eliminadas"
```

### 5.2 Actualizar Documentación

```markdown
# Actualizar README.md

## Estructura del Proyecto

\`\`\`
AuditHistoryExtractorPro/
├── App/                  ← Plugin entry point
├── Models/               ← Data structures
├── Services/             ← Business logic
├── Helpers/              ← Utilities
├── Forms/                ← UI (Blazor/WinForms)
├── Resources/            ← Assets
└── Tests/                ← Unit tests
\`\`\`
```

### 5.3 Commit y Push

```bash
git add .
git commit -m "refactor: reorganize to XrmToolBox standard structure

- Move Domain → Models (single namespace)
- Move Infrastructure → Services (consolidated)
- Create Helpers folder for utilities
- Consolidate 5 projects → 1 project
- Update all namespaces
- Remove 33 domain/infrastructure projects

Benefits:
- Cleaner file structure
- Easier navigation
- Reduced complexity
- Aligns with XrmToolBox patterns
- Single project compilation"

git push origin refactor/reorganize-xrmtoolbox

# Crear pull request para review
```

---

## 🔍 TROUBLESHOOTING

### Problema: "Namespace not found"
**Solución:**
```powershell
# Buscar namespace incorrecto
Select-String -Path "**/*.cs" -Pattern "namespace .*" -Recurse | Select-Object -Unique

# Reemplazar globalmente
Get-ChildItem -Recurse -Filter "*.cs" | ForEach-Object {
    (Get-Content $_.FullName) -replace `
        'namespace AuditHistoryExtractorPro.*\.([A-Za-z]*);',`
        'namespace AuditHistoryExtractorPro.$1;' | 
    Set-Content $_.FullName
}
```

### Problema: "Type not found" en Tests
**Solución:**
```csharp
// Actualizar referencias en Tests/
// ANTES
using AuditHistoryExtractorPro.Domain.Tests;

// DESPUÉS  
using AuditHistoryExtractorPro.Tests;
```

### Problema: "Multiple definitions" en Interfaces
**Solución:**
```powershell
# Buscar interfaces duplicadas
Get-ChildItem -Recurse -Filter "IService*.cs" | Group-Object Name | Where-Object { $_.count -gt 1 }

# Mantener la más reciente, eliminar antiguas
```

---

## 📊 Checklist Final

- [ ] Rama creada
- [ ] Estructura de carpetas creada
- [ ] Archivos copiados a nuevas ubicaciones
- [ ] Todos los namespaces actualizados
- [ ] .csproj consolidado y validado
- [ ] Solución compila sin errores
- [ ] Solución compila sin warnings
- [ ] Tests pasan 100%
- [ ] Intellisense funciona
- [ ] Documentación actualizada
- [ ] Carpetas antiguas eliminadas
- [ ] Commit realizado
- [ ] Pull Request creado
- [ ] Code review aprobado
- [ ] Merged a main
- [ ] Rama feature eliminada

---

## 📈 Post-Migración

### Verificación en CI/CD

Asegurar que pipeline completa:
- ✅ Build
- ✅ Tests
- ✅ Code Coverage
- ✅ Lint/Formatting

### Monitoreo

Después del merge:
1. Verificar que no hay regressions
2. Revisar reports de cobertura
3. Validar performance (no debe cambiar)

### Documentación

Actualizar:
- [ ] README.md
- [ ] CONTRIBUTING.md
- [ ] Architecture doc
- [ ] Team wiki

---

**Tiempo total estimado:** 5-6 horas  
**Complejidad:** Media (mecánica, sin lógica)  
**Riesgo:** Bajo (respaldables con git revert)

Próximo paso: ¿Autorizo la ejecución?

