# 🔧 Configuración de Dataverse

## Requisitos Previos

Antes de conectar la aplicación a Dataverse, necesitas:

1. **Entorno de Dataverse/Dynamics 365** con acceso habilitado
2. **Registro de aplicación en Azure AD** (para autenticación)
3. **Permisos de auditoría** en Dataverse

---

## 📝 Pasos para Configurar

### 1. Registrar Aplicación en Azure AD

1. Ve a [Azure Portal](https://portal.azure.com)
2. Navega a **Azure Active Directory** > **App registrations** > **New registration**
3. Configura:
   - **Name**: "Audit History Extractor Pro"
   - **Supported account types**: Single tenant
   - **Redirect URI**: `http://localhost` (Public client/native)
4. Guarda el **Application (client) ID** y **Directory (tenant) ID**

### 2. Configurar Permisos API

1. En tu aplicación registrada, ve a **API permissions**
2. Agrega los siguientes permisos:
   - **Dynamics CRM** > **Delegated permissions** > **user_impersonation**
3. Haz clic en **Grant admin consent**

### 3. Habilitar Autenticación Pública

1. Ve a **Authentication**
2. En **Advanced settings** > **Allow public client flows**: **Yes**
3. Guarda los cambios

### 4. Configurar appsettings.json

Edita `src/AuditHistoryExtractorPro.UI/appsettings.json`:

```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://tu-organizacion.crm.dynamics.com",
    "TenantId": "tu-tenant-id-aqui",
    "ClientId": "tu-client-id-aqui",
    "AuthType": "OAuth2"
  }
}
```

**Reemplaza:**
- `tu-organizacion` con el nombre de tu organización Dataverse
- `tu-tenant-id-aqui` con el **Directory (tenant) ID** de Azure AD
- `tu-client-id-aqui` con el **Application (client) ID** de Azure AD

---

## 🔐 Tipos de Autenticación Soportados

### OAuth2 (Recomendado para desarrollo)

```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://org.crm.dynamics.com",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "AuthType": "OAuth2"
  }
}
```

### Client Secret (Aplicaciones servidor-a-servidor)

1. En Azure AD, ve a **Certificates & secrets** > **New client secret**
2. Copia el secreto generado
3. Configura:

```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://org.crm.dynamics.com",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": "tu-secreto-aqui",
    "AuthType": "ClientSecret"
  }
}
```

⚠️ **Importante**: Usa Azure Key Vault en producción para guardar secretos.

### Certificate

```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://org.crm.dynamics.com",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "CertificatePath": "C:\\certs\\certificate.pfx",
    "CertificatePassword": "password",
    "AuthType": "Certificate"
  }
}
```

### Managed Identity (Azure App Service/Functions)

```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://org.crm.dynamics.com",
    "AuthType": "ManagedIdentity"
  }
}
```

---

## 🚀 Ejecutar la Aplicación

```bash
cd src/AuditHistoryExtractorPro.UI
dotnet restore
dotnet build
dotnet run
```

La aplicación estará disponible en: **http://localhost:5000**

---

## ✅ Verificar Conexión

1. Abre la aplicación en tu navegador
2. Ve a **⚙️ Configuración**
3. Haz clic en **🔌 Probar Conexión**
4. Deberías ver un mensaje: "✓ Conexión exitosa con Dataverse"

---

## 🔍 Solución de Problemas

### Error: "Failed to authenticate with OAuth2"

- Verifica que el **TenantId** y **ClientId** sean correctos
- Asegúrate de que la aplicación tenga permisos **user_impersonation**
- Verifica que "Allow public client flows" esté habilitado

### Error: "Failed to connect to Dataverse"

- Verifica que la **EnvironmentUrl** sea correcta
- Formato correcto: `https://[org].crm[region].dynamics.com`
- Sin `/` al final

### Error: "Access denied"

- Tu usuario debe tener permisos para leer registros de auditoría
- Rol requerido: **System Administrator** o **Custom role** con privilegio **View Audit History**

---

## 📚 Recursos Adicionales

- [Documentación de Dataverse](https://docs.microsoft.com/power-apps/developer/data-platform/)
- [Registro de aplicaciones Azure AD](https://docs.microsoft.com/azure/active-directory/develop/quickstart-register-app)
- [Auditoría en Dataverse](https://docs.microsoft.com/power-platform/admin/manage-dataverse-auditing)

---

## 🔒 Seguridad

⚠️ **NUNCA** subas `appsettings.json` con credenciales reales a repositorios públicos.

Usa:
- **Variables de entorno** para desarrollo local
- **Azure Key Vault** para producción
- **Secrets Management** en tu pipeline CI/CD
