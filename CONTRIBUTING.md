# Contribuir a Audit History Extractor Pro

¡Gracias por tu interés en contribuir a Audit History Extractor Pro! Este documento proporciona directrices para contribuir al proyecto.

## 📋 Tabla de Contenidos

- [Código de Conducta](#código-de-conducta)
- [Cómo Contribuir](#cómo-contribuir)
- [Proceso de Desarrollo](#proceso-de-desarrollo)
- [Estándares de Código](#estándares-de-código)
- [Commits y Pull Requests](#commits-y-pull-requests)
- [Reportar Bugs](#reportar-bugs)
- [Solicitar Features](#solicitar-features)

## Código de Conducta

Este proyecto se adhiere a un código de conducta. Al participar, se espera que mantengas este código.

### Nuestros Estándares

- Ser respetuoso con diferentes puntos de vista
- Aceptar críticas constructivas
- Enfocarse en lo mejor para la comunidad
- Mostrar empatía hacia otros miembros

## Cómo Contribuir

### 1. Fork el Repositorio

```bash
# Fork en GitHub, luego clonar tu fork
git clone https://github.com/tu-usuario/audit-history-extractor-pro.git
cd audit-history-extractor-pro
```

### 2. Crear una Rama

```bash
# Crear rama desde main
git checkout -b feature/mi-nueva-caracteristica
# o
git checkout -b fix/mi-correccion
```

**Convención de nombres de ramas:**
- `feature/` - Nuevas características
- `fix/` - Corrección de bugs
- `docs/` - Cambios en documentación
- `refactor/` - Refactorización de código
- `test/` - Agregar o modificar tests

### 3. Hacer tus Cambios

```bash
# Hacer cambios en el código
# Ejecutar tests
dotnet test

# Verificar que compile
dotnet build
```

### 4. Commit y Push

```bash
# Agregar cambios
git add .

# Commit con mensaje descriptivo
git commit -m "feat: agregar soporte para exportación SQL"

# Push a tu fork
git push origin feature/mi-nueva-caracteristica
```

### 5. Crear Pull Request

- Ve a GitHub y crea un Pull Request desde tu rama
- Describe claramente los cambios realizados
- Referencia issues relacionados si aplica

## Proceso de Desarrollo

### Configurar Entorno de Desarrollo

```bash
# Instalar .NET 8 SDK
# https://dotnet.microsoft.com/download

# Restaurar dependencias
dotnet restore

# Ejecutar tests
dotnet test

# Ejecutar aplicación localmente
dotnet run --project src/AuditHistoryExtractorPro.CLI
dotnet run --project src/AuditHistoryExtractorPro.UI
```

### Estructura del Proyecto

```
src/
├── AuditHistoryExtractorPro.Domain/      # Capa de dominio (entidades, interfaces)
├── AuditHistoryExtractorPro.Application/  # Capa de aplicación (casos de uso)
├── AuditHistoryExtractorPro.Infrastructure/ # Capa de infraestructura
├── AuditHistoryExtractorPro.CLI/         # CLI
└── AuditHistoryExtractorPro.UI/          # UI Web

tests/
├── AuditHistoryExtractorPro.Domain.Tests/
├── AuditHistoryExtractorPro.Application.Tests/
└── AuditHistoryExtractorPro.Infrastructure.Tests/
```

## Estándares de Código

### Guías de Estilo C#

- Seguir [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Usar PascalCase para clases, métodos y propiedades
- Usar camelCase para variables locales y parámetros
- Incluir XML documentation comments para APIs públicas

```csharp
/// <summary>
/// Extrae registros de auditoría según criterios específicos
/// </summary>
/// <param name="criteria">Criterios de extracción</param>
/// <param name="cancellationToken">Token de cancelación</param>
/// <returns>Lista de registros de auditoría extraídos</returns>
public async Task<List<AuditRecord>> ExtractAuditRecordsAsync(
    ExtractionCriteria criteria,
    CancellationToken cancellationToken = default)
{
    // Implementación
}
```

### Principios SOLID

Este proyecto sigue principios SOLID:
- **S**ingle Responsibility
- **O**pen/Closed
- **L**iskov Substitution
- **I**nterface Segregation
- **D**ependency Inversion

### Clean Architecture

Respetar las capas de Clean Architecture:
- Domain no debe tener dependencias externas
- Application solo depende de Domain
- Infrastructure implementa interfaces de Domain
- Presentation depende de Application

### Naming Conventions

```csharp
// Interfaces
public interface IAuditRepository { }

// Clases
public class DataverseAuditRepository : IAuditRepository { }

// Métodos async
public async Task<Result> ExecuteAsync() { }

// Constantes
public const int MaxPageSize = 10000;

// Variables privadas
private readonly ILogger _logger;
```

## Commits y Pull Requests

### Mensajes de Commit

Seguir [Conventional Commits](https://www.conventionalcommits.org/):

```
<tipo>[opcional alcance]: <descripción>

[cuerpo opcional]

[nota de pie opcional]
```

**Tipos:**
- `feat`: Nueva característica
- `fix`: Corrección de bug
- `docs`: Cambios en documentación
- `style`: Cambios de formato (no afectan código)
- `refactor`: Refactorización
- `test`: Agregar o modificar tests
- `chore`: Cambios en build, herramientas, etc.

**Ejemplos:**
```bash
feat: agregar soporte para autenticación por certificado
fix: corregir error de paginación en extracción grande
docs: actualizar guía de usuario con ejemplos de filtros
refactor: simplificar lógica de procesamiento de auditoría
test: agregar tests para ExtractionCriteria validation
```

### Pull Request Guidelines

**Título del PR:**
```
[Tipo] Descripción breve
```

**Descripción del PR debe incluir:**
- ✅ Qué cambia este PR
- ✅ Por qué es necesario este cambio
- ✅ Cómo se probó
- ✅ Screenshots (si aplica para UI)
- ✅ Referencias a issues relacionados

**Template de PR:**
```markdown
## Descripción
Breve descripción de los cambios

## Motivación y Contexto
Por qué es necesario este cambio

## Cómo se ha probado
- [ ] Tests unitarios
- [ ] Tests de integración
- [ ] Tests manuales

## Tipos de cambios
- [ ] Bug fix (cambio que corrige un issue)
- [ ] Nueva característica (cambio que agrega funcionalidad)
- [ ] Breaking change (cambio que rompe compatibilidad)
- [ ] Cambio en documentación

## Checklist
- [ ] Mi código sigue el estilo del proyecto
- [ ] He agregado tests que prueban mi cambio
- [ ] Todos los tests pasan
- [ ] He actualizado la documentación
- [ ] Mi cambio no genera nuevas advertencias
```

## Tests

### Ejecutar Tests

```bash
# Todos los tests
dotnet test

# Tests específicos
dotnet test --filter "Category=Unit"
dotnet test --filter "FullyQualifiedName~AuditRecordTests"

# Con cobertura
dotnet test /p:CollectCoverage=true
```

### Escribir Tests

Usar **AAA Pattern** (Arrange, Act, Assert):

```csharp
[Fact]
public void Validate_ShouldThrowException_WhenNoEntitiesSpecified()
{
    // Arrange
    var criteria = new ExtractionCriteria
    {
        EntityNames = new List<string>()
    };

    // Act
    Action act = () => criteria.Validate();

    // Assert
    act.Should().Throw<ArgumentException>()
        .WithMessage("At least one entity name must be specified");
}
```

**Cobertura de código esperada:** >= 80%

## Reportar Bugs

### Antes de Reportar

- Verificar que el bug no esté ya reportado
- Confirmar que es reproducible en la última versión
- Recopilar información relevante

### Template de Bug Report

```markdown
**Descripción del Bug**
Descripción clara del problema

**Pasos para Reproducir**
1. Ejecutar comando '...'
2. Con parámetros '...'
3. Ver error

**Comportamiento Esperado**
Qué debería suceder

**Comportamiento Actual**
Qué sucede actualmente

**Screenshots**
Si aplica

**Entorno**
- OS: [e.g., Windows 11]
- .NET Version: [e.g., 8.0.1]
- Versión de la App: [e.g., 1.0.0]

**Información Adicional**
Logs, stack traces, etc.
```

## Solicitar Features

### Template de Feature Request

```markdown
**¿Tu feature request está relacionado con un problema?**
Descripción clara del problema

**Describe la solución que te gustaría**
Descripción clara de lo que quieres que suceda

**Describe alternativas consideradas**
Otras soluciones o características consideradas

**Contexto Adicional**
Cualquier otro contexto, screenshots, etc.
```

## Review Process

1. **Automated Checks**
   - Build debe pasar
   - Tests deben pasar
   - Linting debe pasar

2. **Code Review**
   - Al menos un mantenedor debe aprobar
   - Resolver todos los comentarios
   - Actualizar según feedback

3. **Merge**
   - Squash commits si es apropiado
   - Merge a main
   - Tag de versión si aplica

## Áreas que Necesitan Contribución

### 🌟 Features Prioritarios
- [ ] Exportación a SQL Server
- [ ] Exportación a PostgreSQL
- [ ] Envío automático por email
- [ ] Gráficos en dashboard UI
- [ ] Soporte para SharePoint

### 🐛 Bugs Conocidos
- Ver [Issues](https://github.com/your-org/audit-history-extractor-pro/issues?q=is%3Aissue+is%3Aopen+label%3Abug)

### 📚 Documentación
- [ ] Más ejemplos de uso
- [ ] Tutoriales en video
- [ ] Traducción a otros idiomas
- [ ] API documentation

### 🧪 Tests
- [ ] Aumentar cobertura de tests
- [ ] Tests de integración
- [ ] Tests de performance

## Contacto

- 💬 Discord: [Servidor de Discord](#)
- 📧 Email: contributors@auditextractorpro.com
- 🐦 Twitter: [@AuditExtractorPro](#)

## Reconocimientos

Todos los contribuidores serán reconocidos en:
- README.md
- Release notes
- Contributors page

¡Gracias por contribuir! 🎉
