# Guía de Usuario - Audit History Extractor Pro

## 📋 Tabla de Contenidos

1. [Introducción](#introducción)
2. [Instalación](#instalación)
3. [Configuración Inicial](#configuración-inicial)
4. [Uso de CLI](#uso-de-cli)
5. [Uso de UI Web](#uso-de-ui-web)
6. [Casos de Uso Comunes](#casos-de-uso-comunes)
7. [Solución de Problemas](#solución-de-problemas)

## Introducción

Audit History Extractor Pro es una herramienta profesional para extraer, analizar y exportar el historial de auditoría de Microsoft Dataverse. Ofrece capacidades avanzadas de filtrado, comparación deregistros y múltiples formatos de exportación.

### Características Principales

- ✅ Extracción de auditoría con filtros avanzados
- ✅ Exportación a Excel, CSV, JSON y SQL
- ✅ Comparación de versiones de registros
- ✅ Modo incremental (solo cambios nuevos)
- ✅ Interfaz CLI y Web
- ✅ Procesamiento paralelo para gran rendimiento
- ✅ Integración con Azure Key Vault

## Instalación

### Requisitos Previos

- .NET 8.0 SDK o Runtime
- Acceso a un entorno de Microsoft Dataverse
- Credenciales de autenticación configuradas

### Opción 1: Instalación desde Código Fuente

```bash
# Clonar repositorio
git clone https://github.com/your-org/audit-history-extractor-pro.git
cd audit-history-extractor-pro

# Restaurar dependencias
dotnet restore

# Compilar
dotnet build --configuration Release

# (Opcional) Publicar versión independiente
dotnet publish -c Release -r win-x64 --self-contained
```

### Opción 2: Instalación desde Release

```bash
# Descargar el release desde GitHub
# Extraer archivos
# Ejecutar el instalador o copiar archivos
```

### Verificar Instalación

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- --version
```

## Configuración Inicial

### 1. Crear Archivo de Configuración

```bash
# Copiar archivo de ejemplo
cp config.example.yaml config.yaml
```

### 2. Configurar Conexión a Dataverse

Editar `config.yaml`:

```yaml
dataverse:
  environment_url: "https://yourorg.crm.dynamics.com"
  auth_type: "oauth2"  # o client_secret, certificate, managed_identity
  tenant_id: "your-tenant-id"
  client_id: "your-client-id"
```

### 3. Configurar Autenticación

#### Opción A: OAuth2 (Interactivo)
```yaml
dataverse:
  auth_type: "oauth2"
  tenant_id: "12345678-1234-1234-1234-123456789012"
  client_id: "87654321-4321-4321-4321-210987654321"
```

#### Opción B: Client Secret
```yaml
dataverse:
  auth_type: "client_secret"
  tenant_id: "your-tenant-id"
  client_id: "your-client-id"
  client_secret: "your-client-secret"  # O usar Key Vault
```

#### Opción C: Certificate
```yaml
dataverse:
  auth_type: "certificate"
  client_id: "your-client-id"
  certificate_thumbprint: "ABC123..."
  # O
  certificate_path: "/path/to/certificate.pfx"
```

#### Opción D: Managed Identity (Azure)
```yaml
dataverse:
  auth_type: "managed_identity"
  use_managed_identity: true
```

### 4. (Opcional) Configurar Azure Key Vault

```yaml
azure_key_vault:
  enabled: true
  vault_url: "https://your-vault.vault.azure.net/"

dataverse:
  client_secret: "kv://your-vault/dataverse-client-secret"
```

### 5. Validar Configuración

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- validate
```

### 6. Configuración local para UI Web

Crear archivo local desde plantilla:

```bash
# Linux/macOS
cp src/AuditHistoryExtractorPro.UI/appsettings.example.json src/AuditHistoryExtractorPro.UI/appsettings.Development.json

# Windows PowerShell
Copy-Item src\AuditHistoryExtractorPro.UI\appsettings.example.json src\AuditHistoryExtractorPro.UI\appsettings.Development.json
```

Editar `src/AuditHistoryExtractorPro.UI/appsettings.Development.json` y completar al menos:
- `Dataverse.EnvironmentUrl`
- `Dataverse.Type`
- Credenciales requeridas por el método elegido

## Uso de CLI

### Comandos Básicos

#### 1. Extraer Auditoría de una Entidad

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-12-31 \
  --format excel
```

**Parámetros:**
- `--entity`: Nombre(s) de entidad (puede especificar múltiples)
- `--from`: Fecha inicial (yyyy-MM-dd)
- `--to`: Fecha final (yyyy-MM-dd)
- `--format`: Formato de salida (excel, csv, json, sql)
- `--output`: Directorio de salida (default: ./exports)

#### 2. Extraer Múltiples Entidades

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact opportunity \
  --from 2024-01-01 \
  --format csv
```

#### 3. Extracción con Filtros Avanzados

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --user "john.doe@company.com" "jane.smith@company.com" \
  --operation update delete \
  --format excel
```

**Filtros disponibles:**
- `--user`: Filtrar por usuarios específicos
- `--operation`: Filtrar por tipo de operación (create, update, delete)

#### 4. Extracción Incremental

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --incremental \
  --format excel
```

El modo incremental extrae solo registros nuevos desde la última extracción.

#### 5. Comparar Versiones de un Registro

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- compare \
  --entity account \
  --recordid 12345678-1234-1234-1234-123456789012 \
  --from 2024-01-01 \
  --to 2024-12-31
```

#### 6. Exportar desde JSON Existente

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- export \
  --input ./exports/audit_data.json \
  --format csv \
  --output ./exports/csv/
```

### Ejemplos Avanzados

#### Ejemplo 1: Auditoría de Oportunidades Modificadas

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity opportunity \
  --from 2024-01-01 \
  --operation update \
  --format excel \
  --output ./reports/opportunities/
```

#### Ejemplo 2: Auditoría de Eliminaciones por Usuario

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact \
  --from 2024-01-01 \
  --operation delete \
  --user "admin@company.com" \
  --format json
```

#### Ejemplo 3: Extracción Completa con Compresión

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact opportunity lead \
  --from 2023-01-01 \
  --to 2024-12-31 \
  --format csv
# La compresión automática se activa para archivos >10MB
```

## Uso de UI Web

### 1. Iniciar la Interfaz Web

```bash
dotnet run --project src/AuditHistoryExtractorPro.UI
```

Navegar a: `https://localhost:5001`

### 2. Dashboard Principal

El dashboard muestra:
- Estadísticas de extracciones realizadas
- Total de registros extraídos
- Entidades monitoreadas
- Última extracción
- Historial de extracciones recientes

### 3. Nueva Extracción

1. Click en "New Extraction"
2. Seleccionar entidades
3. Configurar rango de fechas
4. Aplicar filtros (opcional)
5. Seleccionar formato de exportación
6. Click en "Extract"
7. Monitorear progreso en tiempo real
8. Descargar archivo generado

### 4. Comparar Registros

1. Click en "Compare Records"
2. Introducir Entity Name
3. Introducir Record ID
4. Seleccionar rango de fechas (opcional)
5. Click en "Compare"
6. Ver diferencias campo por campo

### 5. Ver Exportaciones

1. Click en "View Exports"
2. Ver historial de archivos generados
3. Descargar o eliminar archivos
4. Ver metadata de exportaciones

### 6. Configuración

1. Click en "Settings"
2. Configurar credenciales de Dataverse
3. Ajustar configuraciones de rendimiento
4. Configurar destinos de exportación
5. Guardar cambios

## Casos de Uso Comunes

### Caso 1: Auditoría de Compliance Mensual

**Objetivo:** Extraer todos los cambios del mes para reporte de compliance

```bash
# Primer día del mes siguiente
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact opportunity \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --format excel \
  --output ./compliance/2024-01/
```

**Automatizar:** Crear scheduled task/cron job

### Caso 2: Investigación de Eliminaciones

**Objetivo:** Investigar quién eliminó registros específicos

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --operation delete \
  --format excel
```

**Análisis:** Abrir Excel, filtrar por fecha/usuario

### Caso 3: Seguimiento de Cambios de Campo Específico

**Objetivo:** Ver todos los cambios en el campo "estadocode"

1. Extraer auditoría completa a JSON
2. Procesar JSON con script personalizado
3. O usar comparación de registros en UI

### Caso 4: Backup Incremental Diario

**Objetivo:** Backup diario de cambios

```bash
# Script diario (e.g., cron job)
#!/bin/bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact opportunity lead \
  --incremental \
  --format json \
  --output /backups/audit/
```

### Caso 5: Análisis de Usuario Específico

**Objetivo:** Ver todos los cambios realizados por un usuario

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact \
  --user "john.doe@company.com" \
  --from 2024-01-01 \
  --format excel
```

## Formatos de Exportación

### Excel (.xlsx)

**Características:**
- Múltiples hojas (Summary, Audit Records, Field Changes)
- Formato profesional con colores
- Filtros automáticos
- Ideal para análisis manual

**Uso:** Reportes ejecutivos, análisis exploratoria

### CSV (.csv)

**Características:**
- Formato simple separado por comas
- Compatible con cualquier herramienta
- Fácil de importar a bases de datos

**Uso:** Importación a otros sistemas, procesamiento automatizado

### JSON (.json)

**Características:**
- Formato estructurado
- Conserva tipos de datos
- Fácil de parsear programáticamente

**Uso:** Integración con APIs, procesamiento con scripts

### SQL (.sql)

**Características:**
- Scripts INSERT para bases de datos
- Incluye CREATE TABLE
- Listo para ejecutar

**Uso:** Carga en bases de datos SQL, data warehousing

## Solución de Problemas

### Error: "Failed to connect to Dataverse"

**Causas:**
- URL de entorno incorrecta
- Credenciales inválidas
- Firewall bloqueando conexión

**Solución:**
```bash
# 1. Verificar URL
# 2. Validar credenciales
dotnet run --project src/AuditHistoryExtractorPro.CLI -- validate

# 3. Test de conectividad
ping yourorg.crm.dynamics.com
```

### Error: "Authentication failed"

**OAuth2:**
- Verificar tenant_id y client_id
- Asegurar que la app tiene permisos en Azure AD

**Client Secret:**
- Verificar que el secret no haya expirado
- Comprobar que el secret es correcto

**Certificate:**
- Verificar que el certificado existe
- Comprobar permisos de lectura del certificado

### Error: "Throttling detected"

**Causa:** Límites de API de Dataverse alcanzados

**Solución:**
```yaml
# Ajustar en config.yaml
performance:
  max_parallel_requests: 5  # Reducir paralelismo
  page_size: 1000  # Reducir tamaño de página
  retry_attempts: 5  # Aumentar reintentos
  throttle_retry_delay_ms: 2000  # Aumentar espera
```

### Archivo muy grande / Sin memoria

**Solución:**
```bash
# Dividir por fecha
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --format csv  # CSV usa menos memoria que Excel

# Procesar por lotes
```

### Performance lenta

**Optimizaciones:**

1. **Filtrar entidades:** Solo extraer lo necesario
2. **Reducir rango de fechas:** Procesar por períodos
3. **Usar paginación apropiada:**
```yaml
performance:
  page_size: 5000  # Valor óptimo
```
4. **Habilitar caché:**
```yaml
performance:
  enable_caching: true
```

## Mejores Prácticas

### 1. Planificación de Extracciones

- ✅ Extraer fuera de horario laboral
- ✅ Usar modo incremental para extracciones frecuentes
- ✅ Establecer ventanas de fecha razonables

### 2. Gestión de Datos

- ✅ Organizar exports en carpetas por fecha
- ✅ Nombrar archivos descriptivamente
- ✅ Hacer backup de exports importantes
- ✅ Eliminar exports antiguos regularmente

### 3. Seguridad

- ✅ Usar Azure Key Vault para secretos
- ✅ No compartir archivos de configuración
- ✅ Limitar acceso a exports (contienen datos sensibles)
- ✅ Usar Managed Identity en Azure

### 4. Rendimiento

- ✅ Ajustar `max_parallel_requests` según capacidad
- ✅ Usar formato CSV para grandes volúmenes
- ✅ Habilitar compresión automática
- ✅ Monitorear logs para errores

## Preguntas Frecuentes (FAQ)

**Q: ¿Puedo extraer auditoría de entidades personalizadas?**  
A: Sí, especificar el schema name (e.g., `new_customentity`)

**Q: ¿Cuántos registros puedo extraer a la vez?**  
A: Sin límite, pero es recomendable dividir grandes volúmenes

**Q: ¿Los datos extraídos están en tiempo real?**  
A: Sí, se obtienen directamente de Dataverse

**Q: ¿Puedo programar extracciones automáticas?**  
A: Sí, usar Task Scheduler (Windows) o cron (Linux)

**Q: ¿Soporta múltiples entornos de Dataverse?**  
A: Sí, crear múltiples archivos de configuración

**Q: ¿Cómo veo cambios campo por campo?**  
A: Usar el comando `compare` o la hoja "Field Changes" en Excel

## Soporte y Recursos

- 📖 Documentación: [GitHub Wiki](https://github.com/your-org/audit-history-extractor-pro/wiki)
- 🐛 Reportar Issues: [GitHub Issues](https://github.com/your-org/audit-history-extractor-pro/issues)
- 💬 Discusiones: [GitHub Discussions](https://github.com/your-org/audit-history-extractor-pro/discussions)
- 📧 Email: support@auditextractorpro.com

---

**Versión de Documento:** 1.0.0  
**Última Actualización:** 17 de febrero de 2026
