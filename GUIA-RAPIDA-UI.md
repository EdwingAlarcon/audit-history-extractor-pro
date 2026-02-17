# 🚀 Guía Rápida de Inicio - Audit History Extractor Pro

## 📍 ¿Dónde Está la Interfaz?

### ✅ La aplicación YA ESTÁ CORRIENDO

- **URL Principal:** https://localhost:5001
- **URL Alternativa:** http://localhost:5000

### 🌐 Cómo Acceder

1. **Abre tu navegador** (Chrome, Edge, Firefox)
2. **Ve a:** `https://localhost:5001`
3. Si ves advertencia de seguridad: Click en "**Avanzado**" → "**Continuar de todos modos**"
   - Esto es normal en desarrollo local con certificados auto-firmados

---

## 🔐 Configuración de Credenciales

### ❌ NO Edites Archivos Manualmente

**Ya NO necesitas editar `config.yaml`**. Ahora puedes configurar todo desde la interfaz web.

### ✅ Cómo Configurar Credenciales desde la UI

#### **Paso 1: Ve a Configuración**
1. En la interfaz web (https://localhost:5001)
2. Click en el menú lateral → **"Configuración"** (ícono de engranaje ⚙️)
   - O haz click en el botón "**Configurar Credenciales →**" del banner de bienvenida

####  **Paso 2: Completa los Datos**

##### Para **OAuth2** (Recomendado - Autenticación Interactiva):
```
URL del Entorno:    https://tuorg.crm.dynamics.com
Tipo de Auth:       OAuth2 (Azure AD - Interactivo)
Tenant ID:          00000000-0000-0000-0000-000000000000
Client ID:          00000000-0000-0000-0000-000000000000
```

##### Para **Client Secret**:
```
URL del Entorno:    https://tuorg.crm.dynamics.com
Tipo de Auth:       Client Secret
Tenant ID:          00000000-0000-0000-0000-000000000000
Client ID:          00000000-0000-0000-0000-000000000000
Client Secret:      tu-secreto-aqui
```

#### **Paso 3: Guardar**
- Click en **"Guardar Configuración"**
- Opcionalmente, click en **"Probar Conexión"** para verificar

---

## 🔄 Múltiples Cuentas

### ¿Necesitas Trabajar con Varios Entornos?

**Sí, puedes agregar múltiples cuentas fácilmente:**

#### En la pestaña "**Múltiples Cuentas**":

1. **Configura la primera cuenta** en la pestaña "Dataverse"
2. **Dale un nombre** (ej: "Producción", "Testing", "Dev")
3. Click en **"Guardar como Nueva Cuenta"**
4. Repite para cada entorno

#### Características:
- ✅ **Guarda múltiples configuraciones** con nombres descriptivos
- ✅ **Cambia entre cuentas** con 1 click
- ✅ **Edita o elimina** cuentas guardadas
- ✅ **No necesitas reescribir** las credenciales cada vez

---

## 🎯 Cómo Obtener las Credenciales

### Para Conectarte a Dataverse Necesitas:

#### 1️⃣ **URL del Entorno**
   - Formato: `https://TUORG.crm.dynamics.com`
   - Ejemplo: `https://contoso.crm.dynamics.com`
   - La encuentras en: Power Platform Admin Center

#### 2️⃣ **Tenant ID**
   - Azure Portal → Azure Active Directory → Overview → Tenant ID
   - Formato: `12345678-1234-1234-1234-123456789012`

#### 3️⃣ **Client ID (Application ID)**
   - Azure Portal → App Registrations → Tu App → Overview
   - También llamado "Application ID"

#### 4️⃣ **Client Secret** (si usas ese método)
   - Azure Portal → App Registrations → Tu App → Certificates & secrets
   - Click "New client secret"
   - ⚠️ **Cópialo inmediatamente** (solo se muestra una vez)

---

## 📱 Navegación en la Interfaz

### Menú Lateral Disponible:

| Ícono | Página | Función |
|-------|--------|---------|
| 📊 | **Dashboard** | Vista general y estadísticas |
| 📦 | **Extraer Auditoría** | Extraer registros de auditoría |
| 📥 | **Exportar** | Exportar a Excel, CSV, JSON |
| 📜 | **Historial** | Ver extracciones anteriores |
| ⚙️ | **Configuración** | Gestionar credenciales y cuentas |

---

## 🚀 Flujo de Trabajo Típico

### 1️⃣ Primera Vez

```
1. Abrir https://localhost:5001
2. Ir a Configuración
3. Ingresar credenciales de Dataverse
4. Guardar y probar conexión
```

### 2️⃣ Extraer Auditoría

```
1. Ir a "Extraer Auditoría"
2. Seleccionar entidad (account, contact, etc.)
3. Elegir rango de fechas
4. Click "Extraer"
5. Ver progreso en tiempo real
```

### 3️⃣ Exportar Resultados

```
1. Ir a "Exportar"
2. Seleccionar formato (Excel, CSV, JSON)
3. Elegir destino
4. Click "Exportar"
5. Descargar archivo
```

---

## 🔧 Comandos Útiles

### Iniciar la Aplicación
```powershell
cd C:\AuditHistoryExtractorPro
dotnet run --project src/AuditHistoryExtractorPro.UI
```

### Detener la Aplicación
```powershell
# Presionar Ctrl+C en la terminal
# O ejecutar:
Get-Process | Where-Object { $_.ProcessName -eq 'AuditHistoryExtractorPro.UI' } | Stop-Process
```

### Verificar que Está Corriendo
```powershell
netstat -ano | Select-String "5001" | Select-String "LISTENING"
```

---

## ❓ Preguntas Frecuentes

### ¿Puedo usar múltiples cuentas simultáneamente?
❌ No simultáneamente, pero ✅ **puedes guardar múltiples configuraciones** y cambiar entre ellas con 1 click.

### ¿Las credenciales se almacenan de forma segura?
✅ Por defecto en archivos locales. Para producción, usa **Azure Key Vault** (configurable en pestaña "Avanzado").

### ¿Necesito reiniciar después de cambiar configuración?
❌ No, los cambios se aplican inmediatamente.

### ¿Funciona offline?
❌ No, necesitas conexión a internet para acceder a Dataverse.

### ¿Puedo exportar mis configuraciones?
✅ Sí, las configuraciones se guardan en `config.yaml` que puedes respaldar.

---

## 🆘 Solución de Problemas

### No Puedo Acceder a la URL

```powershell
# 1. Verificar que la app está corriendo
Get-Process | Where-Object { $_.ProcessName -eq 'AuditHistoryExtractorPro.UI' }

# 2. Verificar puertos
netstat -ano | Select-String "5001"

# 3. Si no está corriendo, ejecutar:
cd C:\AuditHistoryExtractorPro
dotnet run --project src/AuditHistoryExtractorPro.UI
```

### Error de Certificado en el Navegador
✅ **Normal en desarrollo local**
- Click "Avanzado" → "Continuar de todos modos"
- O confía el certificado de desarrollo:
```powershell
dotnet dev-certs https --trust
```

### La Página No Carga
1. Verifica que usas `https://localhost:5001` (con la 's')
2. Prueba con `http://localhost:5000`
3. Limpia caché del navegador (Ctrl+Shift+Del)

### Error de Conexión a Dataverse
1. Verifica que las credenciales son correctas
2. Revisa que tu app en Azure tiene permisos para Dataverse
3. Verifica que la URL del entorno es correcta
4. Intenta el botón "Probar Conexión" en Configuración

---

## 📞 Ayuda Adicional

- 📖 **Documentación Completa:** [docs/user-guide.md](../docs/user-guide.md)
- 🏗️ **Arquitectura:** [docs/architecture.md](../docs/architecture.md)
- 🐛 **Reportar Problemas:** [GitHub Issues](https://github.com/your-org/audit-history-extractor-pro/issues)

---

## ✅ Checklist de Inicio Rápido

- [ ] Aplicación corriendo en https://localhost:5001
- [ ] Puedo acceder a la interfaz en el navegador
- [ ] Configuré credenciales en la pestaña "Configuración"
- [ ] Probé la conexión exitosamente
- [ ] Guardé la configuración con un nombre descriptivo
- [ ] Exploré el menú y las diferentes páginas

---

**¡Listo! Ya puedes empezar a extraer auditorías de Dataverse** 🎉

**Última actualización:** 17 de febrero de 2026
