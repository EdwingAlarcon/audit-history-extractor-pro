# 🚀 Inicio Rápido - Audit History Extractor Pro

## Primeros Pasos en 5 Minutos

### 1. Clonar y Preparar

```bash
# Clonar el repositorio
git clone https://github.com/your-org/audit-history-extractor-pro.git
cd audit-history-extractor-pro

# Restaurar dependencias
dotnet restore
```

### 2. Configurar Credenciales

```bash
# Copiar configuración de ejemplo
cp config.example.yaml config.yaml

# Editar configuración
# En Windows: notepad config.yaml
# En Linux/Mac: nano config.yaml
```

**Configurar mínimo:**
```yaml
dataverse:
  environment_url: "https://yourorg.crm.dynamics.com"
  auth_type: "oauth2"
  tenant_id: "tu-tenant-id"
  client_id: "tu-client-id"
```

### 3. Validar Conexión

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- validate
```

✅ Si ves "Connection validated successfully", estás listo.

### 4. Primera Extracción

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --format excel
```

### 5. Ver Resultado

El archivo se guardará en `./exports/` con formato:
```
audit_extract_20240217_153045.xlsx
```

---

## Comandos Rápidos

### Extraer Auditoría
```bash
# Básico
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract --entity account

# Con filtros
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account contact \
  --from 2024-01-01 \
  --operation update \
  --format csv
```

### Comparar Registros
```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- compare \
  --entity account \
  --recordid 12345678-1234-1234-1234-123456789012
```

### Modo Incremental
```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --incremental
```

---

## Iniciar Interfaz Web

```bash
# Ejecutar UI
dotnet run --project src/AuditHistoryExtractorPro.UI

# Abrir navegador en:
https://localhost:5001
```

---

## Docker (Opcional)

```bash
# Construir imagen
docker-compose build

# Iniciar servicios
docker-compose up -d

# Acceder a UI
http://localhost:5000
```

---

## Estructura del Proyecto

```
AuditHistoryExtractorPro/
├── src/
│   ├── Domain/           # Entidades y lógica de negocio
│   ├── Application/      # Casos de uso
│   ├── Infrastructure/   # Implementaciones técnicas
│   ├── CLI/             # Interfaz de línea de comandos
│   └── UI/              # Interfaz web (Blazor)
├── tests/               # Pruebas unitarias
├── docs/                # Documentación
├── examples/            # Ejemplos de uso
├── scripts/             # Scripts de build y deploy
└── config.example.yaml  # Configuración de ejemplo
```

---

## Formatos de Exportación

### Excel (.xlsx)
- ✅ Múltiples hojas con datos organizados
- ✅ Formato profesional
- ✅ Ideal para análisis manual

```bash
--format excel
```

### CSV (.csv)
- ✅ Compatible universalmente
- ✅ Ligero y rápido
- ✅ Fácil importación a bases de datos

```bash
--format csv
```

### JSON (.json)
- ✅ Estructura completa de datos
- ✅ Ideal para procesamiento programático
- ✅ Integración con APIs

```bash
--format json
```

---

## Configuración Avanzada

### Usar Azure Key Vault

```yaml
azure_key_vault:
  enabled: true
  vault_url: "https://your-vault.vault.azure.net/"

dataverse:
  client_secret: "kv://your-vault/dataverse-secret"
```

### Ajustar Rendimiento

```yaml
performance:
  max_parallel_requests: 10
  page_size: 5000
  enable_caching: true
```

### Configurar Exportación automática

```yaml
export:
  azure_blob_storage:
    enabled: true
    connection_string: "..."
    container_name: "audit-exports"
```

---

## Solución Rápida de Problemas

| Problema | Solución |
|----------|----------|
| "Authentication failed" | Verificar credenciales en `config.yaml` |
| "Connection error" | Verificar URL de Dataverse |
| "No records found" | Verificar que auditoría esté habilitada |
| "Throttling detected" | Reducir `max_parallel_requests` |

---

## Próximos Pasos

1. 📖 Leer [Guía de Usuario](./docs/user-guide.md) completa
2. 🏗️ Revisar [Arquitectura](./docs/architecture.md) del proyecto
3. 📊 Ver [Diagramas](./docs/diagrams.md) visuales
4. 💡 Explorar [Ejemplos](./examples/) prácticos
5. 🧪 Revisar [Tests](./tests/) para casos de uso

---

## Comandos Útiles para Desarrollo

```bash
# Compilar solución
dotnet build

# Ejecutar tests
dotnet test

# Publicar aplicación
.\scripts\build.ps1 -Configuration Release -Publish

# Desplegar a Azure
.\scripts\deploy-azure.ps1 -ResourceGroup "MyRG" -Location "eastus" -AppName "audit-extractor"
```

---

## Recursos

- 📚 **Documentación completa:** [/docs](/docs)
- 💻 **Ejemplos:** [/examples](/examples)
- 🐛 **Reportar problemas:** [GitHub Issues](https://github.com/your-org/audit-history-extractor-pro/issues)
- 💬 **Discusiones:** [GitHub Discussions](https://github.com/your-org/audit-history-extractor-pro/discussions)

---

## Soporte

- 📧 Email: support@auditextractorpro.com
- 📖 Wiki: [GitHub Wiki](https://github.com/your-org/audit-history-extractor-pro/wiki)
- 🎓 Tutoriales: [YouTube Channel](https://youtube.com/...)

---

**¡Listo para comenzar!** 🎉

Para más información, consulta la [documentación completa](./docs/user-guide.md).
