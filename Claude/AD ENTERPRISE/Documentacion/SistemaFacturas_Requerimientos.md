# AD ALL DIGITAL ENTERPRISE — Sistema de Facturación
**Producto:** AD ALL DIGITAL ENTERPRISE — Sistema multiempresa  
**Empresa piloto:** Fanny Mercedes & Asoc. — Oficina de Abogados  
**Nombre del sistema:** AD ALL DIGITAL ENTERPRISE  
**Modelo:** Un sistema, múltiples instalaciones (una por empresa cliente)  
**Logo del sistema:** AD DIGITAL (por crear)  
**Logo de la empresa:** Se configura por instalación  
**Backend de datos:** Google Sheets en Google Drive (una hoja por empresa)  
**Tecnología:** ASP.NET Core 8 — Acceso Web Multiusuario  
**Usuarios simultáneos estimados:** ~5 por instalación  
**Estado:** EN DESARROLLO  

---

## 0. MODELO MULTIEMPRESA

### Concepto:
AD ALL DIGITAL ENTERPRISE es un **producto** que se instala para diferentes empresas.
Cada instalación tiene su propia base de datos en Google Drive.

### Nombre del archivo Google Sheets por empresa:
```
AD Enterprise.[Nombre de la Empresa]
```
**Ejemplos:**
- `AD Enterprise.Fanny Mercedes & Asoc.`
- `AD Enterprise.Costa Sur Dominicana`
- `AD Enterprise.Mi Empresa SRL`

### Flujo de primera instalación (Setup Wizard):
1. Al ejecutar por primera vez, el sistema detecta que NO hay configuración
2. Muestra una pantalla de **Configuración Inicial**
3. El usuario ingresa:
   - Nombre de la empresa
   - Logo de la empresa (upload de imagen)
   - Usuario y contraseña del primer Administrador
4. El sistema **crea automáticamente** el archivo Google Sheets con el nombre correcto
5. Inicializa todas las hojas maestras (USUARIOS, COMPANIAS, CLIENTES, PAGOS)
6. Guarda el SpreadsheetId en la configuración local
7. Redirige al login — sistema listo para usar

### Configuración local (appsettings.json por instalación):
```json
{
  "Empresa": {
    "Nombre": "Fanny Mercedes & Asoc.",
    "SpreadsheetId": "[ID del archivo creado automáticamente]",
    "SpreadsheetNombre": "AD Enterprise.Fanny Mercedes & Asoc."
  }
}
```

### APIs de Google necesarias:
- **Google Sheets API** — leer/escribir datos ✅ (ya habilitada)
- **Google Drive API** — crear el archivo Sheets nuevo (a habilitar)

---

## 1. ARQUITECTURA GENERAL

- Aplicación web en ASP.NET Core 8 (Razor Pages)
- Google Sheets como base de datos (via Google Sheets API v4)
- Autenticación con Google OAuth 2.0 (cuenta r.mercedesb@gmail.com)
- Multiusuario con control de perfiles propio dentro del sistema
- **Fase 1 — Pruebas:** instalación local en PC Windows con acceso remoto via Cloudflare Tunnel (gratis)
- **Fase 2 — Producción:** migración a servidor en la nube (ver sección 12)
- La base de datos (Google Sheets + Drive) vive en la nube — no depende del servidor local
- Acceso remoto desde internet + red local
- Idioma de la interfaz: **Español**

### Identidad visual
- Logo sistema: **AD DIGITAL** (por crear — se integra después)
- Logo empresa: **Fanny Mercedes & Asoc.** (imagen FM — ya disponible)
- El logo de la empresa aparece en:
  - Encabezado de la aplicación web (todas las pantallas)
  - Encabezado de todos los reportes / PDF
  - Comprobantes de pago en PDF

---

## 2. ESTRUCTURA DE HOJAS EN GOOGLE SHEETS

### Hojas de facturas (existentes — una por año)
| Columna | Campo | Tipo |
|---------|-------|------|
| A | N° Transacción Factura | Número único global (nunca cambia) |
| B | Empresa | Texto |
| C | N° Ticket | Número continuo global |
| D | Nombre (cliente) | Texto |
| E | Monto (RD$) | Moneda |
| F | Fecha de factura | Fecha |
| G | Estado | PENDIENTE / PAGADO PARCIAL / PAGADA |
| H | Fecha de pago | Fecha |
| I | Forma de pago | Efectivo / Cheque / Transferencia / Abono |
| J | N° de cheque / Referencia | Texto (solo si aplica) |
| K | Notas | Texto libre |
| L | Valor anterior (auditoría) | JSON con snapshot antes de edición |

