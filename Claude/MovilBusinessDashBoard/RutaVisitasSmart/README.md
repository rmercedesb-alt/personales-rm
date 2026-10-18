# RutaVisitasSmart

Módulo independiente (fuera de `MovilBusinessDashBoard*`) para mantenimiento, consulta,
reportes y visualización de `RutaVisitas`. Stack: **Node/Express + TypeScript** (API,
solo lectura) y **React + TypeScript + Vite + Leaflet** (frontend).

## Cómo correrlo

```bash
CORRER_LOCAL.bat
```

o manualmente: `npm install` en `server/` y `client/`, luego `npm run dev` en la raíz
(usa `concurrently` para levantar API en `:4000` y app en `:5173`, con proxy `/api`
configurado en Vite).

## Decisiones de arquitectura (2026-08-09)

- **Solo lectura por ahora.** El backend nunca hace INSERT/UPDATE/DELETE contra
  `MovilBusiness5STD`. La conexión (`server/.env`, no versionado) reutiliza el mismo
  servidor/usuario que los dashboards .NET (`192.168.0.1`, user `Ventas`) — es una
  reutilización de credenciales existentes, no un usuario dedicado de solo lectura;
  pedir a TI uno dedicado antes de dar esto por definitivo en producción.
- **Tabla autoritativa: `RutaVisitas`** (no `RutaVisitasFechaHistoricos`, que es
  bitácora histórica). Columnas reales: `RepCodigo`, `CliID`, `RutSemana1..5` (código
  de 7 posiciones Lun..Dom; cualquier dígito no-cero en una posición = visita ese día
  esa semana del mes — así se modela la frecuencia semanal/quincenal/mensual),
  `RutPosicion`, `RutLatitud/RutLongitud` (override opcional de ubicación).
- **Resolución de "día de visita"** (`server/src/diaResolver.ts`): se toma
  `RutSemana1` como patrón representativo y se ubica la primera posición no-cero.
  Hay ~26k filas y el patrón puede variar entre `RutSemana1..5` (frecuencia
  quincenal/mensual) — el día mostrado hoy es el de la semana 1, no un merge de las 5.
- **Tipo de Negocio** = tabla `TiposNegocio` vía `Clientes.TiNID` (Colmados,
  Supermercado, Mini Market, Almacenes, etc. — ya está en los filtros).
- **Tamaño** = `Clasifica` vía `Clientes.ClaID` (ya en los dashboards existentes).
- **Mapa: Leaflet + OpenStreetMap** (mismo criterio que el módulo Geo de los
  dashboards .NET: gratis, sin API key).

## Qué hay hoy (primer corte / prueba)

Pantalla **"Por Día / Supervisor"**: sidebar con los módulos futuros marcados
"Próximamente", filtros (Día, Supervisor, Tipo de Negocio, Tamaño), y mapa con
drill-down de 3 niveles:

1. **Overview** — un polígono (envolvente convexa) por combinación
   Día × Supervisor, coloreado por día.
2. **Supervisor** — click en un polígono → un polígono por vendedor dentro de ese
   día/supervisor, panel lateral con el conteo de clientes **distintos** por
   vendedor (un cliente con 2 visitas no se duplica).
3. **Vendedor** — click en un vendedor → puntos individuales de cada cliente
   (popup con nombre, código, RutPosición, tamaño, tipo de negocio).

Breadcrumb arriba del mapa para volver a niveles anteriores.

## Pendiente (fuera del alcance de esta primera prueba)

- Vistas alternas: Supervisor/Vendedor/Día/Clientes y Supervisor/Tamaño/Día/Clientes.
- Crear Ruta / Modificar Ruta: panel de clientes arrastrables, ordenamiento por
  vecino más cercano, `RutPosicion` de 5 en 5, dibujo de polígono en el mapa para
  traer clientes de una zona, y exportación a Excel con el script SQL sugerido para
  que TI lo aplique (la app nunca escribe directo a la BD).
- Reportes.
- Confirmar con el usuario si conviene mostrar el día "unión" de `RutSemana1..5`
  o dejarlo explícitamente por semana del mes (frecuencia).
