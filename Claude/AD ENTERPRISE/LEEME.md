# AD ALL DIGITAL ENTERPRISE
## Sistema de Facturación — Fanny Mercedes & Asoc.

---

## Estructura de carpetas

```
AD ENTERPRISE/
│
├── Assets/
│   └── Logos/
│       ├── Logos_AD_Digital.html     ← Vista previa de las 3 opciones de logo
│       └── Logo_AD_Digital_v1.svg    ← Logo oficial Opción 2 (azul corporativo)
│
├── Documentacion/
│   └── SistemaFacturas_Requerimientos.md  ← Documento de requerimientos completo
│
├── Versiones/
│   ├── v1.0/
│   │   ├── Instalable/    ← Setup ejecutable para instalar en Windows
│   │   └── Local-Run/     ← Carpeta para correr localmente sin instalar
│   ├── v1.1/              ← (se crea cuando haya nueva versión)
│   └── v2.0/              ← (versiones mayores)
│
└── LEEME.md               ← Este archivo
```

---

## Convención de versiones

| Versión | Descripción |
|---------|-------------|
| v1.0 | Primera versión funcional completa |
| v1.x | Correcciones y mejoras menores |
| v2.0 | Nueva versión con cambios importantes |

## Contenido de cada carpeta de versión

### /Instalable
- `ADEnterprise_vX.X_Setup.exe` — instalador para Windows
- Instala la app como servicio o en el menú de inicio

### /Local-Run
- Archivos de la app listos para correr con `dotnet run`
- Incluye archivo `INICIAR.bat` para arrancar con doble clic
- No requiere instalación — ideal para pruebas

---

*Proyecto iniciado: 2026-06-06*