- Hojas actuales: `2023`, `2024`, `2025`, `2026`
- El sistema crea automáticamente la hoja del nuevo año cuando sea necesario
- **N° Ticket:** numeración continua global (no reinicia por año)
- **N° Transacción:** número único e irrepetible para cada factura — no cambia nunca, ni al editar
- **Auditoría:** al editar cualquier factura, se guarda el valor anterior en columna L

### Hojas de pagos
| Columna | Campo | Tipo |
|---------|-------|------|
| A | N° Transacción Pago | Número único global de pago |
| B | N° Transacción Factura | Referencia a la factura pagada |
| C | N° Ticket | Para búsqueda rápida |
| D | Fecha de pago | Fecha |
| E | Monto pagado | Moneda |
| F | Forma de pago | Efectivo / Cheque / Transferencia / Abono |
| G | N° cheque / Referencia | Texto (obligatorio si forma = Cheque o Transferencia) |
| H | Notas | Texto libre (obligatorio si forma = Abono o Parcial) |
| I | Usuario que registró | Auditoría |

- Hoja: `PAGOS` (una sola hoja para todos los años)
- Una factura puede tener **múltiples pagos** (abonos parciales)
- Cada pago tiene su propio N° de Transacción único

### Hojas maestras nuevas a crear
| Hoja | Propósito |
|------|-----------|
| `USUARIOS` | Control de acceso y perfiles del sistema |
| `COMPANIAS` | Catálogo de empresas con condiciones de pago |
| `CLIENTES` | Catálogo de clientes |
| `PAGOS` | Registro histórico de todos los pagos |

---

## 3. MÓDULO DE USUARIOS

### Hoja USUARIOS — Campos:
| Campo | Descripción |
|-------|-------------|
| ID | Autogenerado |
| Nombre completo | Nombre real del usuario |
| Usuario | Login para entrar al sistema |
| Contraseña | Almacenada con hash seguro (nunca texto plano) |
| Correo | Referencia (no se usa para login) |
| Perfil | `Administrador` / `Operativo` / `Consultas` |
| Estado | `Activo` / `Inactivo` |
| Fecha de creación | Registro automático |

### Permisos por perfil:

| Acción | Administrador | Operativo | Consultas |
|--------|:---:|:---:|:---:|
| Ver / Consultar datos | ✅ | ✅ | ✅ |
| Insertar facturas | ✅ | ✅ | ❌ |
| Registrar pagos | ✅ | ✅ | ❌ |
| Editar registros | ✅ | 🔐 Requiere permiso | ❌ |
| Borrar registros | ✅ | 🔐 Requiere permiso | ❌ |
| Acceso a Mantenimiento (Compañías/Clientes) | ✅ | ❌ | ❌ |
| Acceso a Facturación | ✅ | ✅ | ❌ |
| Acceso a Pagos | ✅ | ✅ | ❌ |
| Acceso a Reportes | ✅ | ✅ | ✅ |
| Acceso a Dashboard | ✅ | ✅ | ✅ |
| Gestionar usuarios | ✅ | ❌ | ❌ |

### Reglas especiales:
- Solo el **Administrador** puede crear, editar o desactivar usuarios
- El usuario **Operativo** necesita autorización del Administrador para editar o borrar
- El primer Administrador se crea durante la instalación inicial del sistema
- El login es con **usuario + contraseña** propios del sistema (no Google login en pantalla)

---

## 4. MENÚ PRINCIPAL

```
HEADER: [Logo FM] — Fanny Mercedes & Asoc.        [Usuario: xxxx | Perfil | Cerrar sesión]
─────────────────────────────────────────────────────────────────
  Mantenimiento  |  Facturación  |  Pagos  |  Reportes  |  Dashboard
```

### Submenús:
```
Mantenimiento
├── Compañías
└── Clientes

Facturación
├── Nueva Factura
└── Consulta de Facturas

Pagos
└── Registrar Pago

Reportes
├── Reporte General
├── Por Compañía
├── Por Cliente
├── Por Período / Rango de Fechas
├── Facturas Pendientes
└── Historial de Pagos

Dashboard
└── (pantalla principal con indicadores)
```

---

## 5. MÓDULO DE MANTENIMIENTO

### 5.1 Compañías
- **Crear** compañía nueva
- **Editar** datos de compañía existente
- **Desactivar** (no borrar — preservar historial de facturas)
- **Listar** todas las compañías con estado y búsqueda

