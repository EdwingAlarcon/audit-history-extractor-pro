# Audit History Extractor Pro

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

## 🚀 Descripción

**Audit History Extractor Pro** es una aplicación empresarial avanzada para extraer, procesar y exportar el historial de auditoría de entidades de Microsoft Dataverse con funcionalidades profesionales, arquitectura limpia y experiencia de usuario moderna.

## ✨ Características Principales

### Autenticación Avanzada
- 🔐 OAuth2 (Azure AD)
- 🔑 Client Secret
- 📜 Certificate Authentication
- 🎯 Managed Identity
- ✅ Detección automática de método recomendado

### Extracción Inteligente
- 📊 Selección flexible de entidades, campos y rangos de fechas
- 🔍 Filtros avanzados por usuario, operación y cambios específicos
- 📄 Paginación inteligente con manejo de throttling
- 🔄 Extracción incremental (solo cambios nuevos)
- ⚡ Paralelización de consultas para máximo rendimiento

### Procesamiento Avanzado
- 🔄 Normalización automática de registros
- 📊 Comparación entre versiones de registros
- 🎯 Identificación de cambios relevantes
- 🧹 Limpieza y enriquecimiento de datos

### Exportación Flexible
- 📁 Formatos: Excel, CSV, JSON, SQL
- 📦 Exportación masiva por lotes
- 🗜️ Compresión automática
- 📧 Envío por correo o Azure Blob Storage

### Interfaz Gráfica Moderna
- 📊 Dashboard con estadísticas en tiempo real
- 👁️ Vista previa de cambios por registro
- ⏳ Barra de progreso en tiempo real
- 📝 Logs detallados y descargables

### CLI Profesional
- ⚡ Comandos intuitivos y potentes
- 📝 Soporte para archivos de configuración YAML/JSON
- 🔄 Scripting y automatización

## 🏗️ Arquitectura

El proyecto sigue **Clean Architecture** con las siguientes capas:

```
├── Domain Layer (Core Business Logic)
│   ├── Entities
│   ├── Value Objects
│   └── Repository Interfaces
│
├── Application Layer (Use Cases)
│   ├── Services
│   ├── DTOs
│   └── Interfaces
│
├── Infrastructure Layer (External Concerns)
│   ├── Dataverse Client
│   ├── Authentication Providers
│   ├── Exporters
│   ├── Caching
│   └── Azure Key Vault Integration
│
└── Presentation Layer
    ├── CLI (Command Line Interface)
    └── UI (Blazor Web App)
```

## 🚀 Inicio Rápido

### Prerrequisitos
- .NET 8.0 SDK
- Visual Studio 2022 o VS Code
- Acceso a un entorno de Microsoft Dataverse

### Instalación

```bash
# Clonar el repositorio
git clone https://github.com/your-org/audit-history-extractor-pro.git
cd audit-history-extractor-pro

# Restaurar dependencias
dotnet restore

# Compilar la solución
dotnet build

# Ejecutar tests
dotnet test
```

### Configuración

1. Copiar el archivo de configuración de ejemplo:
```bash
cp config.example.yaml config.yaml
```

2. Editar `config.yaml` con tus credenciales de Dataverse:
```yaml
dataverse:
  environment_url: "https://yourorg.crm.dynamics.com"
  auth_type: "oauth2"  # oauth2, client_secret, certificate, managed_identity
  
  # Para OAuth2
  tenant_id: "your-tenant-id"
  client_id: "your-client-id"
  
  # Para Client Secret (usar Azure Key Vault en producción)
  client_secret: "your-secret-or-keyvault-reference"
```

## 📖 Uso

### Interfaz de Línea de Comandos (CLI)

```bash
# Extraer historial de auditoría
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-12-31 \
  --format excel

# Extracción incremental
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity contact \
  --incremental

# Comparar versiones de un registro
dotnet run --project src/AuditHistoryExtractorPro.CLI -- compare \
  --entity account \
  --recordid 12345678-1234-1234-1234-123456789012

# Exportar con filtros avanzados
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity opportunity \
  --from 2024-01-01 \
  --user "john.doe@company.com" \
  --operation update \
  --format json \
  --output ./exports/
```

### Interfaz Gráfica (UI)

```bash
# Ejecutar la interfaz web
dotnet run --project src/AuditHistoryExtractorPro.UI
```

**Abre tu navegador en:** `https://localhost:5001`

#### 🎯 Primera Vez - Configurar Credenciales