**Campos:**
| Campo | Tipo | Req. |
|-------|------|:----:|
| ID | Autogenerado | — |
| Nombre | Texto | ✅ |
| RNC | Texto | ⬜ |
| Teléfono | Texto | ⬜ |
| Dirección | Texto | ⬜ |
| Correo | Email | ⬜ |
| Condición de pago | Número de días (ej: 30, 60, 90) | ⬜ |
| Estado | Activo / Inactivo | ✅ |

- La **condición de pago en días** se usa para calcular vencimiento de facturas pendientes
- Ejemplo: Compañía con condición 30 días → factura pendiente de hace 31 días aparece como **VENCIDA**

### 5.2 Clientes
- **Crear** cliente nuevo
- **Editar** datos de cliente
- **Desactivar** (no borrar)
- **Listar** con búsqueda por nombre o cédula

**Campos:**
| Campo | Tipo | Req. |
|-------|------|:----:|
| ID | Autogenerado | — |
| Nombre completo | Texto | ✅ |
| Cédula | Texto | ✅ |
| Teléfono | Texto | ⬜ |
| Correo | Email | ⬜ |
| Estado | Activo / Inactivo | ✅ |

---

## 6. MÓDULO DE FACTURACIÓN

### 6.1 Nueva Factura

**Campos del formulario:**
| Campo | Editable | Notas |
|-------|:--------:|-------|
| N° Transacción | ❌ | Generado automáticamente — nunca editable |
| N° de Ticket | ✅ | El sistema asigna el siguiente disponible (editable si es necesario) |
| Compañía | ✅ | Selector desplegable desde catálogo |
| Cliente (Nombre) | ✅ | Selector desde catálogo + opción de texto libre |
| Fecha de factura | ✅ | Date picker — por defecto hoy |
| Monto (RD$) | ✅ | Campo numérico con formato moneda |
| Estado | ❌ | Automático: `PENDIENTE` al crear |
| Fecha de pago | ❌ | Bloqueado — se llena al registrar pago |
| Forma de pago | ❌ | Bloqueado — se llena al registrar pago |
| N° cheque / Referencia | ❌ | Bloqueado — se llena al registrar pago |
| Notas | ✅ | Texto libre opcional |

- Factura nueva siempre inicia como `PENDIENTE`
- Se guarda en la hoja del año de la fecha de factura
- Si se **edita** la factura después de creada: el sistema guarda un snapshot del registro anterior (auditoría)
- El **N° Transacción nunca cambia**, aunque se edite cualquier otro campo

### 6.2 Consulta de Facturas
- Listado paginado con todos los campos
- **Filtros combinables:** Estado | Compañía | Cliente | Rango de fechas | Año
- Vista detalle de cada factura (modal o página)
- Botón **⬇ Descargar PDF** en vista detalle — genera archivo PDF descargable con:
  - Encabezado con logo Fanny Mercedes & Asoc.
  - Todos los datos de la factura
  - Si tiene pagos registrados, se muestra el historial de pagos
- Desde aquí **no se edita** (solo ver y descargar PDF)

---

## 7. MÓDULO DE PAGOS

### Estados posibles de una factura:
| Estado | Descripción |
|--------|-------------|
| `PENDIENTE` | Sin ningún pago registrado |
| `PAGADO PARCIAL` | Uno o más abonos registrados, pero no cubre el total |
| `PAGADA` | El total de pagos cubre el monto de la factura |
| `VENCIDA` | Pendiente o Parcial, superó los días de condición de pago de la compañía |

### 7.1 Registrar Pago
**Proceso:**
1. Usuario busca la factura por **N° de Ticket** o **N° Transacción**
2. El sistema muestra los datos de la factura + historial de pagos anteriores (si los hay)
3. Muestra el **saldo pendiente** (monto factura − total pagado hasta ahora)
4. Si hay saldo pendiente, habilita el formulario de nuevo pago:

| Campo | Editable | Reglas |
|-------|:--------:|--------|
| N° Transacción Pago | ❌ | Generado automáticamente |
| Fecha de pago | ✅ | Date picker — por defecto hoy |
| Monto a pagar | ✅ | No puede exceder el saldo pendiente |
| Forma de pago | ✅ | Efectivo / Cheque / Transferencia / Abono |
| N° cheque / Referencia | ✅ condicional | **Obligatorio** si forma = Cheque o Transferencia; vacío si Efectivo |
| Notas | ✅ | Texto libre — **obligatorio** si forma = Abono o monto parcial |

5. Al guardar:
   - Se crea un registro en la hoja `PAGOS`
   - Si monto pagado = saldo total → factura pasa a `PAGADA`
   - Si monto pagado < saldo total → factura pasa a `PAGADO PARCIAL`
6. Botón **⬇ Descargar PDF del Pago** — genera comprobante PDF descargable con:
   - Encabezado con logo Fanny Mercedes & Asoc.
   - Datos de la factura
   - Detalle del pago registrado
   - Saldo restante (si es parcial)

### Formas de pago disponibles:
| Forma | N° Referencia requerido | Notas requeridas |
|-------|:-----------------------:|:----------------:|
| Efectivo | ❌ | ⬜ opcional |
| Cheque | ✅ N° de cheque | ⬜ opcional |
| Transferencia Bancaria | ✅ N° de referencia | ⬜ opcional |
| Abono | ❌ | ✅ obligatorio |

**Reglas generales:**
- Solo se puede pagar una factura en estado `PENDIENTE`, `PAGADO PARCIAL` o `VENCIDA`
- Las facturas en `PAGADA` no admiten más pagos (solo lectura)
- El N° Transacción de cada pago nunca cambia
- Si se edita un pago (solo Administrador), se guarda el valor anterior en auditoría

---

## 8. MÓDULO DE REPORTES

### Criterios de filtrado disponibles (combinables entre sí):
- **Compañía:** selección múltiple (checklist o multi-select)
- **Cliente:** selección múltiple
- **Rango de fecha de factura:** desde — hasta
- **Rango de fecha de pago:** desde — hasta
- **Estado:** PAGADA | PENDIENTE | Ambas
- **Año:** selector de hoja/año

### Reportes disponibles:

| # | Reporte | Descripción |
|---|---------|-------------|
| 1 | General de Facturas | Todas las facturas con filtros combinados |
| 2 | Facturas Pendientes | Solo pendientes, con antigüedad |
| 3 | Facturas Pagadas | Solo pagadas, con fecha y cheque |
| 4 | Por Compañía | Agrupado por empresa, subtotales |
| 5 | Por Cliente | Agrupado por cliente, subtotales |
| 6 | Por Período | Facturas en rango de fechas con totales |
| 7 | Historial de Pagos | Pagos realizados en rango de fechas |
| 8 | Clientes con Mayor Deuda | Ranking de pendientes por cliente |

### Formatos de salida (todos los reportes):
- ✅ Vista en pantalla (con paginación)
- ✅ Exportar a **PDF** (con logo en encabezado)
- ✅ Exportar a **Excel** (.xlsx)
- ✅ **Imprimir** (genera PDF para impresión)

---

## 9. DASHBOARD

Pantalla de inicio con indicadores visuales:

| Indicador | Descripción |
|-----------|-------------|
| Total de Cuentas | Cantidad total de facturas + monto total |
| Cuentas PAGADAS | Cantidad y monto total pagado |
| Cuentas PENDIENTES | Cantidad y monto total pendiente |
| Balance Activo por Cliente | Tabla: cliente → monto pendiente total |
| Balance Activo por Compañía | Tabla: empresa → monto pendiente total |
| Historial de Pagos recientes | Últimos N pagos registrados |
| Clientes con Mayor Deuda | Ranking top 5 o 10 por monto pendiente |

- Los indicadores de cantidad/monto se muestran como **tarjetas** (cards)
- Las tablas son interactivas (clic lleva al reporte detallado)
- El Dashboard está disponible para todos los perfiles

---

## 10. MÓDULO DE MIGRACIÓN DE DATOS HISTÓRICOS

### Ubicación: Menú Mantenimiento → Importar Datos

### Proceso:
1. El sistema provee una **plantilla Excel descargable** con el formato exacto requerido
2. El usuario llena la plantilla con los datos históricos
3. El usuario sube el archivo desde la pantalla de importación
4. El sistema **valida** el archivo antes de importar (errores en rojo, advertencias en amarillo)
5. Se muestra una vista previa de los datos a importar
6. El usuario confirma → el sistema importa fila por fila