1. **Accede a la interfaz**: https://localhost:5001
2. **Ve a Configuración**: Click en el menú lateral → ⚙️ Configuración
3. **Completa tus credenciales**:
   - URL del Entorno: `https://tuorg.crm.dynamics.com`
   - Tipo de Autenticación: OAuth2 (recomendado)
   - Tenant ID y Client ID de Azure
4. **Guarda la configuración** con un nombre descriptivo
5. **Prueba la conexión** para verificar

#### 📦 Múltiples Cuentas

¿Trabajas con varios entornos de Dataverse? 

✅ **Puedes guardar múltiples configuraciones**:
- Production
- Testing  
- Development
- Cliente A, Cliente B, etc.

Solo ve a **Configuración** → pestaña **"Múltiples Cuentas"** → Guarda cada una con un nombre único.

> 📚 **Guía Completa**: [GUIA-RAPIDA-UI.md](./GUIA-RAPIDA-UI.md)

## 🔧 Configuración Avanzada

### Azure Key Vault Integration

```yaml
azure_key_vault:
  enabled: true
  vault_url: "https://your-vault.vault.azure.net/"
  
secrets:
  client_secret: "kv://your-vault/dataverse-client-secret"
  certificate: "kv://your-vault/dataverse-certificate"
```

### Configuración de Rendimiento

```yaml
performance:
  max_parallel_requests: 10
  page_size: 5000
  enable_caching: true
  cache_duration_minutes: 30
  retry_attempts: 3
  throttle_retry_delay_ms: 1000
```

### Filtros de Auditoría

```yaml
audit_filters:
  entities:
    - account
    - contact
    - opportunity
  
  operations:
    - create
    - update
    - delete
  
  date_range:
    from: "2024-01-01"
    to: "2024-12-31"
  
  users:
    - "john.doe@company.com"
    - "jane.smith@company.com"
```

## 📊 Ejemplos de Uso

Ver la carpeta [examples/](./examples/) para casos de uso completos:

- [Extracción básica](./examples/01-basic-extraction.md)
- [Filtros avanzados](./examples/02-advanced-filters.md)
- [Extracción incremental](./examples/03-incremental-extraction.md)
- [Comparación de registros](./examples/04-record-comparison.md)
- [Automatización con scripts](./examples/05-automation.md)

## 🧪 Testing

```bash
# Ejecutar todos los tests
dotnet test

# Tests con cobertura
dotnet test /p:CollectCoverage=true

# Tests de integración
dotnet test --filter Category=Integration
```

## 📦 Despliegue

### Docker

```bash
# Construir imagen
docker build -t audit-extractor-pro .

# Ejecutar contenedor
docker run -p 5000:5000 -v $(pwd)/config.yaml:/app/config.yaml audit-extractor-pro
```

### Azure App Service

```bash
# Desplegar a Azure
az webapp up --name audit-extractor-pro --runtime "DOTNET:8.0"
```

## 🔒 Seguridad

- ✅ Credenciales nunca almacenadas en texto plano
- ✅ Integración con Azure Key Vault
- ✅ Validación estricta de parámetros
- ✅ Logging sin información sensible
- ✅ HTTPS obligatorio en producción

## 📚 Documentación

- [Guía de Arquitectura](./docs/architecture.md)
- [Guía de Desarrollo](./docs/development-guide.md)
- [Guía de Usuario](./docs/user-guide.md)
- [API Reference](./docs/api-reference.md)
- [Troubleshooting](./docs/troubleshooting.md)

## 🤝 Contribuir

Las contribuciones son bienvenidas. Por favor, lee [CONTRIBUTING.md](./CONTRIBUTING.md) para detalles.

## 📄 Licencia

Este proyecto está licenciado bajo la Licencia MIT - ver [LICENSE](./LICENSE) para detalles.

## 👥 Autores

- **Tu Nombre** - *Trabajo inicial* - [GitHub](https://github.com/yourusername)

## 🙏 Agradecimientos

- Inspirado en [Audit-History-Extractor](https://github.com/alduzzen1985/Audit-History-Extractor)
- Microsoft Dataverse SDK
- Comunidad .NET

## 📞 Soporte

- 📧 Email: support@auditextractorpro.com
- 🐛 Issues: [GitHub Issues](https://github.com/your-org/audit-history-extractor-pro/issues)
- 💬 Discusiones: [GitHub Discussions](https://github.com/your-org/audit-history-extractor-pro/discussions)

---

Hecho con ❤️ para la comunidad de Dataverse