### Plantilla de importación de Facturas (columnas del Excel):
| Columna Excel | Campo | Obligatorio | Notas |
|---------------|-------|:-----------:|-------|
| A | Año | ✅ | 2023, 2024, 2025... |
| B | N° Ticket | ✅ | Número original |
| C | Empresa | ✅ | Debe existir en catálogo o se crea |
| D | Nombre Cliente | ✅ | |
| E | Monto (RD$) | ✅ | Solo números |
| F | Fecha Factura | ✅ | Formato DD/MM/AAAA |
| G | Estado | ✅ | PAGADA o PENDIENTE |
| H | Fecha Pago | ⬜ | Solo si PAGADA |
| I | Forma de Pago | ⬜ | Efectivo/Cheque/Transferencia |
| J | N° Cheque / Ref | ⬜ | Solo si aplica |
| K | Notas | ⬜ | |

### Campos que agrega el sistema automáticamente al importar:
| Campo interno | Valor |
|---------------|-------|
| N° Transacción | Generado automáticamente (correlativo) |
| Origen de datos | `MIGRACIÓN` |
| Fecha de migración | Fecha en que se realizó la importación |
| Usuario que migró | Usuario del sistema que ejecutó la importación |

### Validaciones antes de importar:
- N° Ticket no duplicado
- Fechas en formato válido
- Monto mayor a cero
- Estado = PAGADA o PENDIENTE (únicamente)
- Si Estado = PAGADA y no hay Fecha de Pago → advertencia (no bloquea)

---

## 11. SERVIDOR Y DESPLIEGUE

### FASE 1 — Pruebas locales (inmediato, costo $0)
| Componente | Solución |
|------------|---------|
| Servidor app | PC Windows local con `dotnet run` o IIS Express |
| Acceso remoto | **Cloudflare Tunnel** (gratis, sin abrir puertos, HTTPS automático) |
| Base de datos | Google Sheets en Drive (ya en la nube) |
| HTTPS | Incluido automáticamente con Cloudflare Tunnel |

**Cloudflare Tunnel:** crea una URL pública segura (ej: `addigital.cfargotunnel.com`) que redirige al servidor local. Gratis, sin necesidad de IP fija ni abrir puertos del router.

### FASE 2 — Producción en la nube (opciones económicas)

| Opción | Costo aprox. anual | Pros | Contras |
|--------|:-----------------:|------|---------|
| **Railway.app** | ~$60/año ($5/mes) | Fácil deploy, .NET soportado, HTTPS automático | Límite de recursos en plan básico |
| **Render.com** | $84/año ($7/mes) | Muy fácil, HTTPS, deploy desde GitHub | Se "duerme" en plan gratis |
| **Fly.io** | ~$0–$36/año | Plan gratis generoso, .NET soportado | Más técnico de configurar |
| **Azure App Service** | ~$120/año (B1) | Microsoft nativo, .NET perfecto | Más caro, más complejo |
| **DigitalOcean Droplet** | ~$72/año ($6/mes) | Control total, Linux VPS | Requiere configurar manualmente |
| **Hetzner VPS** | ~$48/año (€3.29/mes) | Más barato de Europa, buen rendimiento | Servidores en Europa (latencia leve) |

### ✅ Recomendación para este proyecto:
**Railway.app (~$5/mes)** → más sencillo para empezar, soporta .NET Core directamente, HTTPS automático, deploy con un comando. Cuando el sistema esté listo en pruebas locales, se publica en Railway en menos de 1 hora.

### Nota sobre la base de datos:
- Google Sheets/Drive ya está en la nube → **no necesitas base de datos en el servidor**
- El servidor solo corre la aplicación web — los datos siempre están en tu Google Drive
- Esto hace que cualquier hosting económico sea suficiente

---

## 12. PREGUNTAS PENDIENTES ⬜

- [ ] ¿Tienes dominio propio? (ej: addigital.com) o usamos subdominio del hosting
- [ ] ¿Cuándo estará disponible el logo de AD DIGITAL?

---

## 13. PENDIENTES TÉCNICOS ⬜

- [ ] Guardar imagen del logo FM en el proyecto (✅ imagen recibida, pendiente integrar)
- [ ] Crear/confirmar logo AD DIGITAL para el sistema
- [ ] Confirmar ID del Spreadsheet de producción en Google Drive
- [ ] Crear proyecto en Google Cloud Console
- [ ] Configurar Google Sheets API y credenciales OAuth2
- [ ] Configurar Cloudflare Tunnel para pruebas locales
- [ ] Crear usuario Administrador inicial
- [ ] Crear plantilla Excel de migración histórica

---

*Última actualización: 2026-06-06*  
*Estado: EN DEFINICIÓN — ⛔ No iniciar desarrollo hasta aprobar este documento*
