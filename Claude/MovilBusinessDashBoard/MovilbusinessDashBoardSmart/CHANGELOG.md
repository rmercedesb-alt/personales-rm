# MovilBusiness Dashboard Smart — Historial de versiones

Cada versión es una **foto congelada** del sistema. El número sale de `<Version>` en
`DashSmart.csproj`; el instalable se nombra con esa versión y `/version` la reporta en vivo.

**Flujo:** las mejoras se acumulan en **"Sin liberar"**. La versión y el instalable se generan
**solo cuando se pide expresamente** una versión nueva (así se instala un lote, no cada ajuste).

**Cómo liberar una versión nueva (cuando se indique):**
1. Sube `<Version>` en `DashSmart.csproj` (ej. 1.2.1 → 1.3.0).
2. Mueve lo acumulado de "Sin liberar" a la nueva versión, con fecha.
3. Ejecuta `CREAR_INSTALABLE_IIS.ps1` (FULL, framework-dependent — el server necesita .NET 10+
   instalado) o `CREAR_INSTALABLE_STANDALONE.ps1` (lleva su propio runtime .NET adentro, el server
   no necesita nada instalado salvo el Módulo ASP.NET Core de IIS — usado en las últimas
   releases). Genera `Instalables\DashSmart_IIS_v<version>_<fecha>.zip` o
   `DashSmart_STANDALONE_v<version>_winx64_<fecha>.zip`.
4. En el servidor, corre `ACTUALIZAR_IIS.ps1` del paquete (conserva tu `appsettings.json`).
   Guarda los ZIP anteriores: son tus puntos de retorno.

---

## Sin liberar

- **Geolocalizaciones → nueva opción «Visitas del día» (P4, 2026-10-17).** Elige fecha + centro/supervisor/vendedor (dependientes) y «Generar»: las **casitas** son los clientes programados ese día
  (RutaVisitasFechaHistoricos) y los **puntos** las visitas realizadas (más pequeños, sobre las casitas): **verde = visita efectiva** (con pedido), **amarillo = sin pedido**; al pasar el mouse muestran hora, cliente, si fue
  efectiva, monto y distancia. Casita **roja** = no visitada a la hora elegida, **verde** = visitada en su lugar, **azul** = visitada fuera de su lugar (distancia GPS > N m, por defecto 200, editable; incluye virtuales).
  Línea de tiempo dentro del mapa (7:00 AM – 7:00 PM; se amplía si hay visitas fuera de ese rango) con «Filtro de visitas hasta las HH:MM», **Play** que recorre las visitas de la primera a la última (Lento / Medio / Rápido;
  al generar arranca solo en lento para ver cómo entran) y 4 tarjetas dinámicas (clientes a visitar, visitados, visitas efectivas, monto prevendido; cada una con **Cumplimiento de ruta %** y **Efectividad %**). Botón «Ocultar tarjetas» y
  botón en los filtros para ocultar menú y filtros y dejar el mapa a pantalla completa. Exporta a CSV. Backend: `GET /apigeo/visitas-dia`.
- **P1 — Filtros dependientes Centro → Supervisor → Vendedor en todas las ventanas (2026-10-17).** Al elegir un supervisor (o un centro) solo se ofrecen sus vendedores, y al elegir un centro solo sus supervisores; la selección que siga
  siendo válida se conserva y la que ya no aplica se descarta. Ahora lo hacen también: **Visitas (Home)**, **Productividad** (el endpoint de filtros devuelve supervisor y centro de cada representante) y, en Supervisión Dinámica,
  **PDF Presentaciones**, **Resumen Cierre del Día** y **Cierre Diario** (estos dos además respetan la División elegida). Ya lo hacían: Geolocalizaciones, Seguimientos, Torre, Históricos (todas las pestañas), Presupuestos
  y los filtros de Plan/Reportes/Análisis (por supervisor). «Presupuestos — Gestión» captura los vendedores como códigos escritos (no hay lista de vendedores que filtrar).

## v1.16.1 — 2026-10-15 · Plan de Trabajo con aprobación y avisos visibles, Ventas vs. Presupuesto como Presupuestos, Resumen Cierre con calendarios y supervisores por persona, selector de visitas presenciales y PDF más livianos

- **Resumen Cierre del Día (Supervisión Dinámica → PDF Presentaciones) — 3 mejoras (2026-10-14):**
  (1) **Diferencia ajustada** (ejecutado − presupuesto ajustado: cuánto se dejó de vender sobre la meta ajustada) en todas las tablas del punto 1; la anterior pasa a llamarse «Diferencia lineal».
  (2) **Supervisores agrupados por persona** en Pedidos, Cumplimiento y Efectividad y AMCO-GT (y en hallazgos y compromisos): una fila con su nombre y debajo sus grupos (Ramón, Eliezer, Cristiano) o, si tienen un solo grupo
  con 2 divisiones (Narciso, Rafael), el detalle por división. Se suma con montos/conteos, no promediando %.
  (3) Nuevo **3.1 Detalle histórico (30 días)** con 3 calendarios a todo el ancho: Pedidos RD$, Activaciones y AMCO-GT de la compañía (día por día, ▲▼ vs. el mismo día de la semana anterior). El de AMCO-GT se calcula
  en el navegador (≈15 s) y el PDF/correo esperan a que termine.
- **PDF Presentaciones → «Ventas Proyectadas próxima semana», punto 1 «Ventas vs. Presupuesto al día y proyectado»: ahora con las mismas columnas y cifras de Presupuestos (2026-10-12).**
  Cada bloque (Por Centro, Canal, Supervisor y Suplidor) muestra `#, nombre, Presupuesto, Pres. al día, Ejecutado, Alcance%, Déficit al Momento, Proyectado, Proy.%, Estimado, Est.%, Déficit Estimado y Necesidad Diaria`,
  más 10 tarjetas (Presupuesto del mes, Presupuesto al día, Ejecutado, Logro al día, Alcance del mes, Proyectado, Estimado, Logro estimado, Déficit Estimado y Necesidad Diaria); en el detalle, columnas «Logro al día%» y «Logro Est.%». Se calcula con el mismo motor de Presupuestos → Detalle (días hábiles de `PresupuestosCore`, avance con 1 día de rezago,
  Proyectado lineal, Estimado con la curva de estacionalidad de cada canal, solo vendedores activos) y se suma por nivel; los % y la Necesidad Diaria se recalculan sobre el total de cada fila. Verificado contra
  `/apipresupuestos/drill/organizacion`: coincide al peso en Minorista, Mayorista y TOTAL. Los informes guardados antes de este cambio se siguen viendo con el formato anterior.
- **Fix (2026-10-11): los avisos tipo toast quedaban DETRÁS de las ventanas emergentes** (z-index 50 < 60), así que un error o validación al guardar un plan no se veía; ahora van por encima y los errores de guardado del plan
  también salen como mensaje fijo en la ventana y en una alerta.
- **Plan de Trabajo: confirmación visible y señales de «pendiente de aprobación» (2026-10-10/11).** Al guardar/enviar/cargar un plan aparece una **ventana de confirmación** con el **número (PT-xxxxx)**, el **estado**
  (Por aprobar / Borrador — no enviado / Activo) y qué sigue; antes solo salía un aviso de 2 segundos que se perdía. Insignia roja en «🗓️ Plan» y «✅ Ejecución del plan»: Gerencia ve cuántos planes esperan su
  aprobación; el supervisor ve los suyos por aprobar, en borrador o sin cerrar (se refresca cada 2 min). Al abrir «Ejecución del plan» se ocultan los filtros de Priorización (no aplican) y la vista sube al plan.

## v1.16.0 — 2026-10-10 · Plan de Trabajo semanal del supervisor, PDF más livianos y selector de visitas presenciales (AMCO-GT)

- **Seguimientos: selector «Visitas: Todas / Solo presenciales» (2026-10-10).** Por defecto «Todas» (nada cambia). «Solo presenciales» mide el AMCO-GT sin las visitas virtuales
  (usa `tipoVisita=presencial` de `/apiseg/semana`); aplica al informe, a la variación semanal y se imprime en el encabezado y en el Excel. No aplica al modo «Por Suplidor».
- **PDF más livianos (2026-10-10):** todos los PDF generados por imagen (PDF Presentaciones/Reportes de Supervisión, Históricos y Presupuestos) usan JPEG comprimido (calidad 0.85, fondo blanco)
  en vez de PNG; `ppExportarPDF` lo usa por defecto (`jpeg:false` fuerza PNG). Prueba: un reporte de texto pasó de 58 KB a 24 KB por imagen; con gráficas la reducción es mayor.
- **Plan de Trabajo (ajustes 2026-10-10):** reunión matinal 7:30–7:55 solo de lunes a viernes; la propuesta ordena a los vendedores **primero por prioridad y luego por AMCO-GT** (el más bajo primero;
  sin dato al final; desempata por tiempo sin acompañamiento) usando el AMCO-GT de la semana anterior al plan (misma fórmula que Priorización); «Foco en riesgo» = prioritarios y AMCO-GT < 70 con 2 visitas cada uno;
  la meta de cada actividad muestra el AMCO-GT del vendedor.
- **Supervisión Dinámica → Plan de Trabajo semanal del supervisor (2026-10-10).** Botón «🗂 Crear Plan de Trabajo semanal» en Plan: asistente por
  supervisor + rango de fechas con 4 opciones de propuesta (Equilibrado, Foco en riesgo, Cobertura total, En blanco) que protege los **días de calle**
  y de **supervisión telemática** (según el cronograma), fija **cobros dinámicos 3:30 pm**, **reunión de seguimiento 5:30 pm (L–V)**, **reunión con Gerencia
  (sábado)** y el **cierre del plan el viernes**; avisos al guardar (días sin calle/telemática, traslapes, vendedores sin actividad). Se guarda en tablas
  nuevas con secuencia única `PT-00001…` (`SupervisionPlan_RM`, `…Actividad_RM`, `…Ejecucion_RM`; **TI debe correr `SQL/SupervisionPlanTrabajo_RM.sql`**).
  Nueva subpestaña «✅ Ejecución del plan»: lista de planes con % de logro, doble clic en una actividad para registrar trabajo realizado, fecha, vendedor/cliente
  atendido y resultado; actividades adicionales (extra); cierre semanal (viernes) que congela el % de logro (Realizada=100, Parcial=50; ponderado por peso).
  Carga de un plan ya hecho desde **Excel** (plantilla con instrucciones/listas, validación previa y confirmación) y descarga a Excel. En **Reportes** nuevo
  «Catálogo de reportes» (plan de trabajo, avance, cierre, consolidado + enlaces a Cobros del día, Supervisión dinámica, Calle, Acompañamientos, Auditorías,
  Compromisos) con formatos: Agenda/Por vendedor/Ejecutivo, Ejecutivo/Detallado por día/Pendientes, Cierre completo/Gerencia/Mensaje WhatsApp; PDF y Excel.
  Archivos: `Controllers/ApiSupervisionPlanController.cs`, `Views/Supervision/_PlanTrabajo.cshtml`, `Views/Supervision/Index.cshtml`.
  **Flujo de aprobación (2026-10-10):** el supervisor arma el plan (Borrador) y lo **envía a aprobación**; **Gerencia** lo revisa, puede **ajustarlo**
  («Ajustar plan»), **aprobarlo** (queda Activo) o **devolverlo con comentarios**. La ejecución se registra **día a día** con el plan Activo; el **cierre** lo hace el
  supervisor **desde el viernes** (antes, solo Gerencia): si pasa el viernes sin cerrarlo, el plan se muestra **«Sin cerrar»** (con aviso) y, si se cierra tarde, queda marcado
  «cierre tardío». El supervisor puede cambiar TODO lo sugerido (día, hora, tipo, texto, vendedor, cliente/lugar, meta, peso, agregar/quitar actividades); después de aprobado
  solo agrega «actividades adicionales» (extra). Un plan cargado por Excel por un supervisor queda «Por aprobar». **Tablas renombradas:** `SupervisionPlanTrabajo_RM`,
  `SupervisionPlanTrabajoAct_RM`, `SupervisionPlanTrabajoEje_RM` (la `SupervisionPlan_RM` ya existía: es la del Plan de prioridades y no se toca); el SQL elimina las dos
  tablas vacías de la primera corrida (`SupervisionPlanActividad_RM`/`…Ejecucion_RM`). Reunión con Gerencia: sábados 8:30 am; reunión diaria 5:30 pm L–V. **Un plan por persona (2026-10-10):** el supervisor se trata como persona aunque tenga varios grupos/divisiones (Ramón = S1+S4+S7, Eliezer = S2+S5, Cristiano = S12+S13; se agrupan por
  el nombre sin el sufijo entre paréntesis): un solo plan con el equipo de todos sus grupos y la lista de vendedores de todos ellos; cualquier código de la persona da acceso. Cada actividad
  indica **con quién** (todo el equipo, un grupo, una división o un vendedor) y la **modalidad** (presencial, virtual/Teams o llamada colectiva). **Reunión matinal 7:30–7:55 am** (Lun–Sáb, todo el equipo)
  en todo plan; **reuniones fijas semanales** por supervisor (ej. jueves 5:00 pm virtual por Teams) que se incluyen solas en cada propuesta (tabla nueva `SupervisionPlanTrabajoRec_RM`);
  los cobros dinámicos ahora terminan a las 4:55 pm. Excel: dos columnas nuevas al final (Con quién / Modalidad).

- **Fix «Resumen Cierre del Día» — AMCO-GT por supervisor/compañía no coincidía con Seguimientos (2026-10-09).** El informe
  promediaba «Fuera de ruta» y «Fuera de PDV» al agrupar vendedores, mientras Seguimientos usa la SUMA (aggMetrics). Con datos del
  09-oct el total salía 80.8 en el informe vs 68.6 en Seguimientos. Ahora usa la suma y coincide exacto (68.6 y los 7 supervisores).
- **`/apiseg/semana` acepta `tipoVisita=presencial` (2026-10-09, opcional; por defecto nada cambia).** Calcula el AMCO-GT solo con
  visitas presenciales (`Visitas.VisTipoVisita='1'`; '2' = virtual): Inicio/Cierre, Ritmo, Fuera de PDV, sin venta e inventarios salen
  de visitas presenciales; los pedidos nacidos de una visita virtual no cuentan (Efectividad, Fuera de ruta) y «Visitados» =
  clientes distintos con visita presencial (ResumenVisitasRm.Visitados incluye las virtuales). Sirvió para la comparación del 09-oct;
  aún no hay selector en pantalla.

- **Históricos — nueva pestaña «Inventarios Físicos» (2026-10-09; primera pestaña de la ampliación P2).** Tres vistas con filtros
  Desde/Hasta + Centro › Supervisor › Vendedor (dependientes) + División + Suplidor + Categoría + Producto + Cliente:
  (1) **Representantes** (Supervisor › Vendedor): inventarios generados, **clientes DISTINTOS** con inventario, clientes de su
  cartera (RutaVisitas) y % con inventario, productos y unidades; opción de incluir vendedores activos sin inventarios;
  (2) **Suplidor › Categoría › Producto** de los inventarios del rango (inventarios, clientes distintos, productos, unidades);
  (3) **Clientes** (Supervisor › Vendedor › Cliente) y, al hacer clic en un cliente, el **detalle de Suplidor › Categoría ›
  Producto** guardado en cada uno de sus inventarios (se elige el inventario por fecha). Orden por doble clic en las columnas,
  expandir/contraer, Excel y PDF de lo que se ve. Backend `GET /apihist/inventarios` (`ApiHistoricosInventariosController.cs`,
  caché 3 min); fuentes `InventarioFisico` + `InventarioFisicoDetalle` (+ Históricos, hoy vacíos) → Productos/Categoria3/Lineas.
  Verificado contra SQL (01-sep→09-oct-2026: 197 inventarios, 179 clientes distintos, 256 productos, 14,458 unidades).

- **PDF e impresión: margen lateral de 1/2 pulgada (12.7 mm) a izquierda y derecha (2026-10-09).** Pedido del usuario: al
  imprimir los PDF a página completa se perdían datos en los bordes (el contenido iba pegado al borde de la hoja y la
  impresora recortaba). Ahora el contenido y el encabezado se dibujan con 12.7 mm de margen a cada lado (el contenido se
  reduce un ~12 % para caber y se vuelve a paginar solo) en: **todos los informes de PDF Presentaciones** (Cierre Diario,
  Ventas Proyectadas, Resumen Cierre del Día, Reunión Gerencial; `ppExportarPDF`), **Históricos** (`histExportarPDF`, A4
  horizontal) y **Presupuestos** (PDF de la pantalla). Además, en las pantallas con «Imprimir» (Seguimientos/AMCO-GT,
  Supervisión, Torre de Control, Geolocalizaciones, Históricos) se fijó `@page` con ≥ 12.7 mm laterales (Home y Productividad
  ya tenían 15 mm). Verificado en navegador: imágenes en x = 12.7 mm con ancho 184.6 mm (vertical) y 271.6 mm (horizontal).

*(vacío — todo lo acumulado salió en v1.15.0)*

## v1.15.0 — 2026-10-09 · Fotos de representantes en el mapa, Mes a Mes sin auto-generar y con fechas, etiquetas AMCO-GT «(Primer/Último Pedido)»

- **Geolocalizaciones — foto del representante en el mapa (2026-10-08).** En «Ubicación de Vendedores» (y «Ubicación de la
  Ruta»), cada representante que tiene foto en `RegistrosImagenes` (RepTabla='Representantes', RepTablaKey=RepCodigo) se muestra
  como un círculo con su foto; el **aro** conserva el color de estado de antes (sincronización / cercanía a ruta) y el
  popup incluye su foto. Los que no tienen foto siguen como punto de color. Nuevos `GET /apigeo/fotos-reps` (quiénes tienen
  foto + versión, sin descargar imágenes) y `GET /apigeo/foto-rep?cod=&s=` (miniatura PNG cuadrada, recorte anclado arriba,
  cacheada en memoria y 1 día en el navegador; ~15 KB en vez de ~350 KB por foto). Nuevo paquete `System.Drawing.Common`
  (Windows). Si se cambia una foto, se refresca sola (la versión sale de `TraFechaActualizacion`).

- **AMCO-GT — etiquetas aclaradas: «Inicio temprano (Primer Pedido)» y «Cierre no prematuro (Último Pedido)» (2026-10-08).**
  Pedido del usuario, para que siempre se entienda que se miden PEDIDOS y no visitas. Actualizado en Seguimientos (Torre de
  Control IEGT/AMCO-GT: tabla «Puntos del AMCO-GT», configuración de pesos, leyenda, conclusiones/hallazgos de «arranque
  tardío»/«cierre prematuro» ahora dicen «inicio tardío (primer pedido …)» y «cierre prematuro (último pedido …)»), en el
  informe «Resumen Cierre del Día» (Supervisión) y en el flyer de vendedores. Solo cambian textos, no cálculos.

- **Históricos → Ventas «Mes a Mes»: ya no se genera solo y respeta las fechas (2026-10-06).** (1) Cambiar entre «Rango» y
  «Mes a Mes», o entre «Tabla» y «Gráficos», ya NO consulta nada: solo se genera al presionar **Generar** (antes, al pasar a
  Mes a Mes se ejecutaba sola con todos los años). (2) Mes a Mes ahora respeta **Desde/Hasta**: toma los meses completos
  del mes de «Desde» al mes de «Hasta», siempre solo meses ya cerrados (el mes en curso no entra; si el rango no tiene meses
  cerrados avisa por qué). `GET /apihist/ventas-mensual` acepta `fechaIni`/`fechaFin`. Revisados los demás reportes de
  Históricos (Notas de Crédito, Pedidos, Suplidor/Categoría, Activaciones, Análisis, Vend./Cte., Ped-Fact-NC, Fill Rate,
  Ofertas y Descuentos): sus cambios de modo/vista y Tabla↔Gráficos ya solo re-pintan lo ya generado; no requirieron cambio.
  Verificado: Ene–Mar 2026 con fechas = mismo total (239,591,767) que sumando esos meses del resultado sin fechas.

## v1.14.0 — 2026-10-06 · Nuevo informe «Resumen Cierre del Día», «Reunión Gerencial» y AMCO-GT con fuera de ruta por día de la semana

- **AMCO-GT — «Fuera de ruta» ya no penaliza al cliente del mismo día de la semana (2026-10-06).** Pedido del usuario: si el
  cliente pertenece a la ruta del vendedor en ESE día de la semana (ej. un cliente de los martes visitado un martes, aunque
  no tocara esa semana del ciclo), no cuenta como fuera de ruta; sí cuenta si es de otro día calendario o no está en su
  ruta. Se mira `RutaVisitas.RutSemana1..5` (7 caracteres por semana, posición 1 = lunes … 7 = domingo) además de
  `RutaVisitasFechaHistoricos`. Aplica en `GET /apiseg/semana` (indicador «Disciplina de ruta» y el modo «ruta» de Fuera de
  PDV) y por tanto también al informe «Resumen Cierre del Día». Verificado: 06-oct, 71 → 11 fuera de ruta (Heriberto Nuñez
  5 → 0); semana 28-sep→03-oct, 1,892 → 1,024. Torre de Control y Geo siguen con su definición propia (sin cambio).

- **Nuevo informe «Resumen Cierre del Día» (2026-10-06)** — Supervisión Dinámica → PDF Presentaciones → Gobierno Comercial,
  debajo de «Ventas Proyectadas». Por defecto es el día de hoy (o un rango Desde/Hasta) con los filtros habituales (Suplidor, Categoría, Canal, Centro, Supervisor, Vendedor) y División 10/11/12 preseleccionada. Trae: (1) **Pedidos vs Presupuesto** por Centro, Canal, Supervisor
  y Suplidor (presupuesto lineal `MNTDCAT2DIA`, ajustado `MNTDCAT1DIA`, ejecutado, alcance lineal, alcance ajustado y diferencia)
  con la escala de color pedida (&lt;75 rojo · 75–85 amarillo · 85–95 verde claro · 95–105 verde oscuro · &gt;105 azul oscuro);
  (2) **Cumplimiento y Efectividad de la visita** por supervisor (`ResumenVisitasRm`: a visitar, visitados, pedidos, cumplimiento =
  visitados÷a visitar, efectividad = pedidos÷visitados) coloreado por efectividad (verde &gt;60% o ≥5% sobre el mes anterior; rojo
  bajo su promedio de 30 días; amarillo el resto); (3) **AMCO-GT por supervisor** — foto del tablero de Seguimientos para el MISMO
  período del informe (misma fórmula y configuración guardada) con variación vs los 5 días hábiles previos (o 2 períodos previos si es un rango);
  (4) **Hallazgos**: 2 positivos y 1 oportunidad por punto, medidos contra el comportamiento HABITUAL (pedidos: ritmo del mes /
  mes anterior si es día ≤5; visitas: 30 días; AMCO-GT: 5 días hábiles previos) y ponderados por incidencia (RD$, clientes, pedidos,
  vendedores), no solo por el %; (5) **Compromisos para el próximo día hábil** (sábado/domingo → lunes): primero lo más
  importante de la compañía y luego el tema de oportunidad más relevante de cada supervisor. Backend: `GET /apipdfpres/resumen-cierre-dia`
  (`GenerarResumenCierreDia`; `ConsultarPresupuestoDia` ganó `diaHasta` opcional). Frontend: bloque `rc*` en `Supervision/Index.cshtml`
  (PDF y correo reusan `ppExportarPDF`/`abrirModalEnviarCorreo`). No incluye aún historial ni programación automática.
- **Nuevo informe «Reunión Gerencial» — Informe Ejecutivo de Cierre de Ventas mensual (2026-10-05).**
  Supervisión → PDF Presentaciones → Informes Gerenciales → **Reunión Gerencial** (antes "en desarrollo"). El usuario
  elige **Mes y Año** y se arma el cierre completo que antes se hacía con el "motor" Python de Giancarlo
  (charts.py / build_xlsx*.py / build_pdf.py): Capa 1 (KPIs vs forecast, interanual **reportado y pera con pera**,
  puente interanual, serie mensual, cumplimiento por centro/canal), un **bloque por centro** (operación, proveedores
  y categorías, ABCD del trimestre, campana de cuota, fuerza de venta y vendedores), apéndice Colombina, **Cierre
  accionable** (señal/qué pasó/qué hacer/responsable/fecha) y apéndice **Ventas a Caobo**.
  - Backend nuevo `ApiReunionGerencialController` (`GET /apipdfpres/reunion-gerencial`, `POST .../excel`). Cifras
    verificadas al centavo contra el cierre de sep-2026 del motor (ventas, comparable pera con pera, proveedores,
    ABCD, 10/26 y 8/28 sobre cuota, Caobo). Pera con pera **completo y simétrico**: la lista de marcas descontinuadas
    y nuevas se recalcula cada mes y se excluye de ambos años (+ categoría GALLETERIA); se muestra para auditoría.
  - **Textos = borrador automático editable** en pantalla (clic sobre el texto); lo editado va al PDF, al Excel y al
    historial. Botones: Descargar PDF, Descargar Excel (8 hojas con gráficas, ClosedXML), Guardar en historial,
    Actualizar datos, Regenerar textos.
  - Rendimiento: el informe completo tarda ~1 min contra Movil Business; los **meses ya terminados se guardan** en
    `App_Data/reunion_gerencial/AAAA-MM.json` y abren al instante ("Actualizar datos" fuerza una consulta nueva).
  - `ppExportarPDF` admite `jpeg:true` (opt-in, solo este informe): el PDF pasó de ~150 MB (PNG) a ~3 MB. Los demás
    informes no cambian.

## v1.13.0 — 2026-10-05 · Históricos: Ofertas y Descuentos completo + Ventas Proyectadas con capítulo «Ventas vs. Presupuesto al día y proyectado»

- **Ventas Proyectadas próxima semana — nuevo capítulo previo «Ventas vs. Presupuesto al día y proyectado» (2026-10-05).**
  Antes de las Ventas Proyectadas, el informe (Supervisión → PDF Presentaciones → Gobierno Comercial) abre con un capítulo
  que compara, para el **mes en curso**, Presupuesto del mes · Presupuesto al día · Ejecutado · Logro al día % · Dif. vs. al
  día · Proyectado al cierre · Dif. proyectado vs. mes, en 4 tablas: **Por Centro, Por Canal, Por Supervisor, Por Suplidor**
  (+ 3 tarjetas: Ejecutado, Logro al día, Proyectado cierre). Fuente: `PresupuestosHistoricos` PreTipo mensual `MNTDCAT`
  (mismo criterio que «Resultados acumulados del Mes» del Cierre Diario): días **hábiles** de
  `VWPresupuestosAlDiaUnidadesProductos`; Presupuesto al día = presupuesto × hábiles transcurridos / hábiles del mes;
  Proyectado = Ejecutado / hábiles transcurridos × hábiles del mes (ritmo actual). Respeta los mismos filtros del informe
  (Suplidor/Categoría/Canal/Centro/Supervisor/Vendedor); excluye Institucional salvo filtro explícito de canal. Backend:
  `GenerarVentasVsPresupuesto` en `ApiPdfPresController.cs` (campo `vsPresupuesto` del JSON; si falla devuelve null y el
  informe sigue igual). Frontend: `ppVsCapitulo` en `Supervision/Index.cshtml` (PDF/correo/historial lo heredan; los
  informes guardados antes de este cambio se ven sin el capítulo). El correo de la programación incluye un resumen del capítulo.
  Verificado contra SQL independiente (totales y por centro cuadran).
- **Ofertas y Descuentos — ampliación (2026-10-05): filtros en todos los reportes, drill-downs, vista Representantes y orden por columnas.**
  Pedido del usuario: (1) el selector de **Oferta(s)/Descuento(s)** y el nuevo filtro **Estado** (Activas / Finalizadas /
  Programadas) están ahora en las 4 vistas (en «Cantidad dadas» sigue siendo obligatorio elegir al menos una); (2) en la
  **Lista**, clic en una fila abre «Cantidad dadas» ya filtrada por esa promoción (también desde Productividad);
  (3) en **Cantidad dadas**, clic en un vendedor → los **días** en que dio promociones con las cantidades por día
  (nueva `vista=dias`) y clic en un día → **clientes y promociones de ese día** (`vista=clientes`, último nivel),
  con migas de pan para volver; (4) nueva vista **Representantes** (`vista=representantes`): Centro › Supervisor ›
  Vendedor y, debajo de CADA uno, todas las promociones que le aplican con veces dada y clientes impactados, ordenadas de
  la más a la menos usada (resaltadas «más usada» / «menos usada»); clic en el nombre baja de nivel (centro →
  supervisores → vendedores → detalle del vendedor), ▾/▸ contrae bloques, «Incluir las no utilizadas» muestra/oculta
  las de 0; (5) **doble clic en cualquier encabezado ordena** (otro doble clic invierte), en todos los reportes y drills,
  respetando la jerarquía (ordena hermanos). El estado «Vigente» pasó a llamarse **«Activa»**. Técnico: las líneas
  consultadas (catálogo + uso + cartera) se guardan 3 min en memoria (`IMemoryCache`, clave = filtros que cambian el
  SQL) para que drills y cambios de vista no vuelvan a la base; `ApiHistoricosController` ahora recibe `IMemoryCache`. Las
  líneas ahora incluyen fecha y cliente (mismo número de filas). Excel/PDF exportan lo que se está viendo (incl. cada
  drill). Verificado con datos reales (copia aislada, solo lectura): estado/ids en lista y productividad, días (25 = suma
  de los 3 días del vendedor), clientes del día, y Representantes (la suma de los centros = 1.296 = total de ofertas
  dadas); en pantalla: drills, miga de pan, orden asc/desc de texto y números, contraer/expandir, no utilizadas,
  Descuentos. **No probado con login/licencia reales.**
- **Históricos: se activa la pestaña "Ofertas y Descuentos" (antes un placeholder).** Pedido explícito:
  2 botones (🎁 Ofertas / 🏷 Descuentos) y, en cada uno, 3 reportes con filtros completos (Centro, Supervisor,
  Vendedor, División, Canal, Suplidor, Categoría, Grupo Producto, Producto, Grupo Clientes, Tamaño, **Cliente**,
  Estatus de pedido), rango por defecto del 1 del mes a hoy, e Imprimir/Excel/PDF:
  (1) **Lista de activas**: nombre, inicio/fin, estado, suplidor, categoría(s), canal, centro de distribución
  y vendedores a quienes aplica; (2) **Cantidad dadas**: se eligen una o varias promociones y se lista
  Supervisor › Vendedor (opcional › Promoción) con veces dada, clientes distintos impactados, monto generado
  (venta de productos que tenían la oferta; en descuentos: venta con descuento + monto descontado), unidades
  regaladas, clientes asignados (RutaVisitas) e **incidencia %** (impactados ÷ asignados), con subtotal por
  supervisor y total general (clientes distintos, sin repetir); (3) **Productividad**: ranking de más a menos
  activadas (con cobertura = vendedores que la dieron ÷ a quienes aplica) y tarjetas **Top 5 / Last 5 vendedores**
  y **Top 3 / Last 3 promociones**. Backend: `GET /apihist/promos` (`tipo`, `vista`, …) en el nuevo
  `Controllers/ApiHistoricosPromosController.cs` (`ApiHistoricosController` pasó a `partial`; hereda el gate
  de licencia del módulo "historicos"); frontend en `Views/Historicos/_OfertasDescuentos.cshtml`.
  Fuentes verificadas EN VIVO contra MovilBusiness5STD: Ofertas = `vwOfertas` (activas+históricas) +
  `OfertasRepresentantes`/`OfertasRepresentantesHistoricos` (vendedores) + líneas de `VWPedidosDetalle` con `OfeID`
  (`PedIndicadorOferta=1` = regalo; las de indicador 0 con el mismo OfeID = venta real); Descuentos =
  `DescuentosRecargos` (`RepCodigos` en JSON) + `PedidosDescuentos` (OJO: `PedDescuento` es POR UNIDAD → monto =
  PedDescuento × PedCantidad de la línea). Mismos estatus de pedido (2/7/11) y canal por defecto que la pestaña
  Pedidos. Decisiones: "vendedores que aplican" = elegibles de la promo que tienen cartera (RutaVisitas; la tabla
  de elegibles arrastra vendedores dados de baja); suplidor/categoría se derivan de los productos de la promo (o
  de su grupo); **Canal = «Todos»** casi siempre (ninguna promo trae canal configurado) y Centro = centros de los
  vendedores elegibles; "Last 5 vendedores" = vendedores a quienes aplica alguna promo y con pedidos en el rango.
  Verificado: build limpio; cifras de totales y de un vendedor cuadradas contra SQL independiente (1.296 ofertas
  dadas / RD$1.298.318; vendedor 7: 30 dadas, 29 clientes, RD$14.643, 55 unidades, 547 asignados; descuentos
  RD$105.857 descontados); filtros (cliente, supervisor, suplidor, centro) y Excel de las 6 combinaciones
  probados con datos reales en una copia aislada; PDF genera. **No probado con login/licencia reales** (se probó en
  una copia aislada de solo lectura con la sesión desactivada). Descuentos en rangos largos pueden tardar
  (~9 s por 5 semanas).

_(lo demás salió en v1.12.1)_

---

## v1.12.1 — 2026-10-04 · Licencia opcional atada a servidor (huella) y `-Licencia` en los creadores de instalable

> Sobre v1.12.0 (que ya exigía licencia). Una licencia SIN huella funciona igual que antes.
> Solo las licencias emitidas con huella del servidor se bloquean al copiarse a otra máquina.

- **Licencia opcional ATADA A UN SERVIDOR (huella del servidor).** Pedido del usuario: que si un
  cliente copia la carpeta del programa a otra máquina no pueda seguir usándolo, pero solo
  para los clientes a los que se les ponga huella; los demás siguen pudiendo correr en
  cualquier servidor. El sistema SIEMPRE se instala y arranca. `LicenseService.cs` calcula la
  huella del equipo (SHA-256 del `MachineGuid` de Windows, formato `XXXX-XXXX-XXXX-XXXX-XXXX`),
  la guarda en `App_Data\huella_servidor.txt` al iniciar, la muestra en la pantalla "Sistema no
  disponible" y en `/api/licencia` (`huellaServidor`, `atadaAServidor`, `estado`), y los
  instaladores (`INSTALAR_IIS`/`ACTUALIZAR_IIS`) la imprimen al terminar. Si la licencia
  trae el campo `huella` (dentro del payload FIRMADO, no se puede editar) y no coincide con
  este equipo → estado `OtroServidor`: sistema bloqueado con el mensaje "Esta licencia fue
  emitida para otro servidor… Contacte al administrador del sistema". Sin `huella` → funciona
  en cualquier servidor, igual que antes (y un sistema viejo ignora el campo). Límites: una VM
  clonada comparte el MachineGuid y un administrador experto podría falsearlo — sube el
  esfuerzo, no es infalible. `DashSmart.csproj` ahora excluye TODA `App_Data` del publish.
  Generadores: `LicenseTool` (`--huella`) y el programa web (ahora **ALL DIGITAL LICENSE**,
  campo "Huella del servidor", la guarda en la ficha del cliente y en el Excel). Verificado
  con el `LicenseService.cs` REAL (llave pública de prueba): 16 casos (sin licencia, sin
  huella, con huella correcta incl. minúsculas/sin guiones, otra huella, vencida, huella
  editada a mano → firma inválida, página con el ID) + 3 licencias emitidas por el generador;
  y 13 pruebas del generador (normaliza/valida huella, renovar sin huella la desata, Excel).
- **Creadores de instalable: parámetro `-Licencia` para armar el paquete de un cliente con su
  license.lic ya emitida.** `CREAR_INSTALABLE_STANDALONE.ps1 -Licencia "ruta\license.lic"`
  (igual en `CREAR_INSTALABLE_IIS.ps1`): valida que sea un .lic bien formado (avisa si ya
  está vencida), lo coloca junto a INSTALAR_IIS/ACTUALIZAR_IIS (que lo toman solos) y agrega
  el nombre del cliente al ZIP. Sin el parámetro el paquete sigue saliendo genérico, sin
  licencia. Probado: rutas inexistentes y archivos basura se rechazan antes de publicar, y un
  paquete real quedó con el .lic en la raíz y ninguno dentro de `app\`.

---

## v1.12.0 — 2026-10-04 · Sistema de LICENCIA (vencimiento + módulos), Históricos con Gráficos / Mes a Mes / drill-down, y mejoras de Presupuestos y Cierre Diario

> **IMPORTANTE al actualizar:** esta versión exige una licencia (`App_Data\license.lic`).
> Sin ella el sistema muestra "Sistema no disponible". `ACTUALIZAR_IIS.ps1` la toma de un
> `license.lic` puesto junto al script (o pide la ruta) y, en actualizaciones futuras, conserva la del servidor.

- **Instaladores con manejo de licencia (cada cliente tiene la suya).** Pedido del usuario:
  que el instalable no arrastre la licencia de desarrollo y que el instalador conserve la
  del servidor o la pida. (1) `DashSmart.csproj` excluye `App_Data\license.lic` del
  publish (en desarrollo sigue funcionando desde disco) y los dos `CREAR_INSTALABLE_*.ps1`
  verifican/eliminan cualquier licencia del paquete. (2) `INSTALAR_IIS.ps1`: si el
  servidor ya tiene licencia la conserva; si no, usa un `license.lic` puesto junto al
  script o pide la ruta (valida que sea un .lic bien formado; Enter = omitir con aviso).
  (3) `ACTUALIZAR_IIS.ps1`: antes borraba todo `App_Data`; ahora respalda y restaura la
  licencia del servidor (o la pide si no tenía). (4) Ambos muestran al final el estado vía
  `/api/licencia` (válida/vence/días/módulos). `ACTUALIZAR_DELTA.ps1` no se tocó (nunca
  borra nada). LEEME.txt de cada paquete documenta el procedimiento. Verificado: sintaxis
  PowerShell de los 4 scripts (parser, 0 errores), publish real confirma que la licencia
  NO sale en el paquete, y la validación del .lic acepta el real y rechaza basura. **No se
  ejecutó un INSTALAR/ACTUALIZAR real contra IIS** (sin servidor en esta sandbox).

- **Licencia: los avisos al usuario ahora dicen "Contacte al administrador del sistema"**
  (antes decían "AM Comercial") — pedido explícito: para cada cliente, AM Comercial es
  una empresa más. Aplica a la pantalla de licencia vencida/ausente y a la de módulo no
  incluido (`Services/LicenseService.cs`).
- **Sistema de licencia: fecha de vencimiento + los 9 módulos activables/desactivables,
  modificable SOLO por AM Comercial.** Pedido explícito: el producto se instala en
  varios servidores de distintos clientes y se necesita poder renovar la licencia por
  rango de fechas, y activar/desactivar cada uno de los 9 módulos por cliente, de forma
  que nadie con acceso al servidor del cliente pueda extender su propia licencia ni
  reactivar un módulo apagado. Se descartó una variable de entorno o un valor en
  `appsettings.json` (ambos texto plano, editables por cualquier administrador del
  servidor) a favor de un archivo **firmado digitalmente** (RSA-2048 + SHA256):
  `App_Data/license.lic` contiene la fecha de expiración y los 9 flags de módulo + una
  firma que solo se puede generar con una llave PRIVADA que nunca sale de la máquina de
  AM Comercial — el producto solo trae la llave PÚBLICA (verifica, no firma). Si el
  archivo falta, está corrupto, o fue editado a mano (firma no coincide), se trata como
  licencia inválida — "fail closed". Nueva herramienta separada `LicenseTool/` (carpeta
  hermana de la solución, NUNCA se despliega con el producto) para generar el par de
  llaves una vez y emitir/renovar licencias (`dotnet run -- emitir --clave ... --cliente
  "..." --expira yyyy-MM-dd [--modulos ...]`). Enforcement en 3 capas: (1) Menú oculta la
  tarjeta de un módulo apagado (`/api/licencia`, infraestructura compartida sin gate);
  (2) cada controller de los 9 módulos valida su propio módulo vía
  `[RequiereModulo("...")]` (nuevo filtro de acción) — bloquea con 403 aunque se entre
  por la URL directa; (3) un middleware global en `Program.cs` corta TODAS las rutas
  (excepto `/version`) con un aviso si la licencia venció o no es válida, sin importar
  si hay sesión iniciada. Se detectaron y resolvieron 2 casos de endpoints de
  infraestructura compartida que viven dentro de un controller de módulo (`/apisuperv/
  branding`, que el Menú llama para TODAS las tarjetas; `/apipresupuestos/filtros`, que
  usan también Supervisión Dinámica y Presupuestos-Gestión) — nuevo atributo
  `[SinGateDeLicencia]` los exime del gate de su propio módulo. **Queda documentado un
  acoplamiento sin resolver**: la sub-vista "Virtual" de Supervisión Dinámica llama a
  `/apiseg/semana` (módulo Seguimientos Semanales) — si algún cliente tiene Supervisión
  activa pero Seguimientos apagado, esa sub-vista puntual no cargará (el resto del
  módulo sigue funcionando). Verificado en vivo con una instancia descartable
  compilada aparte (puerto 5098, nunca la instancia real del usuario en 5012): se probó
  (a) licencia válida con "geo" apagado → `/geo` responde 403 con la página de aviso,
  `/torre` (módulo activo) deja pasar hasta el controller; (b) licencia vencida →
  CUALQUIER ruta (incluida una de un módulo activo) muestra "Sistema no disponible" con
  la fecha de vencimiento exacta, `/version` sigue respondiendo; (c) archivo alterado a
  mano (1 carácter cambiado) → firma inválida, tratado igual que "sin licencia". De paso
  se encontró y corrigió un bug real durante esta verificación: la deserialización JSON
  del sobre `{payload,signature}` no tenía `PropertyNameCaseInsensitive`, por lo que
  SIEMPRE fallaba con "formato inválido" pese a una licencia bien firmada — el harness en
  vivo lo detectó de inmediato. `dotnet build -c Release`: limpio. **Pendiente (acción
  del usuario, no de código):** la llave privada usada durante esta verificación se
  generó en este mismo entorno — por diseño, NO debe usarse en producción (si yo la vi,
  deja de cumplir "solo tú la puedes firmar"). Antes de emitir la primera licencia real,
  generar un par NUEVO con `LicenseTool` en su propia máquina, reemplazar la constante
  `PublicKeyXml` en `Services/LicenseService.cs` con la nueva llave pública, y guardar la
  llave privada fuera de este repo/OneDrive.
- **Históricos: gráficos con tamaño correcto, título en cada uno, y % en los Pasteles.**
  Pedido explícito, con screenshot de Pedidos mostrando el gráfico de Línea ocupando
  varias pantallas de alto: "el reporte lineal en Pedidos, está fuera de la página, es
  demasiado grande, ajústalo a un tamaño adecuado, además ponle un título a cada gráfico,
  y en los gráficos de pastel poner el valor %." Causa real del tamaño: los `<canvas>` de
  Chart.js vivían en contenedores `flex` SIN altura fija — con `responsive:true` y sin
  `maintainAspectRatio:false`, Chart.js calcula el alto a partir del ancho (aspect ratio
  2:1 por defecto); el gráfico de Línea, en un contenedor a todo el ancho de la página
  (~1900px), salía con ~950px de alto o más. Se agregaron 2 clases CSS nuevas
  (`.graf-box`/`.graf-box-linea`, alto fijo 260px/300px + `position:relative`, requisito
  de Chart.js para que un canvas responsive se ajuste al contenedor en vez de al revés) y
  se aplicaron a los 32 contenedores de canvas (12 reportes × Pastel+Barra, 7 de ellos +
  Línea). Las 3 funciones compartidas (`graficoPastel`/`graficoBarra`/`graficoLinea`,
  usadas por los 12 grupos de gráficos) ahora reciben un parámetro `titulo` — se
  actualizaron los 32 sitios donde se llaman, cada uno con un título descriptivo en
  español (p.ej. "Peso de Ventas por Suplidor", "Tendencia Diaria de Pedidos"). Para el %
  en los Pasteles: sin plugin de Chart.js vendorizado para etiquetas (`chartjs-
  plugin-datalabels` no está en `wwwroot/lib`, y no se agregó — el proyecto evita CDN/
  librerías nuevas), se calculó el % client-side y se inyectó vía
  `legend.labels.generateLabels` (el % queda SIEMPRE visible en la leyenda, no solo al
  pasar el mouse) + `tooltip.callbacks.label` (valor y % juntos al hacer hover). **No se
  pudo probar el render real en un navegador** (sin acceso en esta sandbox, ni a la BD
  para generar datos reales) — el usuario debe confirmar visualmente que el gráfico de
  Línea ahora cabe en pantalla en los 12 reportes y que los Pasteles muestran el %.
  Verificado con `dotnet build -c Release` (limpio), `node --check` (sin errores), y un
  harness de Node contra las 3 funciones reales (`graficoPastel`/`graficoBarra`/
  `graficoLinea`) con un `Chart` de Chart.js simulado — 15/15 aserciones OK: `type`
  correcto, `maintainAspectRatio:false` presente, título correcto en cada tipo, leyenda y
  tooltip del Pastel calculan el % exacto (500/300/200 → 50.0%/30.0%/20.0%).
- **Pedidos (Históricos): nueva opción "Suplidor → Categoría → Producto" en "Analizar por",
  con drill-down directo Producto → Cliente.** Pedido explícito: "dentro de Analizar por,
  incluye uno que diga suplidor - categoria - Producto y que genere el mismo proceso de
  Drill down y sub totalice por categoria, suplidor y total general por compañia. luego
  del producto si hago un drill down entonces me lleva a los clientes." Se dejó como
  opción NUEVA y separada de la ya existente "Suplidor → Categoría" (esa se había
  construido deliberadamente sin Producto en una sesión anterior; no se tocó su
  comportamiento). Backend: nueva rama `modo="suplidorprod"` en `Pedidos()`
  (`ApiHistoricosController.cs`), mismo patrón manual de "GROUPING SETS por línea" que el
  resto del archivo, un nivel más profundo (Suplidor → Categoría → Producto), con
  subtotales por Categoría, por Suplidor y un TOTAL GENERAL por compañía. Frontend: nueva
  `pdRenderSuplidorProd()`, filas de Suplidor/Categoría abren el drill por día (mismo
  mecanismo `pdAbrirDrillDia` ya usado en el resto de "Analizar por"), filas de Producto
  (el nivel más profundo) abren directo el desglose por Cliente sin pasar por día
  (`pdAbrirDrillClienteDirecto`, nuevo — usa el rango de fechas completo elegido en
  pantalla, no un solo día). También se corrigió `excelPD()`, que no tenía rama para este
  modo nuevo y hubiera exportado columnas vacías/erróneas (Centro/Canal/Supervisor no
  existen en estas filas) — ahora exporta Nivel/Suplidor/Categoría/Producto/Cantidad/
  Montos. Verificado: `dotnet build -c Release` limpio, `node --check` sin errores, harness
  de Node contra las funciones reales (`pdRenderSuplidorProd`, `pdOnclickDia`,
  `pdOnclickClienteDirecto`) con datos sintéticos de 2 suplidores × 2 categorías × varios
  productos — 14/14 aserciones OK (orden por monto, acumulación correcta de filtros
  `suplidores=X&categorias=Y&productos=Z` por fila, subtotales, TOTAL GENERAL). **No se
  pudo probar en un navegador real ni contra la base de datos en vivo** (sin acceso en
  esta sandbox) — pendiente de confirmación visual del usuario.
- **Históricos: rediseño de los controles "Rango/Mes a Mes" y "Tabla/Gráficos" — reubicados
  arriba a la derecha, encima de los filtros, con un control segmentado real.** Pedido
  explícito: "los botones están muy feos, tratemos de colocarlos en otro lugar y que se
  vean más estéticos... quizás a la derecha encima de los filtros." Causa real del
  problema (no solo estética): los botones usaban una clase `.seg` que **no existía** en
  este archivo (se copió de memoria del patrón de `Views/Presupuestos/Index.cshtml`, un
  archivo DISTINTO con su propio CSS) — sin ningún estilo definido, el navegador los
  pintaba con su apariencia por defecto (bordes planos, sin agrupar), de ahí lo feo.
  Se agregó una clase `.seg` propia y real a `Views/Historicos/Index.cshtml` (pastilla
  única con fondo `--panel2`, botones sin borde propio que se diferencian por el fondo, la
  opción activa resaltada en `--accent` con una sombra sutil — mismo lenguaje visual que
  `.btn-modo`, ya usado en el resto del archivo, pero en formato compacto de "pastilla") y
  una clase `.hist-toolbar` (fila alineada a la derecha) que ahora aloja los 12 controles
  segmentados (9 reportes + 3 sub-vistas de Análisis), movidos de donde vivían a mitad de
  pantalla (después de las tarjetas/print-head) a una fila nueva justo ARRIBA de
  `.filtros`, junto al título del reporte cuando lo tiene (Pedidos, Notas de Crédito) o
  junto al toggle de modo ya existente cuando lo tiene (Vend./Cte., Ped-Fact-NC, Fill
  Rate, Comparativo Ctes. — combinados en la misma fila en vez de vivir en 2 filas
  separadas). Verificado que los 12 controles quedan con exactamente 1 ocurrencia cada
  uno (sin duplicados tras mover el HTML), `node --check` sin errores, `dotnet build -c
  Release` limpio. **No se pudo ver el resultado visual real en un navegador** (sin acceso
  en esta sandbox) — el usuario debe confirmar que ahora se ven como controles
  segmentados reales, no botones sueltos.
- **Ventas (Sup./Vend.): nuevo "Histórico Mes a Mes" — solo meses ya cerrados, sin límite
  de años hacia atrás, con esquema elegible.** Pedido explícito: "quiero poder ver un
  histórico Mes a Mes (Meses cerrados) en el esquema que quiera... presentar los meses y
  año uno por uno en cada columna y un subtotal por año y un total general, cada mes
  compara con el anterior y muestra si fue mayor o menor." Nueva pestaña `.seg`
  "Rango"/"Mes a Mes" dentro de Ventas (Sup./Vend.) — "Rango" es el comportamiento de
  siempre, sin cambios. "Mes a Mes" es 100% nuevo: nuevo endpoint
  `GET /apihist/ventas-mensual` (agrupa por Año/Mes/Vendedor, todos los años disponibles
  en `VWVentas`, excluyendo el mes calendario en curso — "meses cerrados"), y una tabla
  pivote con banda de AÑO arriba de sus meses, 4 esquemas elegibles (Total Compañía —
  pedido como primer ejemplo del usuario—, Por Centro, Por Supervisor, Por Vendedor,
  agregados 100% client-side desde la misma respuesta para no repetir la consulta al
  cambiar el esquema), ▲/▼ mes a mes reusando `scCelda`/`flecha` ya existentes en este
  archivo, fila "Subtotal por año" y fila "TOTAL GENERAL". Verificado con un harness de
  Node contra las funciones reales con 2 años sintéticos y un hueco de 1 mes (confirma
  bandas de año correctas, suma exacta por Centro, reseteo del ▲/▼ en el hueco, y que el
  esquema "Compañía" combina todos los centros en 1 sola fila).
- **Históricos: pestaña de Gráficos (Pastel/Barra/Línea) en los 9 reportes del módulo.**
  Pedido explícito: "todos los reportes que están en históricos, tengan al momento de
  generarse una pestaña de gráficos... un Pastel para determinar el peso de las ventas y
  también dentro de lo posible... lineales, de barra y columnas." Alcance acotado con el
  usuario: Pastel+Barra en los 9 reportes (Ventas Sup./Vend., Notas de Crédito, Pedidos,
  Suplidor/Categoría, Activaciones, Vend./Cte., Ped-Fact-NC, Fill Rate, y las 4 sub-vistas
  de Análisis) — Línea solo donde el reporte YA trae una serie temporal en los datos que
  consulta hoy (Ventas Mes a Mes, Notas de Crédito, Pedidos, Suplidor/Categoría,
  Activaciones, Vend./Cte., Comparativo Ctes.) — Ped-Fact-NC, Fill Rate, Comparativos,
  Clientes Perdidos y Clientes Recuperados quedan sin Línea (su consulta actual no trae
  ninguna dimensión de fecha por transacción; agregarla es trabajo de backend nuevo,
  explícitamente diferido). Infraestructura nueva y compartida: `ensureChart()` (mismo
  lazy-loader ya usado en Presupuestos para Chart.js v4.5.1, ya vendorizado en
  `wwwroot/lib/`, cero descarga/CDN nueva), `graficoPastel`/`graficoBarra`/`graficoLinea`
  (mismos patrones de `data`/`options` ya usados en los gráficos de línea/barra de
  Presupuestos), `topNConOtros` (agrupa a los 10 más grandes + un balde "Otros" para que
  Pastel/Barra con 50+ filas no sea ilegible), y un toggle genérico `.seg` "Tabla"/
  "Gráficos" (`histToggleVista`) reusado por los 12 grupos de vista (9 reportes + 3
  sub-vistas adicionales de Análisis). Cada reporte arma sus gráficos desde los datos YA
  EN MEMORIA (la variable que ya llena su propia función de "Generar") — sin ningún
  endpoint nuevo salvo 2 excepciones documentadas en el propio código: la Línea de
  Pedidos y de Notas de Crédito reutilizan una consulta que YA existía para otro fin (el
  drill-down Fecha→Cliente de una entrega anterior de esta misma sesión), pedida con un
  fetch nuevo pero sin lógica de backend nueva. Verificado con un harness de Node contra
  `topNConOtros` (top-10+Otros, orden descendente, sin balde cuando no sobra nada) y
  contra la lógica real de agrupación cliente-side de 2 reportes representativos.
  `dotnet build -c Release` y `node --check`: limpios. **No se pudo probar el render real
  de Chart.js en un navegador (requiere un canvas real, sin acceso en esta sandbox)** — el
  usuario debe confirmar visualmente que los 12 grupos de gráficos aparecen y tienen
  sentido.
- **Renombrado "Históricos de Ventas" → "Históricos"** (pedido explícito): título de la
  pestaña del navegador, encabezado visible de la pantalla, y el nombre de la tarjeta en
  el Menú principal — los 3 lugares donde aparecía el nombre completo del módulo.
- **Pedidos: las tarjetas ("Pedidos del Día"/"Pendientes por Facturar"/"Pedidos Perdidos")
  y las mini-tablas Por Canal/Por Centro ahora se recalculan también al hacer un
  drill-down, no solo al presionar "Generar".** Pedido explícito: "la tarjeta que se
  genera de los pendientes, debe ser de los filtros que se realicen, si se hace un drill
  down tambien el filtro debe aplicar a esto." Antes, `pdCargarTarjetas()` solo se
  llamaba desde el botón "Generar" — al drillear (clic en una fila del reporte, ver
  entrega anterior) la tabla principal se acotaba a esa fila pero las tarjetas seguían
  mostrando los números del filtro general de pantalla, sin reflejar el drill. Ahora
  `pdCargarDrill()` (y `pdCerrarDrill()` al volver) también llama a
  `pdCargarTarjetas(extraQs)`, pasándole el mismo fragmento de filtro (`PD_DRILL.qs`) que
  ya usa la tabla principal — con la misma semántica de REEMPLAZO (no concatenación) que
  el resto del drill, para que un drill a "Estatus 11" no siga arrastrando el
  estatus=2,7,11 del filtro general. La ventana de fecha de cada tarjeta NO cambia (sigue
  siendo su propio diseño: "Pedidos del Día"=hoy siempre, "Pendientes"=últimos 90 días
  siempre, "Perdidos"=el rango elegido en pantalla) — solo el alcance de
  Centro/Canal/Suplidor/Categoría/Estatus se actualiza con el drill. Verificado con un
  harness de Node contra la función real: el drill reemplaza (no concatena) el filtro
  general, y al cerrar el drill se vuelve exacto al filtro de pantalla sin arrastrar el
  drill anterior. `dotnet build -c Release`: limpio.
- **Históricos de Ventas: drill-down Fecha → Cliente en Pedidos, Ventas (Sup./Vend.) y
  Notas de Crédito.** Pedido explícito del usuario (con screenshot de "Pedidos"): "al
  realizar los reportes, permite que estos tengan un Drill Down por fecha, con los
  mismos campos... si hago un drill down debe mostrar dia por dia, tambien agregar un
  segundo drill Down por cliente." Alcance acotado con el usuario (`AskUserQuestion`) a
  las 3 pestañas que ya tienen el patrón exacto "fila por dimensión + Cantidad/Montos +
  TOTAL" de la imagen — Activaciones (matriz dimensión×período sin Monto) y los 4
  sub-reportes de Análisis (comparativos de período, cobertura de clientes, o ya a nivel
  cliente) quedan fuera por ser estructuralmente distintos; Ofertas y Descuentos no
  existe (placeholder deshabilitado). Ahora, en cualquiera de las 3 pestañas: clic en
  CUALQUIER fila de la tabla de totales (hoja, subtotal "Total X", o TOTAL GENERAL) abre
  un desglose día por día con las MISMAS columnas; clic en un día abre el desglose de
  ese día por cliente, mismas columnas otra vez — 2 niveles exactos, con breadcrumb
  "‹ Volver" para deshacer.
  - **Pedidos**: se agregaron `Fecha`/`CliID`/`CliCodigo`/`CliNombre` al `SELECT` de
    `GatherPedidosLineas` (sin joins nuevos — `p.PedFecha` ya se filtraba, `cl` ya estaba
    joineado) y 2 modos nuevos (`modo=dia`/`modo=cliente`) a `/apihist/pedidos`, con la
    misma lógica de agregación ya usada por `modo=estatus`. El front acumula, al recorrer
    el árbol/tabla ya renderizado, qué filtros existentes (`estatus`/`suplidores`/
    `categorias`/`centros`/`canales`/`supervisores`/`vendedores`) identifican la fila
    exacta clickeada, y reconsulta el mismo endpoint con esos filtros + el modo nuevo.
  - **Ventas (Sup./Vend.)**: `/apihist/ventas` gana 2 parámetros opcionales
    (`vendedorDrill`+`granularidad`) — cuando vienen informados, se salta el flujo normal
    (agregado de TODOS los vendedores + Δ% vs. período previo) y corre una consulta nueva
    acotada a un solo vendedor, agrupada por día o por (día+cliente), reusando los mismos
    filtros transaccionales (`Tx`/`TxSql`) que ya usa el resto del reporte. El nivel
    "Vendedor" (antes un callejón sin salida, sin ninguna interacción) ahora abre el
    drill al hacer clic.
  - **Notas de Crédito**: CERO cambios de backend — `/apihist/notascredito` ya soportaba
    `porDia` y `nivel=cliente` desde una entrega anterior (2026-08-28), solo que como una
    vista de árbol totalmente expandida vía el selector "Analizar por", no como un drill
    por clic. Como esa consulta SIEMPRE agrupa por Vendedor además de lo pedido, si la
    fila clickeada es un Centro (que engloba varios vendedores) el front re-suma
    client-side por Fecha (o por Cliente en el 2º nivel) antes de pintar.
  - Verificado con harnesses de Node contra las funciones reales extraídas del `.cshtml`
    (no reescritas): acumulación correcta de filtros por fila en las 3 pestañas para
    varios niveles/subniveles, orden ascendente por fecha en el desglose diario, y —
    específico de Notas de Crédito — que la re-suma client-side por Fecha entre 2
    vendedores de un mismo Centro da el monto combinado correcto (400+200=600), no el de
    uno solo. `dotnet build -c Release` limpio tras cada pestaña. **No se pudo probar
    contra la BD real ni en un navegador logueado** (sin acceso en esta sesión) — el
    usuario debe confirmar visualmente en las 3 pestañas.
- **Fix: el checkbox "Recalcular asumiendo ayer" (47ª parte) rompía la consulta con
  "Error al consultar: desconocido".** Bug real, no una consulta lenta — confirmado en
  vivo corriendo la app localmente: el frontend mandaba `asumirAyer=1`, pero el binder de
  `[FromQuery] bool` de ASP.NET Core usa `bool.TryParse` internamente, que NO acepta "1"/
  "0" (`bool.TryParse("1")` devuelve `false`) — solo "true"/"false". Eso hacía fallar el
  model binding, y con `[ApiController]` eso dispara automáticamente un `400` con un JSON
  de validación (`{"errors":{"asumirAyer":["The value '1' is not valid."]}}`) que no tiene
  los campos `ok`/`error` que espera el frontend — de ahí el "desconocido", sin ningún
  mensaje útil. Corregido: el frontend ahora manda `asumirAyer=true`
  (`Views/Presupuestos/Index.cshtml`). Se aprovechó para que el manejo de errores YA NO
  muestre "desconocido" en ningún caso futuro — si la respuesta no tiene el shape
  esperado, ahora se ve el código HTTP + un fragmento del cuerpo real de la respuesta.
  Verificado en vivo corriendo una copia temporal de la app localmente (puerto aparte,
  sin tocar la instancia del usuario): `asumirAyer=1` → HTTP 400 con el error de
  validación exacto descrito arriba; `asumirAyer=true` → se comporta igual que sin el
  parámetro (confirma que el binding ya funciona). **Importante: este fix explica el
  mensaje "Error al consultar: desconocido", pero NO necesariamente el "2 minutos
  colgado en Cargando…" que el usuario reportó primero — son 2 síntomas distintos; el
  segundo (posible lentitud real de la curva de estacionalidad, o el candado global
  haciendo cola) sigue sin poder reproducirse/confirmarse en esta sesión.** También se
  corrigió el `web.config` de despliegue (`DEPLOY_DashVisitas/app/web.config`,
  `stdoutLogEnabled` de `false` a `true`) para que el próximo publish a IIS capture los
  logs de diagnóstico agregados en sesiones anteriores — no aplica a quien corre la app
  localmente vía `CORRER_LOCAL.bat` (ahí el log es la propia ventana de consola).
- **Presupuestos → "Del Mes": diagnóstico más fino de dónde se va el tiempo + contador
  visible mientras carga.** El usuario reportó que, incluso después del candado global
  para curvas de estacionalidad (46ª parte), una consulta que a su juicio es "sencilla"
  se quedó en "Cargando…" 2 minutos sin cambiar — mismo patrón que la lentitud reportada
  antes. Revisión del código del candado: no se encontró ningún bug (semáforos liberados
  correctamente, sin deadlock) — pero el candado por sí solo NO distingue "esta petición
  está haciendo cola detrás de otras 2 curvas lentas" (el candado funcionando como se
  diseñó, esperando su turno) de "la consulta en sí contra VWVentas es lenta" (un problema
  de fondo que el candado no resuelve) — ambos casos se sienten idénticos desde la
  pantalla ("Cargando…" fijo, sin ningún número). Se agregó: (1) en el backend
  (`ApiPresupuestosController.cs`), el log de cada curva calculada ahora separa cuánto
  tiempo esperó por el candado global de cuánto tardó el cálculo real contra VWVentas —
  para poder distinguir en los logs cuál de los 2 escenarios está pasando realmente; (2)
  en el frontend (`Views/Presupuestos/Index.cshtml`), "Cargando…" ahora es un contador en
  vivo (segundos transcurridos, actualizado cada segundo) en vez de un texto estático —
  para que la pantalla nunca se sienta "congelada" sin ninguna señal, y para tener un
  número real que reportar si el problema persiste. **No se pudo reproducir ni medir en
  vivo** (sin acceso a la BD/logs de producción en esta sesión) — se necesita revisar los
  logs del servidor (carpeta `.\logs`, líneas "Curva ...: esperó Xms por el candado global
  + Yms calculando contra VWVentas") la próxima vez que ocurra, para saber si hay que subir
  el límite del candado (de 2 a 3-4) o si el problema real está en la consulta contra
  VWVentas (índices, volumen de datos — a revisar con TI). `dotnet build -c Release`:
  limpio.
- **Presupuestos → "Del Mes": alerta cuando la última actualización de datos es de HOY (en
  vez de "ayer", lo normal) + opción manual "Recalcular asumiendo ayer".** El usuario notó
  que "Días restantes" mostraba 5 cuando esperaba 6, y explicó el pipeline real: el
  proceso nocturno de TI que puebla `PresupuestosHistoricos` (usualmente ~11 PM) refleja la
  facturación de un día antes — así que, bajo operación normal, la fecha de última
  actualización (`Ultima`) SIEMPRE debería quedar "ayer", nunca "hoy". Si ese proceso no
  corrió anoche y se corrió hoy en su lugar (catch-up tardío/manual), `Ultima` queda "hoy",
  e infla "Días transcurridos"/"Días restantes" en 1 día hábil de más — sin que el backend
  tenga forma de distinguir por sí solo "corrió a tiempo" de "corrió tarde hoy mismo" (ambos
  casos dejan una fecha válida). Se agregó: (1) la fecha de "Última actualización de datos"
  ahora se muestra junto a las tarjetas de "Días del mes" (antes se calculaba pero no se
  mostraba en ningún lado); (2) cuando esa fecha coincide con hoy, aparece una alerta
  explicando el patrón atípico + un checkbox "Recalcular asumiendo ayer" que reconsulta
  `/apipresupuestos/resumen` con el nuevo parámetro `asumirAyer=1` — el backend le resta 1
  día a `Ultima` en el ÚNICO punto donde se lee de la BD (`ApiPresupuestosController.cs`,
  dentro de `Resumen`), así el ajuste se propaga solo a TODO lo que depende de esa fecha
  (Días transcurridos/restantes, Presupuesto al Día, Proyectado, Estimado, por canal, Del
  Año, Detalle mensual) sin duplicar la lógica "-1 día" en varios lugares. Es una
  corrección MANUAL (el usuario confirma si aplica, el sistema no la adivina) — no cambia
  el comportamiento por defecto para nadie más. Verificado con un harness de Node contra
  las funciones reales (`ultimaActualizacionHtml`/`cardsDiasHtml`): sin alerta cuando
  Ultima=ayer, alerta+checkbox cuando Ultima=hoy, sin reventar cuando Ultima=null. Repro a
  mano de las fórmulas de `PresupuestosCore.cs` con los números reales del caso reportado
  (Septiembre 2026, Días del mes=26): con Ultima=hoy da Días transcurridos=21/restantes=5
  (lo que vio el usuario); aplicando "asumir ayer" (Ultima−1 día) da
  transcurridos=20/restantes=**6** — coincide exacto con lo que el usuario esperaba.
- **Presupuestos: se frena la saturación de la BD cuando varias curvas de estacionalidad
  (la parte cara, ~17s contra VWVentas la 1ª vez que se pide una combinación) se calculan
  en paralelo, y se agrega logging de diagnóstico.** Pedido explícito del usuario tras ver
  "Del Mes" tardar mucho con 3 meses seleccionados: "Revisa el tiempo que esta durando
  para generar el resultado de los presupuestos, es mucho, debemos eficientizar estos
  datos para que no dure tanto." Investigación: la caché por-clave (`_cacheLocks`, ya
  existente desde el fix de "Dashboard Gráficos" del 2026-08-29) evita recalcular la MISMA
  curva 2 veces, pero "Del Mes" con 2+ meses (o "Del Año", hasta 8 meses) dispara varios
  requests en paralelo que casi nunca comparten clave (cada mes/Canal/Categoría es su
  propia clave) — si todas caen en cache miss a la vez (primera consulta del día para esa
  combinación exacta), cada una ejecuta su propio escaneo pesado de `VWVentas` AL MISMO
  TIEMPO, saturando la BD compartida y haciendo que cada una tarde más de lo que tardaría
  sola — mismo patrón raíz ya diagnosticado para "Dashboard Gráficos", pero disparado por
  claves DISTINTAS en vez de la misma clave repetida. Se agregó un candado GLOBAL
  (`SemaphoreSlim`, máx. 2 cálculos de curva concurrentes en todo el proceso,
  `ApiPresupuestosController.cs`) — el resto espera su turno en vez de competir por el
  mismo I/O de SQL Server. También se agregó: (1) logging (`ILogger`, Information) del
  tiempo real de cada curva calculada y de la consulta principal de
  `PresupuestosHistoricos`, para tener números reales de producción la próxima vez; (2)
  logging (Warning) cuando `LeerCurvaDeTabla`/`GuardarCurvaEnTabla` (persistencia en
  `PresupuestosCurvas_RM`, pensada para calcularse UNA sola vez y nunca más) fallan
  silenciosamente — antes ese fallo era invisible; si la persistencia lleva tiempo sin
  funcionar en producción (tabla sin crear, o sin permiso de escritura), cada curva se
  recalcularía contra `VWVentas` en cada request, para siempre, sin que se viera en
  ningún lado. Verificado: `dotnet build -c Release` limpio; repro aislado con
  `SemaphoreSlim(2,2)` + 6 tareas simultáneas confirmando que el candado global limita
  correctamente a 2 cálculos concurrentes. **No se pudo medir el tiempo real contra la BD
  en producción** (sin acceso a la BD en esta sesión) — el logging nuevo es precisamente
  para poder confirmarlo la próxima vez que se pruebe en vivo.
- **Presupuestos → "Del Mes": con 2+ meses seleccionados, ahora se muestra el ACUMULADO
  (suma) de los meses por defecto, con un toggle "Detalle por mes" para ver el
  desglose (drill-down) mes a mes.** Pedido explícito del usuario: "debe presentar el
  resultado de la suma de los meses seleccionados, y si se pide ver el detalle entonces
  hacer un Drill Down por Mes, pero todos los datos deben ser acumulados de los meses
  seleccionados." Antes, seleccionar varios meses repetía el reporte completo (tarjetas de
  días + tabla de KPI) una vez por cada mes, sin ningún total combinado. Ahora: los montos
  (Presupuesto, Presupuesto al Día, Ejecutado, Proyectado, Estimado, Días Transcurridos/del
  Mes/Restantes) se SUMAN directo entre los meses elegidos; los porcentajes (Alcance%,
  Proyectado%, Estimado%) y la Necesidad Diaria NO se promedian (un promedio simple de %
  mensuales desvirtúa el peso real de cada mes) — se RECALCULAN desde los montos ya
  sumados, misma fórmula que ya usa el backend para 1 solo mes. El toggle "Acumulado /
  Detalle por mes" (mismo patrón visual `.seg` ya usado en el resto de esta pantalla) se
  re-renderiza desde los datos ya consultados (sin volver a pedirle nada al backend); con 1
  solo mes seleccionado no cambia nada (sigue igual que antes). Implementado 100% en el
  frontend (`Views/Presupuestos/Index.cshtml`: `sumarKpi`/`sumarEntornosMes`/
  `renderResumenMes`) — sin cambios de API. Verificado con un harness de Node contra las
  funciones reales: suma directa de montos, Alcance% recalculado (no promedio ingenuo —
  se comprobó explícitamente que difiere de un promedio simple), Necesidad Diaria
  recalculada, y manejo de un mes sin dato (`VentasProyectadas=null`) sin reventar.
- **Supervisión Dinámica → Cierres → "Cierre Diario": el Presupuesto/Logro del día
  PRINCIPAL ahora se calcula sobre la Meta Lineal (MNTDCAT2DIA), no sobre la Meta
  Ajustada acumulada (MNTDCAT1DIA), en TODOS los niveles.** Pedido explícito del usuario:
  "los presupuestos, tómalos de la meta lineal, no el acumulado... el presupuesto y el
  logro hazlo en base al presupuesto lineal del día. para todos los niveles del informe" —
  mismas etiquetas "Meta ajustada"/"Meta lineal" que ya usa el módulo Presupuestos
  (`/apipresupuestos/deldia`). Antes de este cambio, la sección "2. Ventas Vs.
  Presupuestos del día" del Cierre Diario usaba MNTDCAT1DIA ("Meta ajustada por déficit",
  que absorbe el faltante de días previos) como Presupuesto/Logro/Diferencia principal, y
  mostraba MNTDCAT2DIA ("Meta lineal") como 3 columnas secundarias "Presupuesto Línea/
  Logro Lineal %/Diferencia Lineal". Se invirtió: ahora MNTDCAT2DIA es el
  Presupuesto/Ejecutado/Logro del día %/Diferencia principal (Centro, Canal,
  Supervisor→Vendedor, Suplidor→Categoría), y MNTDCAT1DIA quedó como columna de
  referencia secundaria, renombrada de "Presupuesto Línea/Logro Lineal %/Diferencia
  Lineal" a **"Meta Ajustada/Logro Ajustado %/Diferencia Ajustada"** (para no dejar un
  nombre de campo "Lineal" apuntando a un dato que ya no es el lineal) — mismo cambio
  aplicado en la imagen de WhatsApp (`ReportesProgramadosSchedulerService.cs`). El bloque
  "1. Ventas Vs. Presupuestos del mes" (mensual, `MNTDCAT`) no se tocó — MNTDCAT1DIA/
  MNTDCAT2DIA son exclusivamente diarios. Verificado con un harness de Node contra la
  función real `cdTablaAlcance`: con Centro Azua (Presupuesto lineal 2,043,097, Ejecutado
  2,458,000) el Logro del día % principal da 120.3% y la columna Meta Ajustada da
  3,609,595/68.1% — coincide exacto con "Meta lineal"/"Meta ajustada"/"Alcance lineal%"
  del módulo Presupuestos para ese mismo Centro (screenshot del usuario).
- **Históricos de Ventas: nueva pestaña "Suplidor / Categoría" — pivote mensual de
  Cajas/Ctes./Monto.** Hasta ahora era un botón deshabilitado ("Definiremos su estructura
  contigo"), sin filtros ni datos detrás. Se activó con: mismos filtros que "Activaciones"
  (Centro/Supervisor/Vendedor/División/Suplidor/Categoría/Producto/Grupo Producto/Canal/
  Grupo Clientes/Fuente/Esquema) + **Cliente** nuevo; filtro "Reporte" con 3 opciones que
  deciden la fila — Suplidor, Categoría, o Suplidor/Categoría (con clic para "drillear" de
  Suplidor a sus Categorías, mismo patrón ya usado en Vend./Cte./Ped-Fact-NC/Fill Rate).
  Tabla: 1ª columna código+nombre, un grupo de 3 columnas (Cajas/Ctes./Monto) por cada mes
  del rango, con flecha ▲/▼ comparando cada métrica contra el mes anterior de la misma
  fila (igual que "Activaciones"), columna Total al final y fila TOTAL GENERAL — el Total
  de Clientes NUNCA es la suma de los meses (mismo motivo que Activaciones: un cliente que
  compró en 2 meses se contaría 2 veces si se sumara; sale de `COUNT(DISTINCT CliID)`
  sobre el rango completo, vía `GROUPING SETS`). Nuevo endpoint
  `GET /apihist/supcat-mensual` — extiende el mismo `GROUPING SETS` que ya usa
  `VendCteSupCat` (Suplidor/Categoría con Cajas+Clientes+Monto exactos por nivel),
  agregando el período a cada tupla.
- **Supervisión Dinámica → Cierres → "Cierre Diario": Presupuesto Línea (MNTDCAT2DIA),
  nuevo punto "1. Resultados acumulados del Mes" (con Presupuesto al Día/Logro al Día %/
  Logro Acumulado %), y orden por Logro en todos los niveles.** Pedido explícito del
  usuario: (1) en Ventas del día se agregan 3 columnas nuevas — Presupuesto Línea, Logro
  Lineal % y Diferencia Lineal — leídas del `PreTipo` `MNTDCAT2DIA` (la variante
  lineal/diaria del presupuesto de Categoría·Dinero, ya mapeada en `PresupuestosCore.Mapa`
  pero sin usar hasta ahora); solo aplica a Ventas (Activaciones no tiene variante lineal
  en el mapa). (2) Se inserta un nuevo bloque "1. Resultados acumulados del Mes" (mismas 4
  tablas — Centro/Canal/Supervisor·Vendedor/Suplidor·Categoría — que ya tenía el punto
  diario, pero con los `PreTipo` MENSUALES `MNTDCAT`/`UNDDACTCAT3`/`UNDDLINACT`) con
  visibilidad de días transcurridos/del mes/restantes; lo que antes era "1."/"1.1"/"2."
  pasa a "2."/"2.1"/"3.". Ese bloque del mes distingue **"Presupuesto al Día"** (el
  presupuesto del mes prorrateado a ritmo hasta hoy, `presupuesto×díasTranscurridos/
  díasDelMes`) de **"Logro al Día %"** (Ejecutado acumulado ÷ ese prorrateo — "¿vamos al
  ritmo correcto?") y **"Logro Acumulado %"** (Ejecutado acumulado ÷ Presupuesto TOTAL del
  mes, sin prorratear — "¿cuánto llevamos de la meta completa?") — pedido explícito
  2026-09-20, tras una 1ª versión que solo mostraba un "Logro del día %" (vs el
  presupuesto del día) también en el bloque mensual, ambigüedad que el usuario corrigió.
  (3) Todas las tablas (incl. niveles anidados Vendedor/Categoría) ahora ordenan por Logro
  descendente en vez de por Ejecutado o Presupuesto — el bloque del mes ordena por "Logro
  al Día %", el bloque del día (sin cambios de fórmula ahí) sigue ordenando por su propio
  "Logro del día %" (Ejecutado del día vs Presupuesto del día). Se detectó en vivo que el
  `PreTipo` mensual `UNDDACTCAT3` no trae `PreEjecutado` poblado por el proceso externo
  (aunque el mismo mes sumado desde el diario `UNDDACTCAT3DIA` sí lo trae) — se agrega un
  aviso visible en el reporte en vez de forzar una reconciliación falsa, mismo criterio
  que ya usa este módulo para otras discrepancias de granularidad entre `PreTipo`.
- **Envío por WhatsApp — checklist de qué incluir, mensaje personalizado, filtro visible
  en la imagen, cierre de firma, y confirmación de envío.** Tras el primer envío exitoso,
  5 mejoras pedidas explícitamente: (1) checklist en el modal "Enviar por WhatsApp" (Ventas
  del día / Activaciones del día / Acumulado del mes — Ventas / Acumulado del mes —
  Activaciones / Resumen ejecutivo / Pensamiento del día) que decide qué bloques entran en
  la imagen — `ReportesProgramadosSchedulerService.WhatsAppPuntos`, nuevo parámetro de
  `ArmarHtmlWhatsAppCierreDiario`. (2) Mensaje de introducción editable (textarea, default
  "Buenas, a continuación les compartimos el resultado del día:") que se manda como un
  mensaje de TEXTO aparte, ANTES de la imagen (`textoIntro` en el sidecar — 2 mensajes en
  secuencia). (3) La imagen ahora muestra los filtros activos del reporte (p.ej.
  "Colombina") justo debajo de la fecha — `FiltroResumenWhatsApp`, omite las dimensiones
  sin filtrar en vez de decir "Todos" para no ensuciar una imagen que debe salir corta. (4)
  Cierre de firma fijo ("Feliz resto del día, Atentamente, Gerencia Comercial, AM
  Comercial") como caption de la imagen. (5) Confirmación "✅ Mensaje enviado…" al terminar.
  El envío programado (scheduler) también hereda el mensaje/cierre por defecto — solo el
  checklist y el mensaje personalizado son configurables desde el envío ad-hoc por ahora.
  Fix inmediato tras la primera prueba real: cada bloque traía MENOS campos que ese mismo
  punto en el reporte completo (p.ej. "Ventas del día" sin Diferencia/Presupuesto Línea/
  Logro Lineal %; "Acumulado del mes" sin Presupuesto al Día/Logro al Día %/Diferencia, y
  el "Logro" mostrado en realidad era el Acumulado sin decirlo) — reescrito para que cada
  bloque tenga EXACTAMENTE los mismos campos que ese punto en pantalla (4+3 para
  Ventas/Activaciones del día, 6 para los bloques del mes). Bug real corregido de paso:
  `var t = SumarSeccion(sec)` con `sec` dynamic hacía que TODA la llamada quedara tipada
  `dynamic` en tiempo de compilación (regla del lenguaje: método REAL de una clase + arg.
  dynamic), tumbando el envío con `RuntimeBinderException: 'double' does not contain a
  definition for 'HasValue'` — fix: tipo explícito (`TotalesSeccion t = ...`) en vez de
  `var`, confirmado con reproducción aislada antes y después del fix.
- **"1. Resultados acumulados del Mes": días HÁBILES reales (no de calendario).** El
  encabezado decía "Día 19 de 30 del mes" (días de calendario) — pedido explícito: "eso no
  me sirve, quítalo", debe ser días HÁBILES (lunes a sábado) tomados de la misma fuente que
  ya usa el negocio para esto, `VWPresupuestosAlDiaUnidadesProductos` (columnas
  `diasdelmes`/`diasTranscurridos`, verificado en vivo: uniformes para todo un
  `PreAnio`/`PreMes`, ej. sep-2026 = 26/15 en 8,855 filas). Nuevo
  `ObtenerDiasHabilesMes()` en `ApiPdfPresController.cs` — con fallback a
  `PresupuestosCore.DiasHabiles()` (el cálculo que ya usa el resto del sistema) si el mes
  todavía no tiene filas en la vista. El mismo valor ahora también alimenta el prorrateo
  de "Presupuesto al Día" (antes por días de calendario, ahora por días hábiles — más
  coherente con un negocio que no opera domingos). Encabezado renombrado a "Día hábil X de
  Y · faltan Z días hábiles" en el reporte y en la imagen de WhatsApp.
- **La imagen de WhatsApp solo mandaba el TOTAL agregado de cada punto — le faltaba el
  desglose por Centro/Canal/Supervisor/Suplidor que sí tiene el reporte completo.** El
  usuario confirmó que las columnas ya calzaban (fix anterior), pero "el mensaje solo
  envía una parte del reporte... lo necesito completo (de lo que se selecciona)". Se
  agregó `TablaNivel()` — una tabla compacta por nivel, con las mismas columnas que ese
  punto tiene en pantalla — llamada 4 veces (Centro/Canal/Supervisor/Suplidor) dentro de
  cada bloque seleccionado del checklist, debajo del total que ya se mostraba. Imagen
  ensanchada de 380px a 520px para que quepan las tablas cómodas (viewport del sidecar
  ajustado a juego).
- **Fix: los botones "solo Gerencia" (Destinatarios/Programados/📱 WhatsApp/Programar
  envío) en Cierres podían quedar escondidos aunque el usuario SÍ fuera Gerencia.** Bug de
  condición de carrera (no de permisos): si se entraba a "Cierres" (dispara `cdBoot()`,
  que solo corre una vez) ANTES de que `/apisuperv/identidad` terminara de resolver,
  `ID.veTodo` todavía estaba en su valor por defecto (`false`) en ese instante — y como
  `cdBoot()` nunca vuelve a correr, los botones quedaban escondidos el resto de esa carga
  de página aunque el chip de rol (arriba a la derecha) sí mostrara "Gerencia" un
  instante después. Se extrajo `cdAplicarVeTodo()` (antes un bloque `if` local dentro de
  `cdBoot()`) a función global, llamada desde los 2 lados posibles de la carrera (al
  terminar `/identidad`, y dentro de `cdBoot()`) — gane quien gane, el último en terminar
  deja los botones en el estado correcto.
- **El Dashboard ahora auto-arranca el sidecar WhatsAppSender si no está corriendo.**
  Pedido explícito: "¿por qué no incluyes que levantemos ese servicio con el proyecto?"
  — hasta ahora había que abrir `node server.js` a mano cada vez, y olvidarlo daba
  "el equipo de destino denegó la conexión" al enviar. Nuevo
  `WhatsAppSenderLauncherService` (`BackgroundService`, registrado en `Program.cs`): al
  arrancar el Dashboard, revisa si el sidecar responde en `/status`; si no, lo levanta
  como proceso DETACHED (ventana propia, igual que `INICIAR_WHATSAPP_SENDER.bat`) desde
  `WhatsAppSender/` (carpeta hermana por defecto, o `Smtp:WhatsApp:SidecarPath` en
  `appsettings.json`). Detached a propósito: si quedara como hijo del Dashboard, se
  moriría en cada recycle del app pool de IIS o cada reinicio de `CORRER_LOCAL.bat`,
  tumbando la sesión de WhatsApp vinculada. Nuevo `Smtp:WhatsApp:SidecarAutoStart`
  (`"true"` por defecto) para desactivarlo si ya está registrado como Servicio de Windows
  aparte (recomendado para producción — el auto-arranque es sobre todo para desarrollo
  local y como red de seguridad, no sobrevive un reinicio completo de Windows). Nunca
  bloquea el arranque del Dashboard si algo falla (carpeta no existe, `node` no está en
  el PATH) — solo lo deja logueado.
- **Envío por WhatsApp — botón "💬 Enviar por WhatsApp" on-demand (Persona o Grupo),
  desvincular/cambiar número, y fix de un bug real de QR vencido.** Tras probar el vínculo
  inicial: (1) el modal de vínculo mostraba "Vinculado" de forma indefinida aunque la
  sesión se hubiera caído (el polling se detenía al conectar) — ahora sigue consultando
  `/status` cada 5 seg. mientras el modal está abierto, conectado o no, para detectar una
  desconexión real; también se detectó que Baileys ROTA el QR cada ~20-30 seg., y escanear
  uno vencido da "no se puede vincular en este momento" en el teléfono — el modal ya
  refrescaba el QR, este fix es aparte (detectar desconexiones DESPUÉS de vincular). (2)
  Nuevo botón "🔌 Desvincular / cambiar número" en el modal de WhatsApp — borra la sesión
  del sidecar (`POST /logout` en Baileys) para poder escanear un número distinto. (3)
  Nuevo botón **"💬 Enviar por WhatsApp"** junto a "📧 Enviar por correo" en el Cierre
  Diario — abre un modal para mandar la imagen AHORA a una Persona (teléfono, con atajos a
  los destinatarios de la libreta que ya tienen Teléfono) o a un Grupo de WhatsApp (select
  poblado en vivo desde los grupos del número vinculado, `sock.groupFetchAllParticipating`
  en el sidecar) — con un acceso directo a "📅 Programar" si prefiere dejarlo agendado en
  vez de enviarlo ya. Nuevos endpoints: `GET whatsapp/grupos`, `POST whatsapp/desvincular`,
  `POST whatsapp/enviar-ahora` (regenera el reporte del lado del servidor con los mismos
  filtros en pantalla — no confía en el JSON que ya tiene el navegador). (4) Al probar el
  primer envío real salió "Could not find Chrome" — el `postinstall` de Puppeteer que
  descarga su Chrome propio quedó bloqueado por la protección de scripts de npm en esa
  máquina. En vez de depender de esa descarga (que además suele toparse con firewall/
  antivirus corporativo), el sidecar ahora detecta y usa el Chrome/Edge YA instalado en
  Windows (Edge viene de fábrica en Windows 10/11) — `detectarNavegadorSistema()` en
  `WhatsAppSender/server.js`, con `PUPPETEER_EXECUTABLE_PATH` como override manual si la
  ruta no es una de las estándar.
- **Envío del Cierre Diario por WhatsApp — dejó de ser un stub.** El usuario eligió el
  mecanismo: WhatsApp Web no oficial vía Baileys (Node.js, vinculado por QR a un número
  real). Se agregó un sidecar nuevo `WhatsAppSender/` (fuera del proyecto .NET, como
  `RutaVisitasSmart/`) que renderiza a imagen (Puppeteer) el HTML compacto que arma el
  propio Dashboard (`ArmarHtmlWhatsAppCierreDiario` — Ventas/Mes/días restantes/Pensamiento
  del día, pensado para salir como una sola imagen corta) y lo envía por WhatsApp. El
  envío programado (`ReportesProgramados_RM`, ya existente para correo) ahora también
  manda por WhatsApp a los destinatarios de la libreta que tengan Teléfono. Nuevo botón
  "📱 WhatsApp" en Supervisión → Cierres para vincular el número (QR) y ver el estado de
  conexión. Pendiente de TI: instalar Node/dependencias en el servidor y dejar
  `WhatsAppSender/` corriendo como servicio (`INICIAR_WHATSAPP_SENDER.bat` para probar
  local primero) — el vínculo real del WhatsApp y el envío real a un teléfono no se
  pudieron probar desde aquí.

---

## v1.11.1 — 2026-09-18 · 2 fixes: atribución en "Comparativo Ctes." + default de División en AMCO-GT

- **Históricos de Ventas → Análisis — fix: "Comparativo Ctes." (v1.11.0) atribuía las
  ventas al vendedor que hizo la transacción, no al vendedor asignado HOY en Clientes.**
  El usuario reportó (con ejemplo concreto) que si un cliente le compró a un vendedor en
  unos meses y a otro en otros meses, pero hoy está asignado a un tercero, el informe
  debía mostrar TODO ese historial bajo el vendedor actual — así puede revisar si le
  falta generar venta a clientes que ya tiene asignados. El modo "Rango vs Rango"
  agrupaba cada mes por el vendedor de esa transacción específica en vez de por el
  vendedor asignado hoy (solo "Vs Maestra Clientes" ya atribuía bien). Se unificaron los
  2 modos para que la atribución sea siempre por `Asignaciones()` + fallback
  `Clientes.RepCodigo` (vendedor asignado hoy) — la única diferencia que queda entre
  modos es si se pre-siembran en 0 los clientes asignados sin compra. Verificado contra
  producción con un caso real (13,107 clientes de 2026 tenían ventas de 2+ vendedores
  distintos en el rango). `dotnet build` limpio. No probado en navegador logueado.

- **Seguimientos / AMCO-GT — fix: el default de "División" dejaba fuera en silencio a
  vendedores activos de División 12 (ej. RepCodigo 77).** El usuario preguntó por qué el
  vendedor 77 y otros no medían en AMCO-GT. Causa: el filtro "División" venía
  preseleccionado a `['10','11']` al abrir la pantalla, y ese valor activaba un
  `INNER JOIN` en el backend que excluía a cualquier vendedor con `RepTipe` distinto de
  10/11 — no había ningún problema en la fórmula del IEGT ni exclusión intencional.
  Verificado que no era un caso aislado: 149 representantes activos quedaban fuera del
  default, 17 de ellos con actividad real reciente. El usuario confirmó que División
  10/11/12 son los 3 grupos reales del negocio — se agregó `'12'` al default
  (`Views/Seguimientos/Index.cshtml`). `dotnet build` y `node --check` limpios. No
  probado en navegador logueado.

---

## v1.11.0 — 2026-09-16 · Históricos de Ventas: pestañas nuevas "Notas de Crédito", "Pedidos" y "Comparativo Ctes."

- **Históricos de Ventas — pestaña nueva "Notas de Crédito" (Resumida/Detallada,
  Acumulado/Por Día, tarjetas de incidencia, entorno "Por Causa").** Pedido explícito del
  usuario: pestaña junto a "Ventas (Sup./Vend.)" con los mismos filtros + Cliente,
  toggles Resumido/Detallado y Acumulado/Por Día, árbol Centro→Supervisor→Vendedor→
  Cliente con subtotal al final de cada grupo, columnas Cantidad/Unidades-Cajas/Monto,
  tarjetas (NC del mes + incidencia vs. Ventas, NC de ayer + incidencia, incidencia por
  Centro), y un entorno "Por Causa". A diferencia de "Devoluciones" en Vend./Cte. (que
  infiere una devolución por el signo del monto en `VWVentas`), esta pestaña usa el
  **documento real de SAP**: `Orin` (cabecera de Nota de Crédito) + `RIN1` (detalle por
  producto) + `MotivosDevolucion` (catálogo de causas) — verificado en vivo: `Orin.
  CardCode`→`Clientes.CliCodigo` (100% match), `Orin.U_Repcodigo`→vendedor real (no el
  repartidor), `RIN1.ItemCode`→`Productos.ProCodigo`, `RIN1.LineTotal` = monto bruto sin
  ITBIS (nunca `Orin.DocTotal`, que sí incluye ITBIS). 3 endpoints nuevos
  (`/apihist/notascredito`, `-tarjetas`, `-causas`) + catálogo `estatusPedido`... *(ver
  detalle completo en `PROGRESS.md`, sesiones 32ª-34ª parte)*. Ajustado en vivo tras
  prueba del usuario: "Por Día" ahora muestra una fila por fecha con la fecha visible
  (antes repetía el árbol completo por fecha sin mostrarla), botón "📥 Excel" inline,
  tarjetas ocultables/compactas. **Verificado**: `dotnet build` limpio, `node --check`
  sin errores, harness de Node contra las funciones reales de agregación/render (16/16
  aserciones OK), y contra producción (solo lectura) con datos reales de septiembre-2026.
  **No probado en navegador logueado.**

- **Históricos de Ventas — pestaña nueva "Pedidos" (tarjetas + análisis por Centro/Canal/
  Supervisor, Supervisor/Vendedor, Suplidor/Categoría o Estatus).** Pedido explícito del
  usuario: filtros con Origen fijo "Pedidos" + filtro nuevo de Estatus de pedido
  (catálogo real `Estados` WHERE `EstTabla='Pedidos'`, default 2/7/11 = Transmitido/
  Pendiente Autorización/Facturado y Enviado al ERP, confirmado con el usuario); 5
  tarjetas (Monto Pedidos del día + cantidad, Pendientes por Facturar estatus 2/7 +
  cantidad, Monto por Canal, Monto por Centro, "Pedidos Perdidos" tipo fill-rate acotado
  a estatus 11); análisis con 4 modos mostrando Cantidad de Pedidos/Monto Bruto Pedido/
  Monto Bruto Facturado/Monto Sin Facturar/Incidencia%, con subtotal por nivel y total de
  compañía; imprimible en PDF y Excel. Adapta la fórmula ya probada de "Fill Rate"
  (`GatherFillRate`) a un `GatherPedidosLineas` nuevo, sin el filtro de estatus
  hardcodeado, con Canal del cliente (no del rep) y filtros completos de Grupo Producto/
  Grupo Clientes/Tamaño. 2 endpoints nuevos (`/apihist/pedidos`, `-tarjetas`) + catálogo
  `estatusPedido` agregado a `/apihist/filtros`. **Verificado**: `dotnet build` limpio,
  `node --check` sin errores, harness de Node contra las funciones reales `pd*` (12/12
  aserciones OK), y contra producción (solo lectura): hoy 1,631 pedidos estatus 2/7/11
  por RD$3,315,253.26; backlog 90 días 1,529 pedidos (2+7); "Pedidos Perdidos" mes en
  curso (estatus 11) 18,620 pedidos, incidencia ~1.5%. **No probado en navegador
  logueado.**

- **Históricos de Ventas → Análisis — sub-pestaña nueva "Comparativo Ctes." (matriz
  mensual por cliente).** Pedido explícito del usuario: junto a "Comparativos"/"Clientes
  Perdidos"/"Clientes Recuperados", con los mismos filtros de "Comparativos" + Tamaño
  Cliente + Día de Visita (nuevos); Modo "Rango vs Rango"/"Vs Maestra Clientes"; matriz
  por cliente (Código-Nombre Supervisor/Vendedor/Cliente/Tamaño + una columna por cada
  mes de la "Fecha Base", hasta el último mes completo antes del actual, + Promedio Mes +
  Mes Actual con ícono de tendencia + Diferencia), ordenada por Promedio descendente, con
  subtotal al cerrar cada Vendedor y cada Supervisor y Total General al final; botón para
  colapsar TODO el panel de filtros; columnas Supervisor/Vendedor/Cliente/Tamaño fijas al
  desplazar horizontalmente. Reutiliza 100% mecanismos ya probados: el pivote Mes1..MesN
  de `VendCte` (con una clave sentinela "COMP" adicional para la Fecha de Comparación), el
  universo `RutaVisitas`+fallback de `VendCte` alcance="todos" (modo "Vs Maestra
  Clientes"), y el Día de Visita de `ComparativoMaestra`
  (`RutaVisitas.RutSemana1/2`+`PrimerDiaActivo`/`DiaVisitaTexto`). 1 endpoint nuevo
  (`/apihist/comparativo-ctes`). **Verificado**: `dotnet build` limpio, `node --check`
  sin errores, harness de Node contra las funciones reales `ctc*` (16/16 aserciones OK),
  y contra producción (solo lectura): un vendedor/cliente real con 6 meses de historial
  (RD$20.9M total, Promedio ≈RD$3.49M/mes). **No probado en navegador logueado.**

**Pendiente de confirmar en pantalla (ninguna de las 3 features de arriba se probó en
vivo antes de este release):** ver el detalle de cada una en `PROGRESS.md`, sesiones
32ª-36ª parte — especialmente las ventanas de tiempo que se diseñaron por decisión propia
en "Pedidos" (tarjetas 1/3/4 "hoy", tarjeta 2 backlog 90 días) y el clamp de "Fecha Base"
en "Comparativo Ctes.".

---

## v1.10.0 — 2026-09-14 · Presupuestos (Gestión rediseñada, Estimado por Canal, Necesidad Diaria, fixes críticos) + Históricos (Devoluciones, PDF/Excel en 8 reportes, Comparativos vs Maestra Clientes) + AMCO-GT: reversión a perfil único

> **Pendientes de TI para esta versión** (la app funciona sin ellos, pero estas features quedan
> a medias hasta que se corran): `SQL/PresupuestoLotes_RM_Descripcion_Alter.sql`,
> `SQL/PresupuestoLotes_RM_Objetivo_Alter.sql`, `SQL/PresupuestoRappelMetas_RM.sql`,
> `SQL/PresupuestosCurvas_RM_Canal.sql`, `SQL/PresupuestoActivacionMetas_RM_v3.sql` (confirmar
> con el usuario cuál de las versiones de Activación Metas quedó vigente). Ver detalle de cada
> uno en las entradas de abajo y en `PROGRESS.md`.

- **Históricos de Ventas — Vend./Cte. (General): nuevo origen "Devoluciones" + columnas Clientes/
  Facturas en todos los niveles + drill "+" de clientes por vendedor.** Pedido explícito del
  usuario. **"Devoluciones"**: 3ra opción en el filtro "Origen" (antes solo Ventas/Pedidos) — sale
  de las mismas tablas de Ventas (`VWVentas`/`VWVentasDetalle`), con el mismo criterio de signo ya
  usado en todo `ApiHistoricosController.cs` para aislar devoluciones (`HAVING SUM(bruto)<0`, en
  vez de `>0` que se usa para netear); aplicado a los 3 endpoints que comparten este filtro
  (`VendCte`, `VendCteSupCat`, `VendCteDetalle`). **Columnas nuevas**: "Clientes" y "Facturas" en
  los 3 niveles de organización (Centro/Supervisor/Vendedor) del modo General — "Clientes" se
  deriva 100% en el frontend (el grano ya es por cliente); "Facturas" requirió backend: nuevo
  `COUNT(DISTINCT VenSecuencia/PedSecuencia)` agregado a `VendCte`, acumulado junto a Monto/Cajas y
  expuesto como `TotalFac` por fila (mismo patrón que los ya existentes `Total`/`TotalCajas`). El
  modo "Suplidor/Categoría" de esta misma pestaña ya las mostraba desde antes — no se tocó.
  **Drill "+"**: cada fila de "Por Vendedor" gana un ícono ➕/➖ que expande, en la MISMA tabla (sin
  perder de vista los demás vendedores — confirmado con el usuario vía `AskUserQuestion`), una
  fila hija con el detalle de clientes de ese vendedor (código, nombre, facturas, cajas, monto) —
  adaptado del patrón `toggleRebalDetalle` ya probado en `Views/PresupuestoGestion/Index.cshtml`
  (fila `<tr>` oculta por defecto, alternada por clic), sin tocar ese archivo ni requerir ningún
  endpoint nuevo (los datos ya estaban en `VCDATA`, mismo grano que ya usa la vista "Por Cliente"
  existente, que sigue intacta — el "+" es un atajo adicional, no la reemplaza). **Verificado**:
  `dotnet build` limpio, `node --check` sin errores; harness de Node ejecutando `vcAgg()`/
  `vcRenderAgg()` reales con datos sintéticos confirmando el conteo exacto de clientes/facturas
  por vendedor, la fila hija oculta con el `colspan` correcto, y que el drill de Suplidor/
  Categoría/Producto de un cliente puntual (que también reusa `vcRenderAgg`) sigue funcionando
  igual sin las columnas nuevas (regresión); contra producción (solo lectura) se encontraron
  combinaciones reales (Rep,Cliente,Mes) con monto neto negativo en agosto-2026 (ej. Rep 36 /
  Cliente 25455: 1 factura, -25 cajas, -RD$65,392.50) confirmando que el criterio de signo produce
  resultados reales y sensatos. **No probado en navegador logueado.**

- **Históricos de Ventas — fix del botón Excel roto en "Vs Maestra Clientes" + botón "⬇ PDF" nuevo
  en los 8 reportes activos del módulo.** El usuario reportó que "Excel" no hacía nada en el modo
  nuevo "Vs Maestra Clientes" y pidió, además, un PDF que se vea como la pantalla — "en todos los
  reportes de historicos de ventas debo tener el reporte en excel y en pdf". **Fix del Excel**:
  `excelComp()` (`Views/Historicos/Index.cshtml`) asumía ciegamente la forma de datos de los otros
  2 modos (`j.rangoA`, `f.montoMain`/`facMain`/etc.) — el modo nuevo devuelve `j.rango` y
  `f.clientesAsignados`/`montoTotal`/etc., así que la primera línea de la función tronaba con un
  `TypeError` SÍNCRONO (sin try/catch, sin alert) — exactamente el "no hace nada" reportado. Se
  agregó `excelCompMaestra()`, una rama dedicada con sus propias columnas (incluida una 2da hoja
  "Clientes" con código/nombre/compró/monto/día de visita cuando la vista es "Detallado/Clientes").
  De paso se corrigió un bug relacionado en el mismo router: el botón global de Excel, estando en
  "Análisis", SIEMPRE llamaba `excelComp()` sin importar la sub-pestaña activa — si el usuario
  estaba en "Clientes Perdidos"/"Recuperados" (que ya exportaban bien, pero solo con su botón
  propio) el de arriba exportaba el Comparativo por error; ahora se agregó `SUBANA` (seteada en
  `subAna()`) para que el botón global respete la sub-pestaña real. **PDF nuevo**: se portó (sin
  tocar el original) el algoritmo de paginación A4 ya probado en producción en
  `Views/Supervision/Index.cshtml` (`ppExportarPDF` — captura por bloque vía html2canvas, corte
  alineado al borde inferior de `<tr>` cuando un bloque no cabe entero en una hoja, encabezado
  repetido en cada página) a una versión genérica `histExportarPDF(cfg)` que recibe nodos del DOM
  directamente en vez de exigir las clases CSS de ese otro módulo — así cualquier reporte de
  Históricos la reusa con sus propios divs. Se agregó el botón "⬇ PDF" a los 8 reportes activos
  (Ventas Sup./Vend., Activaciones, Comparativos —los 3 modos—, Clientes Perdidos, Clientes
  Recuperados, Vend./Cte., Ped-Fact-NC, Fill Rate); los 4 que no tenían ningún resumen de
  filtros/rango imprimible (Ventas, Vend./Cte., Ped-Fact-NC, Fill Rate) ganaron uno nuevo (mismo
  patrón `#a-print-head` ya usado por los otros 4). **De paso**, Ped-Fact-NC y Fill Rate —que no
  tenían NINGUNA exportación a Excel, solo un `alert('pendiente')`— ganaron `excelPF()`/`excelFR()`
  completas (general y Suplidor/Categoría). **Verificado**: `node --check` sin errores tras cada
  fase; harness de Node ejecutando `excelCompMaestra()` contra la forma real de respuesta de
  `/apihist/comparativo-maestra` confirmando que ya arma las 2 hojas sin tronar; HTML con divs
  balanceados (273/273) y sin ids duplicados inesperados; `dotnet build` limpio. **No se pudo
  probar la generación real del PDF** (requiere renderizado de canvas en un navegador real) — se
  verificó por revisión de código y por ser el mismo algoritmo ya confirmado en producción para
  Supervisión Dinámica, comunicado así al usuario para que lo pruebe él mismo en pantalla.

- **Históricos de Ventas — Comparativos: nuevo modo "Vs Maestra Clientes".** Pedido explícito del
  usuario: a diferencia de los otros 2 modos (que comparan monto/facturas contra OTRO periodo),
  este compara contra el **universo de clientes asignados** — todos los clientes del Grupo de
  Clientes elegido, asignados vía `RutaVisitas` (misma "maestra" que ya usan "Clientes Perdidos"/
  "Clientes Recuperados") — mostrando cuántos de esos clientes de hecho compraron en el rango y
  productos elegidos (**% de activación**), no cuánto vendieron vs. el mes pasado. Restringido a
  "Analizar por: Supervisor → Vendedor" (confirmado con el usuario — "clientes asignados" es un
  concepto ligado al vendedor vía `RutaVisitas`, sin análogo natural por Suplidor/Canal/Cliente; al
  elegir este modo, "Analizar por" se bloquea en Supervisor y "Periodos atrás" se oculta). Columnas
  nuevas: **Clientes Asignados** (justo después del nombre de Supervisor/Vendedor), **Clientes en
  el rango** (distintos, activados) en el lugar donde antes iba "Facturas", y el **Δ%** ahora
  compara activados vs. asignados (no contra un periodo anterior). Nueva vista **"Detallado/
  Clientes"** (3ra opción en el filtro "Vista", solo visible en este modo): baja el drill hasta
  cada cliente individual (código, nombre, si compró, monto) con una columna **"Día de visita"**
  resuelta desde `RutaVisitas.RutSemana1`/`RutSemana2` (puerto mínimo a C# de
  `RutaVisitasSmart/server/src/diaResolver.ts`, que hasta ahora solo existía en el proyecto
  hermano Node/TS) — si el cliente visita en día distinto entre semana1 y semana2, se muestran
  ambos separados por "/" (ej. "Jueves/Viernes"; confirmado con el usuario que es barra normal, no
  invertida). Nuevo endpoint `GET /apihist/comparativo-maestra`
  (`Controllers/ApiHistoricosController.cs`) — separado de `Comparativo()` para no complicar la
  lógica de buckets A/B1..Bn que ese método ya tiene y que no aplica aquí; reusa tal cual
  `Asignaciones()`, `RepsFiltrados()`, `CargarReps()` (mismas funciones ya probadas de "Clientes
  Perdidos"/"Recuperados"), solo se sumó una consulta nueva de monto por (rep,cliente) y el
  resolvedor de día de visita. **Verificado**: `dotnet build` limpio, `node --check` sin errores,
  harness de Node ejercitando el bloqueo/desbloqueo de "Analizar por" y la aparición/desaparición
  de la opción "Detallado/Clientes" sin duplicados; contra producción (solo lectura) se comparó a
  mano (SQL directo) el % de activación de un Grupo de Clientes real (56 asignados, 37 activados en
  agosto-2026 = 66.1%, RD$280,636.15) y se confirmó un caso real de `RutSemana1`≠`RutSemana2` con
  días distintos, saliendo "Jueves/Viernes" como se esperaba. **No probado en navegador logueado.**

- **Presupuestos — Gestión: sub-menú de 4 pestañas (Generar/Rebalancear/Editar/Borrar) — solo un
  entorno visible a la vez.** Pedido explícito del usuario tras la separación de entornos de la
  entrega anterior: "no quiero ver tantos datos de entrada cuando en algunos casos no voy a
  trabajar con ellos". Se agregó una barra de pestañas (`Views/PresupuestoGestion/Index.cshtml`,
  nueva clase `.tab-btn`) arriba de los paneles — "Generar Presupuesto", "Rebalancear por
  Categoría", "Editar Presupuesto", "Borrar Presupuesto" — que muestra/oculta el CONTENEDOR
  completo de cada entorno (`tabGenerar`/`tabRebalancear`/`tabEditar`/`tabEliminar`) vía
  `cambiarTabPrincipal()`. Los paneles internos que ya se mostraban/ocultaban condicionalmente
  ("2. Revisar" solo tras generar, "5b. Revisar y publicar edición" solo tras editar) no se
  tocaron — siguen con su propia lógica de `display:none`/`block`, independiente del sub-menú: un
  panel solo es visible si SU pestaña está activa Y su propia condición interna se cumple.
  Verificado: `node --check` sin errores, HTML con divs balanceados (151/151) y sin ids
  duplicados, `dotnet build` limpio. **No probado en navegador logueado.**

- **Presupuestos — Gestión: "Editar", "Rebalancear" y "Eliminar" pasan a ser entornos separados,
  cada uno con su propio cálculo (Generar) y su propia carga (Publicar).** Pedido explícito del
  usuario, con 3 capturas de referencia. Antes, "5. Editar / Reajustar Presupuesto" **compartía**
  con "1. Generar Nuevo Presupuesto" el mismo panel "2. Revisar, ajustar y publicar" y las mismas
  variables globales de JS (`loteActual`/`filasActuales`) — si un gerente generaba un lote en la
  sección 1 y luego usaba "Editar" sin publicar, el primer lote quedaba huérfano en pantalla (la
  variable se sobreescribía; el lote seguía en la base como Borrador pero ya no había forma de
  volver a él sin recargar la página y pegar el GUID a mano). **Fix**: `Views/PresupuestoGestion/
  Index.cshtml` reemplaza esas variables globales por dos objetos de contexto independientes
  (`CTX_GEN`/`CTX_ED`, con su propio `lote`/`filas` y su propio juego de ids de DOM), y todas las
  funciones del panel de revisión (`cargarLote`, `pintarTabla`, `editarCelda`, `guardarValores`,
  `eliminarFila`, agregar fila, exportar/importar Excel, Publicar, Descartar) pasan a recibir el
  contexto como parámetro en vez de tocar variables compartidas. "5. Editar" gana su propio panel
  "5b. Revisar y publicar edición" (mismos controles que el de "1. Generar": tabla editable,
  agregar fila, exportar/importar, Publicar/Descartar) — "3. Análisis comparativo" queda exclusivo
  del flujo de "1. Generar" (no se duplicó, no fue pedido). Además: **"4. Rebalancear presupuesto
  real por Categoría"** gana el filtro de **Vendedor** pedido explícitamente ("incluir
  representante") — nuevo parámetro `reps` en `RebalancearReq`/`RebalancearAplicarReq`
  (`ApiPresupuestoGestionController.cs`), intersectado contra los reps con presupuesto ese mes
  antes del filtro de canal existente. Se renombraron botones por consistencia de vocabulario en
  los 3 entornos: "Vista previa" → **"Generar"** (Rebalancear y Eliminar), "Aplicar a producción" →
  **"Publicar a producción"** (Rebalancear). El botón de Eliminar se dejó deliberadamente como
  **"Eliminar de producción"** (no "Publicar") — llamar "Publicar" a un `DELETE` irreversible podía
  hacer que alguien subestimara el riesgo; el gate real de seguridad (escribir "CONFIRMAR" +
  `confirm()` del navegador) no cambió. **Verificado**: `node --check` sobre el JS embebido — sin
  errores; harness de Node reproduciendo el escenario exacto del bug (generar en "1", sin publicar,
  generar en "5") confirmando que `CTX_GEN.lote` ya NO se sobreescribe y que los ids de DOM de
  ambos contextos no colisionan; grep final sin referencias colgantes a `loteActual`/
  `filasActuales` en todo el archivo; `dotnet build` limpio (2 compilaciones). **No probado en
  navegador logueado** (herramienta solo-Gerencia, regla de seguridad de la sesión). **Fuera de
  alcance** (no confirmado con el usuario): la inconsistencia de `RebalancearCategoriaAplicar`
  escribiendo siempre a producción real sin pasar por `Destino()` (a diferencia de
  Publicar/Eliminar) — sigue igual, reportada aparte.

- **AMCO-GT (Seguimientos/IEGT): revertida la metodología separada de Mayorista y los
  indicadores "Logro Pre Ventas"/"Tiempo en Cliente" — vuelve a un solo perfil de 7 indicadores
  para todos los canales (con "Consistencia PDV"/"Ritmo de visita" como estaban originalmente).**
  El usuario reportó que la reestructuración del **v1.9.0** (2026-09-04, ver esa sección más abajo)
  "está complicando la medición" y pidió volver al esquema original, afinando de a poco en vez de
  cambios grandes de una vez. **Importante: esto revierte una funcionalidad que YA SALIÓ en v1.9.0/
  v1.9.1** — no es solo deshacer trabajo acumulado sin liberar; hasta que esta entrada se empaquete
  en una versión nueva y se instale en el servidor, la instancia en producción sigue mostrando el
  split Mayorista/Minorista viejo. **Cambios** (todos en
  `Views/Seguimientos/Index.cshtml`, único archivo tocado — el backend
  `ApiSeguimientosController.cs` se deja intacto a propósito: los 4 campos que agregó para
  Mayorista, `CanID`/`PrimeraVisitaMin`/`PresupuestoSemana`+`EjecutadoSemana`/`VisitasSemana`+
  `VisitasConInventario`, simplemente quedan sin usar en el frontend, no rompen nada — se pueden
  quitar del backend más adelante si se decide no retomar Mayorista): `scoresDe()` vuelve a un
  único perfil de 7 indicadores (sin la rama `if(esMayorista(v))`), con `pdv`/`ritmo` calculados de
  nuevo con las funciones originales `subPDV`/`subRitmo` (nunca se habían borrado — el modo "Por
  Suplidor" las seguía usando, lo que hizo el revert casi un "reconectar" en vez de un
  "reconstruir"); `nivelStats()` vuelve a recalcular siempre desde las métricas sumadas, sin la
  rama de "canales mixtos" (promediar IEGTs ya calculados); `puntosTabla`/`verNivel`/`verVendedor`/
  `conclusion`/`planAccion` pierden sus ramas condicionadas a `perfil==='mayorista'`; se eliminan
  `conclusionMayorista`/`focoTrabajoMayorista`/`planAccionMayorista` y las constantes
  `PESOS_MAYORISTA`/`MAY_INICIO`/`MAY_RUTA`/`MAY_CIERRE`/`MAY_PRESUP`/`MAY_INVENTARIO_MIN`; la
  pantalla de Configuración pierde la sección "Mayorista (fijo, no editable)" y los umbrales de
  Logro Pre Ventas/Tiempo en Cliente, y sus 2 filas de pesos vuelven a decir "Consistencia PDV"/
  "Ritmo de visita". La **rampa lineal** de los 7 sub-scores (2026-09-03, "8:21 ya no saca la
  misma nota que 8:30") **se mantiene intacta** — el usuario pidió revertir solo el split de
  canal, no la suavización de las notas. **Verificado**: `node --check` sobre el JS embebido — OK;
  harness de Node ejecutando `scoresDe()`/`nivelStats()`/`puntosTabla()` reales contra vendedores
  sintéticos — confirma que ya no hay campo `perfil` en el resultado, que las etiquetas vuelven a
  decir "Consistencia PDV"/"Ritmo de visita", y que ninguna mención de "Mayorista"/"Logro Pre
  Ventas" sobrevive en el HTML generado. Grep final sobre todo el archivo: cero referencias
  colgantes a `esMayorista`, `PESOS_MAYORISTA`, `perfil`, o los campos de Mayorista. **No probado
  en navegador logueado.**

- **Fix crítico: "Estimado" de Presupuestos usaba UNA sola curva de estacionalidad "General"
  (mezclando todos los canales) para TODAS las filas de "Por Canal", y los domingos recibían un
  peso normal que se replegaba sobre el lunes siguiente en vez de no aportar nada.** El usuario
  reportó, con capturas de "Detalle → Por Canal" (Minorista/Mayorista), "hoy estoy proyectando 63
  MM y dizque venderé 90 MM, cuál es el racional de esto" — pidió el cálculo día por día para
  llegar a un acuerdo. **Causa raíz #1 (curva compartida):** `Resumen()` y `DrillOrganizacion()`
  calculaban la curva UNA sola vez con el canal del FILTRO (`vCanales`, típicamente 6 canales
  marcados por defecto) en vez del canal real de cada fila — como `ObtenerCurva` solo resuelve un
  canal específico cuando hay EXACTAMENTE 1 seleccionado, en el estado normal del filtro
  siempre caía a `canId=0` (curva General) y esa misma curva blended se aplicaba a Minorista,
  Mayorista, etc. por igual, aunque la pantalla los mostrara separados por canal. Reproducido en
  vivo: la curva real de Minorista (17.65/29.71/20.10/32.54%) es distinta a la General
  (14.91/28.82/19.32/36.95%), y aplicando la General al caso del usuario (Ejecutado=8,973,512.40,
  diasT=5) da Estimado≈90,276,785 — coincide casi exacto con los RD$90.2MM reportados,
  confirmando la teoría. **Causa raíz #2 (domingo):** el negocio no opera los domingos — antes
  un domingo recibía un peso "normal" que se replegaba sobre el lunes siguiente (fix de la
  entrada anterior de este changelog); el usuario pidió explícitamente que el domingo **no tenga
  peso en absoluto**, ni propio ni replegado — "los pesos deben ser de lunes a sábado, ese valor
  del domingo no aplica". **Fix:** (1) `PesoDiaEnFranja` (`Services/PresupuestosCore.cs`) ahora
  devuelve 0 para domingo, verificado ANTES que los casos especiales de día 1/último día; (2)
  nuevo `AjustarEstimadoPorCanal` (hermano del ya existente `AjustarProyectadoPorCanal`) que suma
  el Estimado de cada franja canal-específica con su propia curva y su propio `diasT`; conectado
  en `Resumen()` (bloques "Del Mes" y "Del Año" mensual) y en `DrillOrganizacion()` (cada fila ya
  trae su `CanID`, se resuelve su curva propia vía una caché por canal); y en `DrillProducto()` el
  JOIN/GROUP BY por canal —antes solo activo con "Desglosar por Canal" marcado— ahora es
  incondicional: sin el flag, las filas por canal se colapsan de vuelta a 1 fila por
  Suplidor/Categoría/Producto (mismo contrato de salida hacia el frontend) pero el Estimado se
  pondera con la curva propia de cada canal en vez de una sola curva general. **Verificado**:
  simulación día-por-día en Python replicando la lógica real (reparto uniforme dentro de cada
  franja, domingo=0, curva de Minorista) contra el caso exacto del usuario — con ambos fixes,
  Estimado a diasT=5 baja de RD$90.2MM a **RD$63.55MM**, en línea con el Proyectado que el usuario
  ya consideraba razonable ("hoy estoy proyectando 63 MM"). `dotnet build` limpio (2 compilaciones,
  una por cada tanda de cambios). **Pendiente**: no se pudo probar en navegador logueado (regla de
  seguridad de la sesión) — solo verificado por simulación numérica + revisión de código;
  `Resumen()`'s bloque "Del Año" (total anual, no mensual) sigue sin curva alguna
  (`Calcular(..., null, ...)`, ya era así antes) — no tiene Estimado curva-based que corregir,
  queda fuera de alcance por diseño.

- **Fix crítico: "Estimado" de Presupuestos inflaba el número ~15% de más, todos los meses, de
  forma sistemática.** El usuario pidió explicación paso a paso de "Proyectado"/"Estimado" y
  sospechaba una falla grave — se encontró revisando `CurvaDesdeSerieDiaria`
  (`Services/PresupuestosCore.cs`), la función que construye la curva de estacionalidad usada por
  "Estimado" (introducida esta misma semana, 18ª parte). **Causa raíz:** al recorrer los días del
  mes, cuando el día era domingo el código hacía `continue` **antes** de sumar su valor al
  acumulado — el aporte de cada domingo se perdía por completo, nunca se sumaba a ningún lado (el
  propio comentario del código ya describía el comportamiento CORRECTO — "el domingo no avanza el
  índice pero SÍ se suma al acumulado" — pero la implementación no lo hacía). Efecto: la curva
  nunca llegaba a 1.0 (100%) ni en el ÚLTIMO día hábil del mes — con 4 domingos en el mes (caso
  típico), tope real medido en 0.87 en vez de 1.0. Como `Estimado = Ejecutado ÷ factor`, dividir
  entre 0.87 en vez de 1.0 infla el resultado ~15% — con RD$1,000,000 ejecutado, mostraba
  RD$1,149,000 en vez de RD$1,000,000, incluso con el mes prácticamente cerrado. El error crecía
  según cuántos domingos ya habían "pasado" ese mes (peor a mitad/fin de mes que al inicio).
  **Fix**: mover la suma al acumulado ANTES del `continue` — un domingo ahora sí aporta su parte,
  solo no avanza el índice de día hábil (se lee en el próximo día hábil, como estaba documentado
  desde el principio). Verificado con una simulación en Python usando los pesos reales de
  producción (14.9%/28.8%/19.3%/36.9%, medidos esta semana): con el bug, curva en el último día
  hábil de septiembre-2026 = 0.8699; corregido = 1.0000 exacto. `dotnet build` limpio.
  **No probado en navegador logueado.**

- **Fix crítico: "Descargar Excel de revisión" (Presupuestos — Gestión) fallaba con "Cada expresión
  GROUP BY debe contener al menos una columna que no sea una referencia externa".** Reportado por
  el usuario en vivo. Causa: la hoja "Resumen" pide el total de TODO el lote (Clientes Efectivos y
  Facturas) llamando a `ClientesEfectivosPorOrg`/`FacturasPorOrg` con el literal `"'TOTAL'"` en vez
  de una columna real — al sustituirlo en la plantilla SQL quedaba `GROUP BY 'TOTAL'` (agrupar por
  una CONSTANTE) dentro de una consulta con un `EXISTS` correlacionado — mismo patrón exacto que ya
  rompió `TorrePersonalizados.Calcular` (v1.8.2): SQL Server rechaza agrupar por un literal cuando
  se combina con ciertas subconsultas. **Reproducido el error EXACTO contra producción** con la
  consulta vieja antes de tocar código. Fix: 2 métodos nuevos y dedicados,
  `ClientesEfectivosTotal`/`FacturasTotal` (`ApiPresupuestoGestionController.cs`) — mismo WHERE,
  pero **sin ningún GROUP BY** (un `COUNT(DISTINCT...)` solo ya colapsa a 1 fila con el total
  correcto, agrupar por una constante nunca aportaba nada). Reverificado contra producción con la
  consulta corregida: sin error, valores reales (17,153 clientes efectivos / 94,298 facturas para
  el Rango Base jun-ago 2026 de un lote real). `dotnet build` limpio.

- **Presupuestos — Gestión: "Generar" ahora convierte el Objetivo TOTAL/POR MES a unidades usando
  precio de Lista (no promedio de ventas) — cierra un hueco marcado desde el 2026-08-31.** Ese día
  se corrigió "3. Análisis Comparativo" para valorar "Sugerido"/"Ventas Promedio" con precio de
  Lista real (por Rep×Producto, vía `RepresentantesAdicionales.LipCodigo`→`ListaPrecios`) en vez
  de un promedio de ventas — pero se dejó explícitamente pendiente que `Generar` seguía convirtiendo
  el "Objetivo en Valor" a unidades con el precio PROMEDIO de ventas (`PrecioPromedioPorProducto`,
  más bajo por los descuentos reales). Resultado: pedías, por ejemplo, un Objetivo de RD$83.5MM, y
  al revisarlo en "3. Análisis Comparativo" salía en RD$88.2MM — mismas unidades, dos precios
  distintos usados en cada lado. El usuario lo reportó de nuevo hoy (2026-09-06) al toparse con la
  discrepancia real en pantalla; decisión explícita tras compararle las 2 alternativas: el Objetivo
  debe representar un TECHO contra precio de Lista, no contra venta real con descuento — "si le dan
  descuento, debe vender más unidades para cubrirlo, pero no es correcto que pida redistribuir
  83.5MM y el cálculo saque un valor diferente."
  - `Controllers/ApiPresupuestoGestionController.cs` (`Generar`, PASO 2 — reescalado por mes):
    `NaturalRD`/el caché de precio ahora usan `PresupuestoBuilderCore.PrecioListaPorRepYProducto`
    (por Rep×Producto, respeta el canal/lista de cada vendedor) en vez de
    `PrecioPromedioPorProducto` — mismo precio que ya usa "3. Análisis Comparativo", así que el
    monto que se pide en "Objetivo" y el que se ve después en "Sugerido" quedan garantizados
    iguales (hasta redondeo).
  - `PrecioPromedioPorProducto` (`Services/PresupuestoBuilderCore.cs`) quedó sin ningún caller en
    todo el proyecto tras este cambio — eliminada (no se dejan funciones muertas).
  - **Verificado contra producción en vivo** (solo lectura): el lote real que generó el usuario hoy
    a las 16:28 con Objetivo=RD$83,500,000 (ANTES de este fix) efectivamente suma
    **RD$88,214,616.36** al valorar sus unidades ya guardadas a precio de Lista — coincide EXACTO,
    centavo por centavo, con lo que mostraba su pantalla. Confirma el diagnóstico al 100%. Ese lote
    específico quedó desactualizado (se generó con la lógica vieja) — **hay que volver a generarlo**
    para que el nuevo cálculo aplique; los lotes generados DESPUÉS de este fix ya calculan bien.
  - `dotnet build` limpio (0 errores). **Pendiente que el usuario regenere el lote y confirme en
    pantalla** que "Sugerido" en Análisis Comparativo ahora coincide con el Objetivo pedido.

- **Presupuestos: tarjetas "Días del mes/transcurridos/restantes/Incidencia" + columna "Necesidad
  Diaria"** en los 3 entornos del módulo (KPI Presupuestos, Dashboard Gráficos, Detalle). Pedido
  explícito del usuario. Necesidad Diaria = (Presupuesto − Ejecutado) ÷ Días Restantes — cuánto
  hace falta vender por cada día hábil que queda para llegar exacto al presupuesto del mes.
  - **Días hábiles con el mismo rezago que ya usaba internamente "Pres. al día"**: el día 1 del
    mes casi no factura (la preventa de HOY se factura mañana) y el último día factura 2 días de
    golpe — reusa `PresupuestosCore.DiasTranscurridosAjustado` (existía desde antes, solo para
    el Proyectado por canal), ahora expuesto en `KpiResultado` para cualquier pantalla. Verificado
    contra producción en vivo: 6 de septiembre de 2026 (domingo) → 26 días hábiles el mes, 4
    transcurridos, 22 restantes — coincide exacto con el ejemplo del usuario.
  - Backend: nuevos campos `DiasTranscurridosAjustado`/`DiasRestantes`/`NecesidadDiaria` en
    `PresupuestosCore.KpiResultado`, calculados una sola vez dentro de `Calcular(...)` — cero
    cambios en las fórmulas ya existentes de Proyectado/Estimado. Los 3 endpoints de Drill
    (`Organizacion`/`Producto` niveles 1 y 3) exponen los campos nuevos en su respuesta (antes
    listaban campos explícitos, no serializaban el objeto completo).
  - Frontend: nueva fila de tarjetas (`cardsDiasHtml`) insertada en "Del Mes" (arriba de la tabla
    KPI, visible también sobre "Detalle" que vive en la misma pestaña) y en Dashboard Gráficos;
    columna nueva en la tabla KPI y en `DRILL_COLS` (Detalle). "Del Año"/Histórico/Extras no se
    tocaron (fuera de alcance, decisión explícita del usuario).
  - Verificado con `dotnet build` limpio y `node --check`. **No probado en navegador logueado.**
  - **Fix el mismo día**: el usuario reportó "Necesidad Diaria" en blanco ("—") en TODAS las
    filas de "Detalle" (Por Centro/Supervisor/Canal), aunque el total de la tabla KPI arriba sí
    mostraba un número. Causa: el backend manda la fila a nivel de VENDEDOR (grano real de
    `PresupuestosHistoricos`); el navegador agrega esas filas en Centro/Supervisor/Canal con
    `drillAggOrg`/`drillAggProd`, funciones que ya sumaban Presupuesto/Ejecutado/Proyectado/
    Estimado explícitamente — pero nunca se les enseñó sobre el campo nuevo `NecesidadDiaria`,
    así que se perdía al agregar (quedaba `undefined`, se pintaba como "—", y el total daba 0
    porque nada se sumaba). Corregido: ambas funciones ahora recalculan Necesidad Diaria sobre
    el Presupuesto/Ejecutado YA agregados de ese nivel, usando `DiasRestantes` (igual para
    todas las filas del mismo período). Verificado con un harness de Node ejecutando la función
    real contra datos sintéticos calcados de la captura del usuario — Centro Azua: 2,034,258,
    Centro Santo Domingo: 1,379,054, suma 3,413,312 = exactamente el total que ya mostraba
    correctamente la tabla KPI general.

- **Presupuestos — Gestión: rediseño de prioridades del motor + "Editar/Eliminar Presupuestos"
  + Excel por Canal + Objetivo por Mes + Crecimiento sugerido entre mes.** ⚠️ **NO VERIFICADO EN
  VIVO** — implementado y compila limpio (`dotnet build` sin errores/warnings), pero esta máquina
  de desarrollo no tiene ruta de red hacia el SQL de producción, así que nada de esto se probó
  contra datos reales todavía. Ver `PROGRESS.md` (sesión 2026-09-06) para el detalle completo,
  los 3 scripts SQL nuevos que hacen falta correr antes de usar esto, y la lista de qué probar.
  Resumen:
  - Rapel por Suplidor pasa de reemplazar el factor de crecimiento a ser un PISO (máximo entre
    ambos).
  - Activación mínima Obligatoria ahora tiene un tope configurable (`TOPE_ACTIVACION_PCT`,
    default 5%) sobre cuánto puede incidir en el total del lote.
  - Efectividad mínima Obligatoria ahora concentra su incremento en la categoría de mayor
    volumen del rep que Activación no tenga ya cubierta (antes se repartía entre TODO lo que
    vende el rep), con cascada de respaldo.
  - "Objetivo" al Generar ahora se puede fijar POR MES además de un Total combinado — se
    reparte lo que quede del Total entre los meses sin objetivo propio, y cada mes queda
    garantizado en su porción exacta (reescalado ahora es por mes, no un solo reescalado
    global).
  - Nuevos entornos **"5. Editar / Reajustar Presupuesto"** y **"6. Eliminar Presupuestos"** —
    mismo motor de Generar pero acotado a un bloque (Canal/Supervisor/Vendedor/Suplidor/
    Categoría/Producto); Eliminar bloquea meses cerrados (anteriores al mes actual) tanto en
    front como en servidor.
  - Excel de revisión: nueva pestaña "Por Canal" + encabezado de Objetivo vs. suma actual en
    las 6 hojas.
  - Nueva sección "Crecimiento sugerido entre mes" en ⚙️ Configuración (matriz Ene-Dic en %,
    alimenta la misma variable `FACTOR_CRECIMIENTO_MES` de siempre).
  - **Actualización (mismo día, probando en vivo):** 2 bugs reales corregidos — falta el ALTER de
    `PresupuestoLotes_RM` (script ya listado), y `Descripcion` truncada (ampliada a varchar(4000),
    `SQL/PresupuestoLotes_RM_Descripcion_Alter.sql`). Además, nombres de Suplidor/Categoría ahora
    se muestran en "Metas Rapel" y demás variables por Ámbito (solo front). Y un pedido nuevo ya
    implementado: **Modelo Rappel generalizado** — además de Monto (`META_RAPPEL`, sigue igual),
    ahora también Activaciones y Factura (conteo de clientes activos / facturas-pedidos distintos)
    por Suplidor O Categoría, pudiendo coexistir varios tipos, cada uno como piso independiente —
    tabla nueva `PresupuestoRappelMetas_RM` (`SQL/PresupuestoRappelMetas_RM.sql`), CRUD y pantalla
    dedicada en ⚙️ Configuración. **Van 5 scripts SQL pendientes de correr en total** (los 3
    originales + estos 2 nuevos) — nada de esto último verificado en vivo todavía.

- **Presupuestos — "Estimado" ahora tiene patrón propio por Canal (además de Suplidor/
  Categoría), basado en 4 franjas del ciclo de venta.** Pedido explícito del usuario: las
  estimaciones de cierre de mes deben venir de un patrón de ventas real observado en VWVentas/
  VWVentasDetalle (últimos 6 meses cerrados), dividido en 4 franjas — días 1-7, 8-16, 17-22,
  23-cierre — en vez de solo un promedio diario continuo por Suplidor/Categoría (lo que ya
  existía). "Proyectado" (el lineal simple) no se tocó, sigue igual.
  - **Nuevo motor** `PresupuestosCore.CurvaPorFranjas`/`PesosFranjas` — reemplaza
    `CurvaCategoria`/`CurvaSuplidor`/`CurvaGeneral` (fuente unificada: 100% VWVentas/
    VWVentasDetalle, ya no depende de `PresupuestosHistoricos.MNTDCAT2DIA` para Categoría).
    `ObtenerCurva` (`ApiPresupuestosController.cs`) gana el eje Canal con la misma regla
    "exactamente 1 elegido, si no cae al agregado" que ya usaba para Suplidor/Categoría —
    ataca el caso ya documentado en el código de Mayorista, que se comporta muy distinto a
    Minorista dentro del mes (verificado en vivo: Mayorista concentra 46.8% de sus ventas en
    "23-cierre" vs. 32.5% de Minorista).
  - **Nuevo endpoint** `GET /apipresupuestos/patron-franjas` + botón **"ⓘ Patrón"** junto a
    "Consultar" (`Views/Presupuestos/Index.cshtml`) — muestra en un popover las 4 franjas
    descubiertas para el Suplidor/Categoría/Canal vigente en el filtro (pedido explícito del
    usuario: "quiero ver el patrón, no solo el número final").
  - **2 bugs reales encontrados y corregidos durante la verificación en vivo** (el usuario los
    señaló al revisar los primeros resultados, antes de que se instalara nada):
    1. El join usaba `VWVentas.RepCodigo` para identificar al vendedor — pero esa columna es el
       **repartidor**, no el vendedor; el vendedor real está en `RepVendedor`. Con el bug, los 7
       vendedores marcados Canal=Mayorista mostraban 0 meses con datos (parecía que Mayorista no
       tenía historial en VWVentas); corregido el join a `RepVendedor`, Mayorista pasó a 6/6
       meses con un patrón real y creíble. Esto también distorsionaba los pesos de TODOS los
       demás canales (no solo Mayorista), no únicamente un caso vacío.
    2. Repartir el peso de cada franja de forma pareja entre sus días (reparto uniforme)
       ignoraba un patrón real de facturación que el usuario describió y se confirmó en vivo:
       el día 1 del mes casi no factura nada (la preventa de HOY se factura al día siguiente),
       salvo el ÚLTIMO día del mes, que factura 2 días de venta de golpe. Corregido: el
       materializado ahora pondera día 1=0, último día=2×, resto=1× dentro de cada franja.
  - **Migración SQL pendiente de TI**: `SQL/PresupuestosCurvas_RM_Canal.sql` (agrega `CurCanID`
    a la tabla de caché de curvas — degrada sola, sin romper nada, si todavía no se corre).
  - **Fuera de alcance de esta entrega** (decisión explícita del usuario): Supervisor y Vendedor
    como ejes adicionales del patrón (solo Canal por ahora); atribución de Canal vía
    `clientesDetalle`/reasignación de clientes (más preciso para reps con clientes reasignados
    recientemente, pero requiere el mismo filtro de "universo de productos por vendedor" que ya
    usa `PresupuestoBuilderCore.CalcularBaseHistorica` para no duplicar clientes con 2+
    vendedores — se dejó con la atribución simple por `RepVendedor`).
  - Verificado con `dotnet build` limpio, `node --check` sobre el JS embebido, y consultas
    reales contra producción (solo lectura) reproduciendo ambos bugs y confirmando el fix.
    **No probado en navegador logueado** — pendiente que el usuario confirme el botón "ⓘ
    Patrón" y los números de "Estimado" en vivo.

---

## v1.9.1 — 2026-09-04 · Fix crítico: "Cierre Diario" fallaba con "Must declare the scalar variable '@d1AND'" al filtrar por Canal

El usuario reportó el error EN VIVO, en producción, generando un Cierre Diario con Canales
filtrado (5 de los canales disponibles, no "Todos"). Pantalla: `/supervision` → PDF
Presentaciones → Cierres → Cierre diario → Generar informe.

- **Causa raíz:** `CalcularCalendarios30Dias` (`Controllers/ApiPdfPresController.cs`) arma la
  cláusula `WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1{filtro}` — `{filtro}` viene de
  `FiltroVWPedidos()`, que a su vez usa `ApiPresupuestosController.BuildIn(...)`, un helper que
  devuelve el texto **sin espacio inicial** (`"AND columna IN (...)"`). Al no haber ESPACIO ni
  salto de línea entre `@d1` y `{filtro}` en esa línea en particular, cuando el filtro de Canal
  resultaba en `"AND ra.CanID IN (...)"`, el SQL final quedaba literalmente
  `...@d1AND ra.CanID IN (...)` — y SQL Server, al no haber ningún separador, lee `@d1AND` como
  el nombre de UN SOLO parámetro (los nombres de parámetro son alfanuméricos contiguos), en vez
  de el parámetro `@d1` seguido de la palabra clave `AND`. Solo ocurría cuando el filtro
  agregado a `FiltroVWPedidos` era el PRIMERO en tener contenido (Canal, en este caso, ya que
  Centro/División/Supervisor/Vendedor/Suplidor/Categoría estaban en "Todos" — vacíos, sin
  aportar texto) — con cualquier filtro previo no vacío el problema quedaba oculto porque
  `BuildIn` siempre TERMINA en `)`, que sí es un separador válido para SQL Server.
- **Fix:** un espacio antes de `{filtro}` en esa línea (`@d1 {filtro}` en vez de `@d1{filtro}`).
  Cambio de una sola línea.
- **Verificación:** se reprodujo el error EXACTO contra la base de producción real (mismo
  mensaje `Must declare the scalar variable "@d1AND"`) con una consulta mínima replicando el
  patrón roto, confirmando el diagnóstico antes de aplicar el fix; luego se ejecutó la consulta
  real de `CalcularCalendarios30Dias` YA CORREGIDA, con 5 Canales de ejemplo (mismo escenario
  del reporte del usuario) contra producción — 34 filas, sin error. `dotnet build` limpio (0
  errores).
- **Auditoría de precaución:** se revisaron TODOS los demás usos de `BuildIn(...)` en el sistema
  (~30 sitios, principalmente en `ApiPresupuestosController.cs`) para descartar el mismo patrón
  — en todos los demás casos el resultado se inserta en su propia línea dentro de un bloque de
  texto SQL verbatim (el salto de línea actúa como separador) o con un espacio explícito antes,
  así que este era el único punto realmente roto.

## v1.9.0 — 2026-09-04 · Reportes Programados (multi-hora/Duplicar), Cierre Diario, AMCO-GT Mayorista, tema claro de marca, Presupuestos — Gestión, PDF Presentaciones, y varios fixes reales (zona horaria, Torre "atendida", filtros lentos)

Release grande — acumula todo lo construido y verificado desde v1.8.2 (2026-09-03) hasta hoy,
son 13 sesiones de trabajo sin liberar todavía. Se agrupa por área para que sea más fácil de
repasar antes de instalar.

### Reportes Programados y Cierre Diario (Supervisión Dinámica → PDF Presentaciones)
- **Nuevo módulo "Cierre Diario"** (`/supervision` → 📑 PDF Presentaciones → Cierres → Diario):
  reporte de Ventas+Activaciones del día vs. presupuesto (Centro/Canal/Supervisor+Vendedor/
  Suplidor con Categoría anidada y subtotal), 2 calendarios de 30 días (Ventas RD$ y
  Activaciones, comparados contra el mismo día de la semana anterior), Resumen Ejecutivo con
  mejor/peor Centro/Canal/Supervisor y hallazgos de Suplidor/Categoría, y "Pensamiento del día"
  (banco local de 122 frases sembradas, rotación por barajado Fisher-Yates sembrado por año —
  no se repite antes de ~122 días, sin depender de ninguna API externa). Exporta a PDF (mismo
  motor de paginación que "Ventas Proyectadas") y se puede guardar/reabrir en historial.
- **"Reportes Programados"**: cualquier reporte (Cierre Diario o Ventas Proyectadas) con
  cualquier combinación de filtros se puede guardar como una programación propia — nombre,
  destinatarios propios (libreta de correos compartida), y **una o varias horas de envío
  separadas por coma** (ej. `10:00,12:00,15:00,17:15` — una misma programación puede enviarse
  varias veces al día). Pantalla "📅 Reportes Programados" lista todas, con el resultado del
  último envío visible (✓ hora en verde / ✗ motivo en rojo / "Nunca se ha enviado"), toggle
  Activo, Editar, 🚀 Enviar ahora por fila, y **🗐 Duplicar** (clona nombre/horas/destinatarios de
  una programación existente, dejando los filtros abiertos en pantalla para ajustar solo lo que
  cambia — p.ej. el Supervisor — antes de guardar la copia). Además, **"📧 Enviar por correo"**
  manda por correo, ahora mismo, exactamente lo que está en pantalla a direcciones sueltas, sin
  necesidad de guardar nada antes. El envío automático corre cada minuto en segundo plano; el
  disparo real (automático o manual) siempre queda registrado con éxito/error visible.
- SMTP configurado con un correo dedicado (`Amcoreportes@gmail.com`, no el personal). WhatsApp
  queda con el intento aislado (nunca bloquea el correo) pero sin proveedor real conectado
  todavía.
- Confirmado por TI: las tablas nuevas (`ReportesProgramados_RM`,
  `ReportesProgramadosDestinatarios_RM`, `CierreDiarioDestinatarios_RM`, `PdfPresHistorial_RM`,
  `PensamientoDelDia_RM`+banco) ya existen en producción — el módulo queda 100% funcional en BD.
- **Pendiente de probar en vivo por el usuario** (Gerencia): guardar una programación con varias
  horas y confirmar que las 4 se disparan el mismo día, "🗐 Duplicar" de punta a punta, y el envío
  ad-hoc desde ambos reportes.

### Tema claro de marca en todo el sistema (excepto Torre de Control)
- Las 10 vistas del sistema (Productividad, Geo, Menú, Home, Históricos, Seguimientos, Login,
  Supervisión Dinámica, Presupuestos, PresupuestoGestion) pasaron a un tema **claro** con los
  colores institucionales (azul/verde de marca, muestreados en vivo del logo real de la
  empresa) — decisión explícita del usuario, con **Torre de Control excluida a propósito**
  (pantalla de vigilancia, se queda en su tema oscuro). De paso corrigió 2 bugs reales
  encontrados en Cierre Diario: una tabla que se veía negro-sobre-negro (heredaba una variable
  de color que seguía en modo oscuro) y el calendario de 30 días que mostraba "undefined" en
  todas las celdas (bug de serialización JSON — un `record` con propiedades en PascalCase se
  serializaba tal cual porque el sistema tiene desactivado el camelCase automático).
- **Rollback parcial (2026-09-04):** Home (Dashboard de Visitas) y Productividad volvieron al
  tema OSCURO — pedido explícito del usuario ("los colores de marca solo resaltan en negro"),
  quedan igual que Torre de Control. El resto de las vistas retemadas siguen en claro.
- **Pendiente:** el usuario debe revisar visualmente cada dashboard detrás de login — la
  conversión se verificó a fondo por código (incluyendo estilos inline y templates de JS que un
  primer barrido automático no cubría) pero no se pudo ver renderizada en pantalla real.

### Otros fixes reales encontrados y corregidos
- **Bug de zona horaria: "hoy" saltaba al día siguiente después de las 8pm.** ~30 lugares en 7
  vistas calculaban "la fecha de hoy" con `Date.toISOString()`, que convierte a UTC antes de
  formatear — República Dominicana es UTC-4 fijo (sin horario de verano), así que después de las
  8:00 PM esa conversión ya "es mañana". Afectaba fechas por defecto en Cierre Diario, Torre,
  Home, Productividad, Históricos, Seguimientos (IEGT) y nombres de archivo de exportación en
  Geo. Corregido con un formateador local (respeta la zona horaria del navegador) en las 7
  vistas, confirmado en vivo con la hora real del sistema.
- **Filtros lentos en Cierre Diario** ("dura mucho cargando los filtros"): el catálogo compartido
  (Suplidores/Categorías/Centros/Canales/Supervisores/Divisiones/Vendedores, `GET
  /apipresupuestos/filtros`) se recalculaba en cada carga de página (~740ms de SQL, medido en
  vivo) aunque casi nunca cambia en el día. Ahora se cachea 1 hora (mismo mecanismo ya usado para
  las curvas de estacionalidad de Presupuestos) — primera carga del día igual de rápida, todas
  las siguientes prácticamente instantáneas.
- **Presupuestos — "Del Día" ya no borra lo que se tenía en pantalla.** El refresco automático
  cada 2 minutos reiniciaba el nivel de drill (Centro/Supervisor/Vendedor) a "Compañía" y la
  vista a "Por Organización" cada vez, perdiendo dónde estaba parado el usuario. Ahora el
  refresco automático solo trae datos nuevos y respeta el drill/vista en pantalla; solo una
  consulta manual (cambiar filtros a propósito) reinicia la navegación.
- **Torre de Control — alertas "atendidas" que se reactivaban solas.** El checklist del modal de
  contacto (marcar cuáles alertas se atendieron) ya guardaba bien el dato, pero el motor de
  evaluación en vivo nunca lo leía de vuelta — solo tenía una regla genérica para UNA alerta
  ("1er pedido"). Ahora cualquier alerta marcada "atendida" en un contacto queda apagada el resto
  del día para ese vendedor (con un indicador visual "✓ atendida" en su celda), sin afectar las
  demás alertas del mismo vendedor.
- **Históricos de Ventas → Ped-Fact-NC: nuevo nivel "Por Cliente".** Lista, uno por uno, los
  clientes que hicieron pedidos (Centro → Supervisor → Vendedor → **Cliente**), con **CliID**
  siempre visible junto al nombre — para que dos clientes con el mismo nombre nunca se confundan
  ni se solapen entre sí.
- Todos verificados con `dotnet build` limpio y `node --check` sobre el JS embebido; los que
  tocan lógica de negocio además se probaron con datos reales (SQL directo contra producción o
  ejecución del código real compilado, según el caso) — el detalle está en `PROGRESS.md`
  (sesiones 2026-09-03/04, partes 3ª a 15ª). **Los últimos 3 (Del Día, Torre atendida, nivel
  Cliente) están pendientes de confirmación del usuario en el navegador en vivo.**

### AMCO-GT (Seguimientos/IEGT): metodología separada para Mayorista + rampa lineal + renombres
- **Las notas de los 7 sub-indicadores originales pasaron de escalones planos** (ej. "antes de
  las 8:20 = 100%, si no 50%") **a una rampa lineal continua** entre los mismos umbrales ya
  configurados — un vendedor que empieza a las 8:21 ya no saca la misma nota que uno de las 8:30.
  De paso, "Efectividad de visitas" se renombró a **"Cumplimiento de Ruta"** en toda la pantalla
  (tablas, config, exportación a Excel).
- **Nueva metodología separada para el Canal Mayorista** (CanID=3000) — estos vendedores no
  encajan en las reglas de Minorista/Prevendedor (visitas más largas, pedidos fuera de ruta,
  casi no digitan en PDV). 5 indicadores propios, con peso fijo: Hora de Inicio (30 pts, ≤8:30
  gana todo, pierde todo después de 9:00), Cumplimiento de Ruta (30 pts, 100% o más gana todo,
  menos del 80% lo pierde todo), Cierre (15 pts, 4:30pm o más tarde gana todo, antes de 3:30pm lo
  pierde todo), Presupuesto de Ventas (15 pts, ≥98% del presupuesto diario gana todo, <80% lo
  pierde todo) e Inventario en clientes visitados (10 pts, al menos 25% de los clientes del día
  con inventario registrado en `VwInventarioFisico`/`VwInventarioFisicoDetalle`).
- **Perfil Minorista/Prevendedor reestructurado**: se quitó "Consistencia PDV" (redundante con
  "Disciplina de ruta") y se agregó **"Logro Pre Ventas"** (100% desde 99% de logro, deteriora
  hacia 0 por debajo de 90%); "Ritmo de Visitas" se reemplazó por **"Tiempo en Cliente"** (0% si
  dura menos de 3 minutos con el cliente, 100% si dura 6 minutos o más) — ambos cambios
  exclusivos de Minorista/Prevendedor, el canal Mayorista tiene su propio set de reglas (arriba).
- Los grupos (Centro/Supervisor) con vendedores de ambos canales muestran "canales mixtos" (se
  promedia el IEGT ya calculado de cada uno, la única excepción documentada a la regla de
  "recalcular desde las métricas crudas, nunca promediar IEGTs").
- **Pendiente de confirmar con el usuario:** el filtro de División por defecto de Seguimientos
  (`['10','11']`, preventa) puede estar excluyendo a los vendedores Mayorista si su RepTipe real
  es otro — no se determinó ese RepTipe en esta sesión. El piso de 90% de "Logro Pre Ventas" fue
  una suposición (el usuario solo confirmó el techo de 99%). El modo "Por Suplidor" sigue con el
  motor viejo, no se extendió al nuevo sistema de perfiles. Nada de esto se probó todavía en el
  navegador con datos reales — solo con un harness de Node.js ejecutando las funciones de puntaje
  reales contra datos sintéticos.

- **Nuevo: "PDF Presentaciones" en Supervisión Dinámica** (`/supervision`, tab 📑 nuevo) —
  entorno para generar informes descargables en PDF para enviar a equipo/jefe/suplidor.
  Pantalla de bienvenida + 5 categorías (Informes Matinales, Resumen Semanal del Supervisor,
  **Informes Gerenciales**, Gobernanzas con Suplidores, Cierres diarios/Semanales/Mensuales) —
  todas "en desarrollo" salvo Informes Gerenciales, único punto donde arrancó el desarrollo real
  (pedido explícito del usuario 2026-08-30). Dentro: submenú estilo `/menu` (tarjetas con
  ícono) con 5 opciones (Gobierno Comercial, Reunión Gerencial, RPS Supervisores, RPS/RPM
  Suplidores, RPS/RPM Vendedores) — solo "Gobierno Comercial" activo, con 3 informes listados
  (solo "Ventas Proyectadas próxima semana" implementado; "Alcance del presupuesto al día" y
  "Estatus Pedidos" quedan "en desarrollo").
  **Ventas Proyectadas próxima semana**: filtros de rango de fechas + Suplidor/Categoría/Canal/
  Centro/Supervisor/Vendedor (uno, varios o todos), reusando `/apipresupuestos/filtros` y el
  widget `msel()` ya existente en Supervisión. Proyecta, para un rango FUTURO, qué parte del
  presupuesto ya cargado cae en ese rango según la curva de estacionalidad de Presupuestos
  (diferencia de la curva acumulada entre el día-hábil de inicio y fin del tramo — reuso nuevo
  de esa curva, antes solo se usaba para "Estimado" del mes en curso). Informe: resumen
  ejecutivo (Facturas/Cajas/Monto), desgloses por Centro/Canal/Supervisor, detalle cruzado
  Suplidor×Categoría por Centro (solo Monto+Cajas — Facturas no existe a nivel de Producto,
  misma limitación ya documentada en Presupuestos), franja de encabezado con los colores del
  logo (muestreados en vivo de `Empresa.EmpLogo`), y cierre "Preparado por: Rudy Mercedes —
  Gerente". **Descargar PDF**: reusa `html2canvas`+`jsPDF` ya vendorizados (mismo patrón que
  Presupuestos) pero con paginación propia que agrega pie de página (filtros + nombre del
  informe) y numeración en CADA hoja. **Historial**: "Guardar en historial" persiste
  filtros+resultado (JSON) en la nueva tabla `PdfPresHistorial_RM` (**SQL/PdfPresHistorial_RM.sql
  aún NO corrido por TI** — sin ella, generar/descargar el PDF funciona igual, solo falla
  "Guardar en historial"); "Ver historial" permite reabrir un informe pasado sin recalcular.
  Backend nuevo: `Controllers/ApiPdfPresController.cs` — reusa (ahora `internal static` en vez
  de `private`, cero cambio de comportamiento) la lógica de cruce Suplidor↔Categoría↔Producto
  ya depurada en `ApiPresupuestosController` (`ResolverReps`/`BuildRefFiltro`/`ParseList`/
  `BuildIn`). **Pendiente de verificar con datos reales**: compilado limpio y probado en vivo
  solo a nivel de ruteo (sin sesión de usuario disponible en esta sesión); el usuario debe
  correrlo él mismo y confirmar que los números cuadran antes de considerarlo "probado".

- **Nuevo módulo "Presupuestos — Gestión"** (`/presupuestos/gestion`, solo Rol='Gerencia' vía
  `SupervisionUsuarioMapa_RM`, mismo mecanismo que Supervisión Dinámica): generar, revisar y
  publicar presupuestos de venta (v1: solo `PreTipo='Unddprod'`). Flujo staging (`PresupuestoLotes_RM`
  + `PresupuestoStaging_RM`) → revisión con diff Propuesto/Actual y anomalías → Publicar (único
  paso que escribe a `PresupuestosHistoricos`). Motor nuevo `Services/PresupuestoBuilderCore.cs`:
  universo de productos por vendedor vía `RepresentantesGrupoProductos`→`GrupoProductosDetalle`
  (resuelve clientes con 2+ vendedores de divisiones distintas sin ambigüedad), base histórica neta
  desde `VwPedidosVsFacutadosMenosDevolucionRM` reatribuida a la ruta vigente (`RutaVisitas`), y
  variables configurables (`PresupuestoVariables_RM`/`PresupuestoVariablesValores_RM`: factor de
  crecimiento, meta de rappel, piso mínimo, factor ruta×proveedor) con resolución en cascada
  Mes>Trimestre>Año / RutaSuplidor>Suplidor>Global. **Pendiente:** PreTipo `UNDDLINACT`
  (activaciones→reparto de unidades entre SKU de la categoría), pantalla de mantenimiento de
  variables, edición directa de lo ya publicado con auditoría. Ver plan completo:
  `~/.claude/plans/bubbly-spinning-blum.md`. Tablas nuevas creadas vía
  `SQL/PresupuestosGestion_RM.sql` — **ya corrido y verificado** en `MovilBusiness5STD_Test`.
  **Dos bases**: lee de `MovilBusiness5STD` (producción, vía nombres 3-partes) y escribe en
  `MovilBusiness5STD_Test` (config `PresupuestosOrigenDb`/`PresupuestosDestino` en
  `appsettings.json`) — cuando se consolide en una sola base, es 1 línea de config, no tocar código.
  **Ajuste del lote antes de publicar** (pantalla "2. Revisar, ajustar y publicar"): editar un valor
  propuesto en pantalla (clic en la celda), quitar una fila, agregar una fila manual (Rep+Producto+
  Año-Mes+valor), y **exportar/reimportar por Excel** (SheetJS, mismo patrón que Históricos de
  Ventas — reimporta por columna `Id`, no duplica ni reordena filas). Solo disponible mientras el
  lote está en estado `Borrador` (bloqueado tras Publicar/Descartar).
- **Bug de rendimiento corregido (2026-08-30):** "Generar" resolvía cada variable (factor de
  crecimiento/piso/ruta) con 1 consulta SQL por fila x mes — con la base completa (cientos de
  reps x miles de productos) eso eran decenas de miles de idas y vueltas a la BD en una sola
  request; el usuario reportó una generación colgada 10+ minutos (confirmado en vivo: sesión SQL
  activa, sin bloqueo, solo miles de SELECT chiquitos en fila). Ahora la tabla de variables (chica,
  un catálogo de configuración) se carga una sola vez y la cascada se resuelve en memoria; el
  insert masivo a `PresupuestoStaging_RM` además quedó envuelto en una sola transacción (antes cada
  fila hacía su propio commit). `Services/PresupuestoBuilderCore.cs`: `ResolverVariable` reemplazado
  por `CargarVariable` + `ResolverVariableEnMemoria`.
- **Config del motor movida a PRODUCCIÓN**: `PresupuestoVariables_RM`/`PresupuestoVariablesValores_RM`
  ya NO se leen/escriben en `_Test` — viven en `MovilBusiness5STD` (el usuario ya las había creado
  ahí también, verificado en vivo). Solo `PresupuestoLotes_RM`/`PresupuestoStaging_RM`/
  `PresupuestoPublicaciones_RM` y el `PresupuestosHistoricos` final siguen en `_Test` mientras dure
  la prueba. Verificado en vivo que el login `Ventas` puede hacer INSERT/UPDATE/DELETE cross-database
  desde la conexión de destino hacia producción (mismo servidor).
- **Pantalla ⚙️ "Configuración de variables/metodologías"** (nuevo modal en `/presupuestos/gestion`):
  ver/editar/crear definiciones de variable (catálogo abierto — Gerencia puede agregar una
  metodología nueva sin tocar código ni tablas a mano) y, por variable, ver/editar/agregar/borrar sus
  valores (ámbito Suplidor/Categoría/RutaSuplidor/Global x periodo Año/Trimestre/Mes). Endpoints
  nuevos bajo `apipresupuestogestion/variables/...`.
- Fix de UX: "Destino" pasó de una lista libre separada por comas (fácil de escribir mal — un
  usuario escribió "2026-09,2026-12" queriendo decir el rango completo Sep-Dic y solo generó esos 2
  meses, saltándose Oct/Nov sin avisar) a 2 selectores "desde/hasta" que arman el rango completo.
- **Bug corregido: JSON en camelCase vs. PascalCase.** Las pantallas nuevas mostraban "undefined" en
  todas las celdas — la causa: `Program.cs` fija `PropertyNamingPolicy = null` a propósito en TODO el
  proyecto ("el JS espera RepCodigo, no repCodigo"), pero el JS de estas pantallas nuevas se escribió
  asumiendo camelCase. Corregido en las 3 pantallas (tabla del lote, exportar Excel, configuración de
  variables) para usar los mismos nombres PascalCase que ya devuelve el backend.
- **Fuente de la base histórica cambiada a `VWVentas`+`VWVentasDetalle`** (pedido explícito del
  usuario 2026-08-30, en vez de la vista `VwPedidosVsFacutadosMenosDevolucionRM` — esa vista resultó
  ejecutar por dentro un fragmento carísimo ligado a integración SAP, causa real de la corrida de 7+
  minutos). Confirmado en vivo que **ya es neta sin restar nada**: `VWVentasDetalle` trae las
  devoluciones como filas con `VenCantidad` NEGATIVO mezcladas con las ventas positivas — un `SUM()`
  simple ya las netea. Prueba real: 5.7s para 3 meses (antes 7+ minutos).
- **Reps evaluados acotados a los "activos"**: sin filtro explícito de vendedor, "Generar" ya NO corre
  para los ~209 reps del catálogo — solo los que tuvieron venta en el rango base o están en
  `RutaVisitas` (`PresupuestoBuilderCore.ResolverRepsActivos`, ~90 reps en la prueba real en vez de 209).
- **ARQUITECTURA DE BASES INVERTIDA (2026-08-30, aclaración del usuario):** la conexión principal del
  controller pasó de "destino" (Test) a **"Default" = producción**. Ahora TODO — data de referencia,
  configuración/metodología (`PresupuestoVariables_RM`/`Valores_RM`) Y las tablas de trabajo de cada
  propuesta (`PresupuestoLotes_RM`/`Staging_RM`/`Publicaciones_RM`, ya creadas también en producción,
  verificado en vivo) — vive y se opera en `MovilBusiness5STD`. **Únicamente** el INSERT/UPDATE/DELETE
  final a `PresupuestosHistoricos` (el botón Publicar) se referencia con 3 partes hacia
  `MovilBusiness5STD_Test` (config `PresupuestosHistoricosDb` en `appsettings.json`, ya no
  `PresupuestosOrigenDb`/`PresupuestosDestino`) — verificado en vivo el cross-database INSERT/DELETE en
  ambos sentidos. Cuando se consolide todo en una sola base, esa config pasa a "" y no hay que tocar
  el SQL de `PresupuestoBuilderCore.cs` ni del controller.
- **Bug de rendimiento corregido (2026-08-30, definitivo):** con todo lo anterior ya arreglado,
  "Generar" seguía colgándose varios minutos. Se agregó logging paso a paso (`Console.WriteLine`,
  visible en la ventana de `CORRER_LOCAL.bat`) que permitió clavar el punto exacto: TODO el método
  corría en ~3 segundos (reps activos, universo de productos, base histórica de 6,307 filas,
  variables, snapshot de actual) y se colgaba minutos enteros justo en el bucle final, que hacía
  `ExecuteNonQuery()` **una fila a la vez** para insertar en `PresupuestoStaging_RM` — hasta ~25,000
  inserts individuales (6,307 filas x 4 meses destino). Reemplazado por **`SqlBulkCopy`**: arma toda
  la propuesta en un `DataTable` en memoria y la sube al servidor de una sola vez — miles de filas en
  menos de 1 segundo, sin importar cuántas sean. El log de diagnóstico queda en el código (no estorba,
  solo imprime en la consola de desarrollo) para poder ver el detalle de cualquier corrida futura.
- **Nueva pantalla "3. Análisis comparativo"** (drill-down General → Centro → Canal → Supervisor →
  Vendedor, pedido explícito del usuario 2026-08-30): tarjetas con Unidades/Cajas/RD$ (RD$ estimado
  con el precio promedio del Rango Base, vía `VWVentas` — el presupuesto en sí solo guarda unidades,
  no requiere cambio de esquema) comparando Propuesto vs. una base elegible: **mismo período** (lo ya
  publicado, `PrePresupuestoActual`) o **un Año-Mes específico** (`PrePresupuesto` de ese mes en
  producción — al comparar contra 1 solo mes, el Propuesto se normaliza a promedio mensual para que
  sea justo). Nuevo endpoint `GET apipresupuestogestion/lote/{id}/comparativo`.
- **"Análisis comparativo" mejorado (2026-08-30, pedido explícito del usuario tras revisar los
  primeros resultados):**
  - **Comparación en "Generar"**: se agregó un campo opcional "Comparar contra (Año-Mes)" al
    generar el lote — si se indica, todo el análisis compara contra **el presupuesto que se
    fijó ese mes específico** en producción (`ObtenerPresupuestoActual` con `compAnio/compMes`),
    no contra el ejecutado real (aclarado explícitamente por el usuario vía pregunta directa:
    "Presupuesto que se fijó ese mes"). Sin ese campo, sigue comparando contra el mismo período
    ya publicado (comportamiento anterior).
  - **Monto total sugerido con redistribución proporcional**: nuevo campo opcional en "Generar".
    El motor calcula primero el propuesto "natural" de cada fila (sin este ajuste), y si el
    usuario da un monto total, escala TODAS las filas por el mismo factor
    (`monto total ÷ suma natural`) antes de insertar — mantiene la proporción relativa entre
    reps/productos que ya calculó el motor, solo re-encuadra el total. La nota queda registrada
    en `Descripcion` del lote ("Redistribuido a total=X").
  - **Nueva métrica "Ventas Promedio"**: además de Propuesto y Comparación, `Comparativo` ahora
    también devuelve el promedio mensual de venta neta del Rango Base (recalculado con
    `CalcularBaseHistorica`) por rep×producto — para poder responder la pregunta del usuario
    ("¿por qué el presupuesto es menor si estamos vendiendo más?") con las 3 cifras una al lado
    de la otra en vez de adivinar.
  - **Tabla de comparación rediseñada a 5 columnas** (pedido exacto del usuario): Presupuesto del
    mes a comparar / Ventas Promedio / Presupuesto Sugerido / Variación % vs Mes / Variación %
    vs Promedio — separadas por tipo de métrica (selector Unidades/Cajas/RD$ arriba de la tabla,
    en vez de las 3 juntas) para no amontonar la lectura. Mismo drill-down General→Centro→
    Canal→Supervisor→Vendedor de antes, ahora recalculado para cada métrica seleccionada.
  - Cambios en `Services/PresupuestoBuilderCore.cs` (`ObtenerPresupuestoActual` ahora acepta
    año/mes explícitos), `Controllers/ApiPresupuestoGestionController.cs` (`GenerarReq` con
    `compAnio`/`compMes`/`montoTotalSugerido`; `Generar` reestructurado en dos pasadas — calcula
    todo en memoria primero, luego aplica la escala si corresponde, y recién ahí abre la
    transacción e inserta; `Comparativo` agrega `ventaPromedio`) y
    `Views/PresupuestoGestion/Index.cshtml` (inputs nuevos en "1. Generar propuesta"; JS de
    "3. Análisis comparativo" reescrito: `METRICAS`, `cambiarMetrica`, `filaHtml`, `sumaGrupo`
    ampliado a 9 campos). Compilado limpio (`dotnet build` 0 errores/advertencias).
  - **Pendiente de probar en vivo**: el usuario debe reiniciar `CORRER_LOCAL.bat` y generar un
    lote nuevo con "Comparar contra" + "Monto Total Sugerido" para confirmar en pantalla que
    las 5 columnas y el selector de métrica se ven y calculan como se espera.
- **Ronda grande de correcciones/mejoras (2026-08-30, tras revisar el análisis anterior en pantalla)**,
  pedido explícito del usuario en 1 solo mensaje con varios problemas de fondo:
  - **Fix "peras con peras"**: en `Comparativo`, cuando el lote cubre VARIOS meses destino, "Mismo
    período (ya publicado)" se dejaba como SUMA de los N meses mientras Sugerido/Ventas Promedio ya
    estaban en promedio mensual — la variación % salía completamente inflada. Ahora ambos lados se
    promedian por `nMesesDestino` (salvo el caso "mes específico", donde el lado Comparación ya es
    de 1 solo mes y no se vuelve a dividir).
  - **Distribución mensual por días hábiles**: antes `Generar` aplicaba el MISMO promedio mensual
    plano a los 4 meses destino por igual. Ahora se calcula "unidades por día hábil" del Rango Base
    (`PresupuestoBuilderCore.DiasHabilesEnRango`, reusa `PresupuestosCore.DiasHabiles` — no se duplica
    esa lógica) y cada mes destino recibe unidades proporcionales a SUS propios días hábiles (Oct=27
    lleva más que Nov=25, a igual venta diaria).
  - **Filtro de Canal + exclusión de Institucional**: `GenerarReq.canales` (opcional) — sin esto,
    default = todos los canales MENOS Institucional (CanID=7000, misma convención ya usada en
    Presupuestos/Historicos — `PresupuestoBuilderCore.FiltrarRepsPorCanal`). Se aplica ANTES de
    calcular universo/base histórica, así ningún rep de ese canal entra siquiera al cálculo.
  - **Objetivo total en Valor (RD$)**: nuevo `montoValorSugerido` junto al ya existente
    `montoTotalSugerido` (unidades) — convierte el objetivo en RD$ a la misma "escala" proporcional
    usando el precio promedio del Rango Base (`PresupuestoBuilderCore.PrecioPromedioPorProducto`,
    extraído de `Comparativo` para reusarlo en los dos lugares).
  - **META_RAPPEL wireado al motor** (antes existía la variable en el catálogo pero nunca se
    consumía): cuando hay una meta de rappel configurada para un Suplidor/mes, esa meta ÷ el valor
    promedio mensual (RD$) que hoy vende ese Suplidor REEMPLAZA el `FACTOR_CRECIMIENTO_MES` normal
    para las filas de ese Suplidor en ese mes.
  - **`Comparativo` a grano Rep×Producto** (antes colapsaba a nivel Rep, perdiendo toda dimensión de
    producto): ahora cada fila trae también `ProId/ProDescripcion/Cat1ID/Cat1Nombre/Cat3ID/Cat3Nombre`
    — necesario para poder drillear también por Suplidor/Categoría/Producto, no solo por Centro/Canal/
    Supervisor/Vendedor.
  - **"3. Análisis comparativo" rediseñado a 3 cadenas apiladas** (pedido explícito: "si paso de
    compañía a Centro solo puedo ver los del centro X no ambos a la vez"): **Por Centro**
    (Compañía→Centro→Supervisor→Vendedor→Producto), **Por Canal** (→Canal→Supervisor→Vendedor→
    Producto), **Por Suplidor** (→Suplidor→Categoría→Producto) — cada nivel es su PROPIA tarjeta que
    se apila debajo de la anterior (nunca desaparece), con "‹ Ver todos" para volver sin perder de
    vista lo de arriba. Mismo patrón ya validado por el usuario en `Views/Presupuestos/Index.cshtml`
    (`renderCadenaCentro/Canal/Suplidor`), adaptado a tabla de 5 columnas en vez de gráficos.
  - **Redistribución en cualquier nodo del drill** (pedido explícito, valor RD$ o %, reparto
    proporcional): botón "🎯 Sugerir para todo" en cada tarjeta de las 3 cadenas y en la tarjeta
    General — nuevo endpoint `POST apipresupuestogestion/lote/{id}/ajustar-nodo` (`ambito` =
    combinación de Centro/Canal/Supervisor/Rep/Suplidor/Categoría/Producto, `modo` 'valor'|'pct',
    `monto`) — reescala SOLO las filas de staging dentro de ese ámbito (excluyendo siempre
    Institucional salvo que el ámbito pedido sea ese canal), recalcula anomalías fila por fila, y dej
    a una nota en `Descripcion` del lote. Solo disponible mientras el lote está en `Borrador`.
  - **Activación mínima Obligatoria/Ideal por Suplidor×Categoría** (pedido explícito, respondiendo
    "no sé dónde entraría el tema de las activaciones mínimas por suplidor"): nueva tabla
    `SQL/PresupuestoActivacionMetas_RM.sql` (**AÚN NO corrida por TI** — `Ventas` no tiene permiso de
    `CREATE TABLE`, mismo patrón que las demás tablas `_RM`) con Cat3ID/Cat1ID/MetaPct/Modo
    ('Ideal'|'Obligatorio')/BloqueaPublicar. Decidido con el usuario (`AskUserQuestion`): **Obligatorio**
    por defecto FUERZA el piso del presupuesto (`PresupuestoBuilderCore.ClientesActivosPorCategoria` +
    `CarteraPorRep` calculan el % real de clientes activos por Rep×Categoría; si queda debajo de la
    meta, el gap se convierte a unidades vía el promedio unidades/cliente activo del grupo y se
    reparte entre sus SKU según el peso actual) y opcionalmente (`BloqueaPublicar`, NO es el default)
    también bloquea `Publicar` si ni forzando se alcanza — con override explícito (`forzar=true`) para
    que Gerencia pueda publicar igual. **Ideal** nunca fuerza ni bloquea. Nueva pestaña en el ⚙️
    modal (`activacion/metas` CRUD) para mantener las metas.
  - **Semilla de configuración real** (confirmado con el usuario vía `AskUserQuestion`, YA corrida en
    producción): `SQL/PresupuestoSeedCrecimiento_RM.sql` cargó en `PresupuestoVariablesValores_RM`
    las curvas de crecimiento mensual Sep-Dic 2026 por Suplidor (`FACTOR_CRECIMIENTO_MES`, Cat3ID
    verificados en vivo: 101=Continental, 978=Molinos, 201=Brinsa, 106=Unilever, 954=Colombina) y las
    metas de rappel (`META_RAPPEL`, PeriodoTipo='Anio') del archivo entregado por el consultor externo
    (`Presupuestos Sugerido Septiembre - Diciembre/modelo_params.py`) — 24 filas insertadas y
    verificadas. Quedan editables/borrables desde el ⚙️ como cualquier otro valor. **Fuera de esta
    semilla** (no encajan en el modelo actual, AmbitoTipo por Suplidor/Categoría/RutaSuplidor/Global,
    no por Canal/Fuerza de venta): `FAC_MAYOR`/`FAC_FERNANDO` del archivo original.
  - **Compilado limpio** (`dotnet build DashSmart.csproj -c Release`, 0 errores/advertencias) y JS
    verificado con `node --check` en cada paso — **sin probar aún en el navegador** (no hubo sesión
    de usuario disponible esta ronda). Pendiente que el usuario reinicie `CORRER_LOCAL.bat` y pruebe:
    generar con filtro de canal + objetivo en Valor, las 3 cadenas del análisis comparativo, el botón
    "🎯 Sugerir para todo" en un nodo cualquiera, y (una vez que TI corra
    `PresupuestoActivacionMetas_RM.sql`) cargar una meta Obligatoria y confirmar que fuerza el número.
- **Fix del RD$ en "3. Análisis comparativo" — dejó de ser un estimado (2026-08-31).** El usuario
  reportó que el monto de las tarjetas "no cuadra con el número real" — investigado y corregido de
  raíz: el RD$ se calculaba con un precio PROMEDIO de ventas del Rango Base, cuando lo correcto (según
  indicó el usuario) es reproducir el proceso real que arma `PreTipo='MNTDPROD'`: **unidades × el
  precio de la Lista de Precios que corresponde al canal/lista de CADA vendedor**
  (`RepresentantesAdicionales.LipCodigo` → `ListaPrecios.LipCodigo+ProID` → `LipPrecio`) — dos
  vendedores con el mismo producto pero distinto canal (ej. Minorista vs. Mayorista) llevan precios
  distintos, por eso el precio se resuelve por (Rep,Producto), no solo por Producto. **Verificado en
  vivo contra producción, agosto 2026**: de 6,578 filas Rep×Producto, 0 discrepancias (más allá de
  redondeo) entre `unidades(UNDDPROD) × este precio` y el `MNTDPROD` ya guardado — reproduce EXACTO
  el monto real. Cambios:
  - **"Presupuesto [mes]" (columna Comparación)**: ya no se calcula — se trae DIRECTO el `MNTDPROD`
    ya publicado en producción para ese Rep×Producto (mismo período o mes específico, con el mismo
    promedio-por-N-meses del fix "peras con peras" cuando aplica).
  - **"Presupuesto Sugerido" y "Ventas Promedio" (en RD$)**: siguen siendo unidades × precio, pero
    ahora con el precio de Lista REAL por vendedor en vez de un promedio de ventas — más preciso y
    consistente con cómo la empresa realmente valora el presupuesto en dinero.
  - `Controllers/ApiPresupuestoGestionController.cs` (`Comparativo`): nueva resolución de
    `RepresentantesAdicionales.LipCodigo` por rep, `ListaPrecios` por (LipCodigo,ProID), y consulta
    directa a `PresupuestosHistoricos WHERE PreTipo='MNTDPROD'` (siempre contra producción, sea cual
    sea `PresupuestosHistoricosDb` — MNTDPROD no lo genera este módulo). `PrecioPromedioPorProducto`
    (el estimado anterior) queda sin usar aquí, pero se mantiene en `PresupuestoBuilderCore` porque
    `Generar` todavía lo usa para convertir el "Objetivo en Valor" a escala de unidades (no se tocó
    esa parte en esta ronda).
  - Compilado limpio (`dotnet build`, 0 errores/advertencias) — **pendiente que el usuario confirme
    en pantalla** que ahora el RD$ de "Presupuesto [mes]" sí cuadra con lo real.
- **Segundo fix el mismo día: el total todavía no cuadraba (2026-08-31).** El usuario pidió validar
  antes de seguir — consulta directa a producción: `MNTDPROD` real de agosto 2026 (excluyendo
  Institucional) = **RD$84,176,109.96** (8,437 filas, 55 reps), pero la pantalla mostraba ~RD$77.8MM.
  Causa real: `Comparativo` solo listaba los pares Rep×Producto que el **motor propuso** (los de
  `PresupuestoStaging_RM`, que salen de la base histórica del Rango Base) — cualquier producto con
  presupuesto real en el mes comparado (o con venta en el Rango Base) que el motor no llegó a
  proponer quedaba **invisible**, aunque sí existiera (verificado: de 8,437 pares reales, solo 6,101
  coincidían con lo propuesto — 2,336 pares, ~RD$6.3MM, quedaban fuera). Corregido: ahora se arma la
  **UNIÓN** de 4 fuentes (`filas`/propuesto, `compEspecifico`/unidades reales, `montoReal`/MNTDPROD
  real, `ventaPromedio`/base histórica) — cualquier par que exista en CUALQUIERA de las 4 aparece en
  el resultado, con `UnidadesPropuesto=0` cuando el motor no lo tocó. Matemáticamente garantiza que
  `Σ MontoComparacion` del resultado = `Σ MontoComparacion` real completo (todo par de `montoReal`
  queda incluido en la unión). `Controllers/ApiPresupuestoGestionController.cs` (`Comparativo`):
  nuevo `todasLasClaves` (HashSet), `propuestoPorPar` (dict), reordenado el cálculo de `montoReal`
  antes de la unión. Compilado limpio.
- **Investigación (sin implementar aún) — reasignación de presupuesto por Vendedor×Categoría a los
  productos que realmente se comercializan.** El usuario detectó, viendo el hueco de arriba, un
  problema de fondo más grave: el presupuesto real de agosto/septiembre a menudo concentra TODO el
  monto de una categoría en un solo producto (a veces uno que ni se vende), en vez de repartirlo
  entre los productos reales — lo cual arruina la comparación real-vs-presupuesto a nivel de
  producto (que es como Gerencia hace seguimiento hoy). Cuantificado en vivo: de 8,437 pares
  Rep×Producto con presupuesto en agosto, **6,562 (78%) no tuvieron NINGUNA venta en mayo-julio
  2026** — la escala del problema es mucho mayor que casos aislados. También se confirmó que los
  "combos" (identificables por `Productos.ProDescripcion LIKE '%COMBO%'`, 285 productos) ya cargan
  un monto prácticamente nulo en `MNTDPROD` (RD$6,058.93 de RD$84MM, agosto Y septiembre — mismo
  valor exacto en ambos meses) — consistente con "los combos solo suman en unidades". No existe un
  campo `Activo`/`Estado` usable en `Productos` (`ProEstado` está NULL en el 100% de los registros) —
  "producto que realmente se comercializa" tendrá que definirse por venta reciente, no por un flag.
  Pendiente: presentar una propuesta formal (algoritmo de reparto, período de referencia, alcance)
  antes de tocar código o datos — modificar `PresupuestosHistoricos` de meses ya publicados es una
  acción de alto impacto que requiere aprobación explícita previa.
- **Nueva herramienta: "4. Rebalancear presupuesto real por Categoría" — construida tras aprobar la
  propuesta.** Reparte el `MNTDPROD`/`UNDDPROD` YA PUBLICADO de un Año-Mes entre los productos que
  cada vendedor REALMENTE vende (referencia fija mayo-julio 2026, decidido con el usuario), dentro de
  cada Vendedor×Categoría — preserva el total de la categoría, excluye combos (no reciben dinero),
  excluye Institucional por defecto. Vendedor×Categoría sin NINGÚN producto con venta real en el
  período → **anomalía**, se deja intacta (no se auto-asigna nada), confirmado en vivo con un caso
  real (rep 36, categoría Agua: 100% del presupuesto en un producto sin ninguna venta en mayo-julio,
  y el rep tampoco vendió NADA más de esa categoría — anomalía correcta). También se validó un caso
  de concentración LEGÍTIMA (rep 102, categoría Refisal/Agua: el producto que sostiene ~97% del
  presupuesto es el mismo que sostiene ~97% de la venta real — el algoritmo no lo toca porque ya
  refleja la realidad).
  - **Flujo obligatorio Vista Previa → Aplicar**: `POST .../historicos/rebalancear-categoria/preview`
    muestra EXACTAMENTE qué cambiaría (por Vendedor×Categoría: monto/unidades antes y después de cada
    producto) sin escribir nada; `POST .../historicos/rebalancear-categoria/aplicar` exige el campo
    `confirmacion="CONFIRMAR"` (literal) — el botón en pantalla queda deshabilitado hasta que el
    usuario escribe exactamente esa palabra, para una acción de este calibre sobre datos reales ya
    publicados.
  - **Excepción deliberada** a la regla "solo Publicar toca `PresupuestosHistoricos`": este endpoint
    escribe DIRECTO a producción (no a la base de prueba) — porque corrige presupuesto YA REAL, no
    una propuesta nuestra. Confirmado con el usuario que el job nocturno que recalcula `MNTDPROD`
    desde `UNDDPROD` usa la MISMA fórmula ya verificada (unidades × Lista de Precios) — el ajuste no
    se revierte solo, ese job simplemente reconfirma lo mismo cada noche.
  - Nuevo en `Services/PresupuestoBuilderCore.cs`: `CalcularPesosReales` (reusa `CalcularBaseHistorica`,
    agrega exclusión de combos vía `ProDescripcion LIKE '%COMBO%'` y agregación de peso por
    Rep×Cat1ID) y `PrecioListaPorRepYProducto` (extraído del cálculo ya usado en `Comparativo`).
  - Nuevos endpoints y helper privado `CalcularRebalanceCategoria`/`UpsertPresupuesto` en
    `Controllers/ApiPresupuestoGestionController.cs`.
  - Sección nueva "4. Herramienta..." en `Views/PresupuestoGestion/Index.cshtml`: selector Año-Mes,
    tabla de grupos con detalle expandible por clic, campo de confirmación de texto.
  - Compilado limpio, JS verificado con `node --check`. **Pendiente que el usuario la use en vivo**
    (empezando por Vista Previa para 2026-08, revisando anomalías, antes de aplicar cualquier cambio).

---

## v1.8.2 — 2026-09-03 · Fix crítico: monitores personalizados de Torre daban 0 en vivo (bug preexistente)

Corrige un bug real encontrado horas después de subir v1.8.1 al servidor (el usuario reportó
"las alertas no se están actualizando ni en el servidor ni local"). **No era un problema de
despliegue ni de la lógica de Ritmo** — se verificó ejecutando el código real compilado
(`TorrePersonalizados.Calcular`) contra producción con `Microsoft.Data.SqlClient` (el mismo
driver de la app, no una réplica manual): la consulta lanzaba
`SqlException: "Each GROUP BY expression must contain at least one column that is not an outer
reference"`, atrapada en silencio por el `try/catch` que rodea `ValoresHoy` — dejando el
diccionario de valores VACÍO, es decir, **0 clientes para todos los vendedores**, todo el día,
para CUALQUIER monitor personalizado (no solo los de Colombina).

- **Causa raíz:** `Services/TorrePersonalizados.cs` → `Calcular()` agrupaba (`GROUP BY`) por un
  literal constante (`CAST('1900-01-01' AS date)`, usado como marcador de "sin desglosar por
  día") tanto dentro del CTE como en la consulta externa. SQL Server rechaza agrupar por un
  literal que se propaga a través de un CTE y se vuelve a agrupar afuera. Esto solo ocurre
  cuando `porDia=false` — el caso de `ValoresHoy` (vista en vivo + snapshot automático); la
  vista de estadística histórica por día (`porDia=true`, agrupa por una fecha real, no un
  literal) nunca estuvo afectada.
- **Impacto real:** desde que existe el motor de "monitores personalizados" (antes de esta
  sesión), su vista EN VIVO y sus snapshots de alarma automáticos **nunca calcularon bien** —
  siempre mostraban 0 y, según el comparador, alarma constante o ausente. Pasó desapercibido
  porque nadie había mirado con esta profundidad hasta los monitores de Colombina de hoy.
- **Fix:** cuando `porDia=false`, ya no se agrupa por el literal (ni dentro del CTE ni afuera) —
  se agrega como valor literal en el `SELECT`, fuera del `GROUP BY` (válido en T-SQL; ese valor
  tampoco se lee del lado C# en ese caso). Cuando `porDia=true` sigue igual que siempre
  (agrupa por la fecha real, columna de tabla, no un literal — nunca tuvo el problema).
- **Verificado con el código real compilado** (no una réplica manual): proyecto de prueba
  aislado referenciando `DashSmart.dll` + `Microsoft.Data.SqlClient` 7.0.1, llamando
  `TorrePersonalizados.ValoresHoy` directo — antes del fix: excepción confirmada; después del
  fix: valores reales y correctos para los 39 vendedores con actividad de Colombina hoy.
- `dotnet build` limpio, 0 errores.

---

## v1.8.1 — 2026-09-03 · Torre de Control: comparador "Ritmo" + alerta Colombina (División 10+12 y Cristiano Pérez) + espejo en Cobros del día

Release chica y acotada a pedido explícito del usuario ("hazme una nueva versión para subirla
ahora mismo") — **no** incluye el módulo grande "Presupuestos — Gestión" ni "PDF Presentaciones"
(siguen en "Sin liberar": ese módulo todavía escribe a `MovilBusiness5STD_Test`, no listo para
producción; se libera aparte cuando el usuario lo pida).

- **Nuevo comparador "Ritmo"** en los monitores personalizados de Torre de Control
  (`Services/TorrePersonalizados.cs`): antes el único comparador ("Al menos ≥X al día") exigía el
  objetivo completo desde el inicio del día — no servía para vigilar un RITMO por hora (a las
  10am, con objetivo=20, no alarmaba aunque el vendedor llevara 0). "Ritmo" calcula, con una regla
  de 3 lineal entre una hora de inicio y una hora límite, cuánto debería llevar acumulado el
  vendedor A ESA HORA, y alarma si va por debajo. No hace falta programar nada aparte: el servicio
  en segundo plano ya existente (`TorreSnapshotService`) evalúa todos los monitores cada 30 min
  dentro de la ventana horaria configurada — crear el monitor basta. UI (`Views/Torre/Index.cshtml`):
  nueva opción "Ritmo (regla de 3 hasta una hora)" en el modal de monitor personalizado + 2 campos
  de hora (inicio/límite).
- **2 monitores nuevos configurados** (datos, no código — ya viven en `TorreControlMonitores_RM`):
  Colombina (Cat3ID 954), clientes distintos, Ritmo 08:20→15:00 — **20/día** para División 10+12,
  **3/día** para el grupo supervisado por Cristiano Pérez (S12+S13).
- **Cobros del día — misma alerta, como espejo**: 3 alertas nuevas en `SupervisionCobrosConfig_RM`
  (Tipo=`activaciones`, Medición=`suplidor`=Colombina) — División 10 (meta 20), División 12
  (meta 20), Cristiano Pérez (meta 3, vía Asignación cotejar/descotejar). Se evalúan en los 3
  cortes de siempre (9:50/11:50/15:30).
- **Fix en Cobros del día:** la pantalla de Asignación (cotejar/descotejar) filtraba el universo de
  vendedores a `RepTipe IN ('10','11')` — dejaba fuera división 12 y a cualquiera fuera de esas 2
  divisiones (como el grupo de Cristiano, repartido en división 20/15/sin división). Se amplió a
  todos los activos no-supervisores (`Controllers/ApiSupervisionController.cs`, `UniversoCobros`).
- Verificado contra producción antes de cargar los datos: de los 6 reps con `RepSupervisor IN
  ('S12','S13')`, 2 son Auditores con 0 pedidos en toda su historia (no venden) y 1 es un registro
  de Supervisor — excluidos de la meta de Cristiano; quedaron los 4 vendedores reales.
- `dotnet build` limpio, 0 errores. JS embebido validado con `node --check`.

---

## v1.8.0 — 2026-08-17 · Supervisión Dinámica: Cronograma/Suplidores/Virtual + nueva sección "Cobros del día"

- **Cronograma sugerido corregido.** Todos los días (Lun–Sáb) sugieren ahora a **todos los
  vendedores** del supervisor, ordenados por **Prioritario primero, luego peor IEGT** (antes
  "Prioritario" se guardaba pero no se usaba en el orden, y solo se sugerían 3 vendedores/día).

- **Suplidores reales en Calle/Virtual.** El multiselect de suplidores estaba **hardcodeado**
  con nombres inventados; ahora carga en vivo desde `Categoria3` (`GET /apisuperv/suplidores`).

- **Supervisión Virtual: Compromiso + múltiples contactos + mute de 60 min.** Se agregó el
  campo **Compromiso** (además de Comentario) y se permiten **tantos contactos como haga
  falta** por vendedor en el día (antes se ocultaba el formulario tras el 1er contacto). Al
  guardar un contacto, el vendedor entra en **pausa 60 min** (reutiliza el mute de Torre de
  Control) y luego reactiva con sus alarmas vigentes.

- **Nueva sección "💰 Cobros del día"** — metas diarias configurables con semáforo
  🟢Verde/🟡Amarillo/🔴Rojo por vendedor (el que va peor, primero):
  - **4 tipos de alerta calculados de verdad:** Presupuesto (`PresupuestosHistoricos`),
    Pedidos distintos del día, Producto específico (pedidos distintos por `ProID`) y
    **Activaciones** (clientes distintos activados hoy, medible por Producto/Categoría/
    **Suplidor**, este último elegible por nombre real de `Categoria3`).
  - **Contacto por recurso** (vendedor o supervisor): lista todas las alarmas activas a la
    vez, guarda un comentario+compromiso, y pausa al recurso 60 min.
  - **Bitácora de 3 cortes/día** (9:50 / 11:50 / 15:30): 9:50 y 11:50 comparan la
    **proyección al cierre**; 15:30 compara el **logro real**.
  - **Configuración (solo Gerencia):** editar/vencer alertas, **asignación fina** (cotejar/
    descotejar vendedores por alerta, más allá del filtro grueso de División), vigencia por
    fechas, umbrales editables (Verde/Amarillo, Rojo automático).
  - **Drill "🚦 Todas las alertas"** (Supervisor→Vendedor con todas las alertas a la vez) y
    **"🖨️ Reportes"** (generadas/atendidas por rango de fechas, imprimible).
  - Nuevo SQL: `SQL/Supervision_Cobros_RM.sql` (Config/Bitácora/Contactos) y
    `SQL/SupervisionCobrosAsignacion_RM.sql`.

- **⚠️ Nota de despliegue:** desde la migración a `net10.0` real (v1.7.0/v1.7.1), el
  instalable **FULL/DELTA requiere Hosting Bundle de .NET 10 o superior** en el servidor —
  ya no corre solo con .NET 8/9. Si el servidor destino no tiene .NET 10, usar el instalable
  **STANDALONE** en su lugar.

---

## v1.7.1 — 2026-08-10 · Instalable AUTÓNOMO (self-contained) para clientes con .NET problemático

- **Build autónomo (self-contained, win-x64).** Nuevo `CREAR_INSTALABLE_STANDALONE.ps1` que publica la app
  **con su propio runtime .NET adentro** (~115 MB). El servidor **NO necesita tener .NET instalado** (ni 8 ni 10);
  solo el Módulo ASP.NET Core en IIS (cualquier Hosting Bundle lo instala). Resuelve el **HTTP 500.30** en clientes
  nuevos donde el runtime del servidor es incompatible (el build normal, framework-dependent, dependía de que el
  servidor tuviera el runtime correcto). El paquete normal (liviano, 4.7 MB) sigue disponible para servidores con
  el runtime ya instalado. Mismo código que v1.7.0.

---

## v1.7.0 — 2026-08-09 · Compatibilidad .NET 10 (roll-forward) + apodo del IEGT + instalable slim/delta

- **Compatibilidad .NET 8/9/10 (roll-forward).** El build sigue en `net8.0` pero con `<RollForward>Major</RollForward>`:
  el mismo instalable corre en servidores con el Hosting Bundle de **.NET 8, 9 o 10** (rueda al runtime mayor
  disponible). Resuelve el error **HTTP 500.30** al instalar en un cliente nuevo que solo tiene .NET 10.
  _(Para un build que apunte directo a `net10.0` se requiere instalar el SDK de .NET 10 en la PC de desarrollo.)_

- **Instalable mucho más liviano + instalable DELTA.** Se corrigió un bug que metía la carpeta `Instalables\`
  (todos los ZIP previos) dentro del publish → el paquete FULL pasó de ~52 MB a **~4.7 MB**. Nuevo
  `CREAR_DELTA_IIS.ps1` genera un parche con **solo los archivos cambiados** (~700 KB) aplicable con
  `ACTUALIZAR_DELTA.ps1` (no borra el resto ni toca appsettings.json).

- **Apodo configurable del término "IEGT" (marca por empresa).** Donde aparezca **IEGT** puede mostrarse el
  apodo de la empresa (ej. **AMCO-GT**); por defecto vacío = sigue diciendo "IEGT". Se administra desde
  **Supervisión › Configuración › "Marca del índice — apodo del IEGT"** (solo Gerencia). Aplica en Supervisión,
  Seguimientos y el Menú (reemplazo automático de texto en pantalla, incluye lo que se re-renderiza). Endpoints
  `/apisuperv/branding` (GET/POST Gerencia) y `/apiseg/branding` (GET). _Requiere que TI ejecute
  `SQL/ConfiguracionApp_RM.sql`_ (tabla clave/valor); sin ella el apodo queda vacío (muestra "IEGT") sin errores.

---

## v1.6.0 — 2026-08-08 · Supervisión Dinámica (reportes/análisis/accesos/cronograma) + Por Fecha en Visitas/Productividad

- **Supervisión › Calle — flujo de evaluación cliente por cliente.**
  Cada cliente nuevo arranca con **todas las estrellas en 1** (antes 3) y **campos vacíos** (ya no
  se copia el comentario del cliente anterior). El **comentario y compromiso son por cliente**. Botón
  **✔ Guardar cliente** que confirma ese cliente y **limpia la pantalla** para el siguiente (el cliente
  queda en la barra). **Finalizar (■, junto a Iniciar)** lleva al reporte/firma. El reporte lista los
  clientes en **orden de visita** con “Ver detalle” (incluye el comentario de cada cliente); la **firma
  aparece solo en el reporte final** (una del vendedor + una del supervisor), nunca por cliente durante
  el recorrido.

- **Supervisión › Plan › Cronograma — agenda que llena todos los días + rejilla editable por vendedor.**
  La agenda automática ahora **reparte los seguimientos virtuales** entre todos los días virtuales y
  **llena Lunes/Sábado** con monitoreo del siguiente peor IEGT (antes quedaban vacíos). Nuevo toggle
  **Sugerida (auto) / Por vendedor (editable)**: la rejilla editable (Vendedor × Día) permite fijar el tipo
  (Presencial/Virtual/Auditoría) de cualquier vendedor cualquier día, con **distintos tipos el mismo día para
  vendedores distintos**, y guarda directo (POST `/apisuperv/cronograma`).

- **Supervisión › Reporte/Análisis — Supervisión Virtual visible como reporte y detalle.**
  El selector de la pestaña **Reporte** ahora tiene un filtro **Tipo** (Acompañamiento / Auditoría / **Virtual**);
  en Virtual, elegir Vendedor + Fecha abre una **constancia de Supervisión Virtual** imprimible (contactos del
  día con hora, canal, **alertas de Torre atendidas** y comentario/compromiso). En **📈 Análisis › Detallada**,
  cada contacto virtual tiene ahora **“Ver detalle”** (alertas atendidas, canal, contacto con, suplidores,
  comentario, compromiso). Endpoint nuevo `/apisuperv/virtual` (por vendedor/rango o por ConID). Nota: la
  constancia muestra la **hora de atención**; la hora de *generación* de cada alerta no se persiste con el
  contacto (queda como mejora futura).

- **Supervisión › Reporte — comentario general, comentarios por cliente y reimpresión de reportes guardados.**
  En el reporte de calle, los **comentarios de cada cliente** van dentro de su “Ver detalle”, y junto a la
  firma hay un **comentario general** y un **compromiso general** editables (se guardan en la evaluación).
  Arriba de la pestaña **Reporte**, un **selector en cascada** (Supervisor → Vendedor → Fecha → Reporte)
  para **abrir e imprimir** cualquier reporte guardado, con su formato de constancia completo (factores con
  peso, clientes en orden de visita con detalle, comentarios, evidencias y **firmas**). Endpoint `/evaluacion`
  ampliado (matriz por cliente + comentarios + firmas + fotos). _Requiere que TI ejecute
  `SQL/SupervisionClienteComent_RM.sql`_ (tabla nueva para el comentario por cliente); sin ella todo funciona
  salvo la persistencia del comentario por cliente.

- **Supervisión › 📈 Análisis — nuevo tab de reportes por rango de fecha.**
  Resultado de las supervisiones **agrupado** por Compañía · Supervisor · Vendedor · Tipo (Acomp/Audit/Virtual) ·
  **Día** · **Factor (pregunta)**, en vista **Resumida** (totales por grupo: # supervisiones, acomp/audit/virtual,
  clientes, **duración total y promedio**, índice promedio y banda) o **Detallada** (lista de cada evaluación).
  Filtros: Desde/Hasta, Supervisor, Vendedor, Tipo. **Drill:** clic en un grupo (vendedor/supervisor/día) baja a
  sus evaluaciones; clic en una evaluación abre su **detalle completo** (factores con estrellas, clientes, horas
  inicio/fin, **duración**, comentario, compromiso, firmas, # fotos). Por Día se ve cuántas veces se supervisó y sus
  resultados. Export a Excel + impresión. Endpoints nuevos `/apisuperv/analisis`, `/evaluaciones`, `/evaluacion`
  (respetan el alcance por rol). Nota: la duración aparece cuando la evaluación guardó Hora inicio y fin.

- **Supervisión › Configuración › Accesos — pantalla de mantenimiento de permisos (solo Gerencia).**
  Nueva sub-pestaña **🔐 Accesos** (visible solo para rol Gerencia) en la pestaña Configuración.
  Permite a Gerencia otorgar/editar/quitar qué representantes ve cada **usuario del sistema**:
  **Gerencia** (ve todos) o **acceso limitado** a uno o varios supervisores (multiselección con
  buscador). Reemplaza el mantenimiento por SQL de `SupervisionUsuarioMapa_RM`. El nombre se toma
  de `UsuarioSistema` en el servidor (evita problemas con acentos/ñ). Protecciones: no puedes
  quitarte a ti mismo el rol Gerencia ni tu propio acceso (anti-bloqueo). Endpoints nuevos
  `/apisuperv/mapa` (GET/POST) y `/apisuperv/mapa-eliminar` (POST), todos exigen rol Gerencia.

---

## v1.5.0 — 2026-08-05 · Supervisión Dinámica + Por Fecha en Visitas/Productividad

- **Módulo nuevo "Supervisión Dinámica"** (`/supervision`, API `/apisuperv`, tarjeta dc-7 en el Menú).
  Para que los supervisores evalúen a sus vendedores y la gerencia tenga historial. Tres esquemas
  que forman un **Índice Global**: IEGT 50% + Calle Acompañamiento 35% + Calle Auditoría 15%
  (pesos editables en Mantenimiento; cada esquema suma 100%).
  - **Supervisión en Calle** (móvil): dos modos — **Acompañamiento** (los 5 pasos de la venta de
    AM Comercial + gestión) y **Auditoría** (servicio y gestión desde el cliente, escalas 1–10 y Sí/No).
    Se elige el vendedor, se **buscan y agregan sus clientes** (por código/nombre) y se evalúa
    cliente por cliente; **foto** desde la cámara, comentario y compromiso, hora de acceso/inicio/fin.
  - **Supervisión Virtual** (oficina): al **cargar un vendedor** trae sus **alertas de Torre de Control**
    (con hora de generación) y exige **≥1 contacto por representante** (bitácora).
  - **Reporte firmado** (vendedor + supervisor en pantalla), exportable/imprimible, con el Índice Global.
  - **Reportes/Expediente**: por rango de fechas, cuántos clientes se abordaron, y por persona sus
    evaluaciones (distintas fechas y tipos) + su Índice Global.
  - **Acceso por rol** (tabla `SupervisionUsuarioMapa_RM`, mapeo usuario→supervisor 1→N):
    **Gerencia** ve todo, **Supervisor** ve solo sus representantes.
  - Tablas `_RM` nuevas (script `SQL/Supervision_RM.sql`, ya aplicado por TI).
  - _Pendiente:_ integrar el IEGT real por vendedor (hoy el 50% del Global va como “pendiente”).

- **Visitas en vivo — filtro Presencial/Virtual, columnas Última Visita y Total Tpo. Vis.**
  Se quitó el botón **"1a Visita Tardía"** y en su lugar hay un filtro **Tipo: Todos / Presencial / Virtual**
  (por defecto Todos). Al elegir Presencial o Virtual, **toda** la data (primera/última visita, pedidos,
  duración) se recalcula solo con visitas de ese tipo (backend `/api/data?tipo=`, VisTipoVisita '1'/'2').
  Nueva columna **Última Visita** (después de Último Pedido). Se quitó **"Mayor Prod."** y en su lugar
  **"Total Tpo. Vis."** = tiempo (H:M:S) de la gestión desde la primera hasta la última visita. En **Resumido**,
  columna **Prom Tpo Gestión** = promedio de ese tiempo entre los representantes de cada supervisor.
  En **Por Fecha**: columna **Prom Tpo. Vis.** por día, respeta el filtro Tipo (backend `/api/data-fecha?tipo=`),
  y la fila final pasó a **PROMEDIO / DÍA (Compañía)** = promedio por día de **todas** las columnas.

- **Visitas en vivo y Productividad — mejoras varias.**
  - **Visitas:** columna **Cuadre** (Cerrado/Abierto/Sin cuadre) en lugar de "Menor Prod."; tarjeta 5
    alineada a "Hora de Mayor Productividad"; botón **📥 Excel** (exporta la vista actual).
  - **Visitas — horarios "A Tiempo" separados y configurables:** el umbral de **primera visita**
    (por defecto **8:00 AM**) ahora es independiente del de **primer pedido** (8:20 AM). Botón **⚙ Config**
    para ajustar ambos (se guardan en el navegador). Aplica a colores, tarjeta "1a Visita Tardía",
    filtro y "% A Tiempo" del resumido.
  - **Productividad:** filtro **por representante** (multiselección) en los 3 modos; nombres de
    representante y supervisor tomados de la tabla **Representantes**; botón **📥 Excel**.
  - **Desglose "Por Fecha" (una fila por día) en ambos dashboards.**
    - *Productividad:* nuevo modo **Por Fecha** (`/apip/data-fecha`) — por cada día del rango:
      a visitar, visitados, sin visitas, pedidos, # reps, cumplimiento y efectividad, con total.
      Respeta todos los filtros; Top 3 pasa a mejor/peor día por efectividad.
    - *Visitas en vivo:* nuevo modo **Por Fecha** (`/api/data-fecha`) con selector **Desde/Hasta** —
      por día: reps activos, visitas, pedidos, % pedidos, prom 1ª visita (coloreada por el umbral),
      **% a tiempo** (según el umbral de primera visita configurable) y prom duración, con total.
      Respeta los filtros de supervisor y centro.

---

## v1.4.0 — 2026-07-24 · Históricos › Análisis: Clientes Perdidos y Clientes Recuperados

- **Históricos › Análisis: dos reportes nuevos — Clientes Perdidos y Clientes Recuperados.**
  Ambos parten de la asignación **RutaVisitas** (vendedor↔cliente) y miden compra contra el filtro
  seleccionado (Suplidor / Categoría / Producto / Grupo). Origen **Pedidos** (predeterminado) o
  **Ventas** (neto por rep+cliente en el rango; positivo = compró, sin filtrar estatus). Tipo de
  reporte **Por Compañía / Centro / División / Supervisor / Vendedor** con quiebre jerárquico
  (columnas padre) y total de compañía por **clientes distintos**. Filtros: fechas,
  Centro/Supervisor/Vendedor/División (dependientes), Suplidor/Categoría/Producto/Grupo y
  **Tamaño de cliente** (Clasifica). Cada reporte es **imprimible (PDF)** con el mismo diseño y
  **descargable a Excel** (hojas Resumen + Detalle; no CSV).
  - **Clientes Perdidos:** clientes asignados que **no** compraron el filtro en el rango.
    Tarjetas: asignados · no compraron (% y conteo) · cobertura. Detalle por asignación
    (Centro, Supervisor, División, Vendedor, Tamaño, Código y Nombre de cliente).
    _Validado en vivo: 20,279 asignados / 12,915 compraron / 7,364 perdidos (36.3%)._
  - **Clientes Recuperados:** rango **Base** (no compraron) + rango **Recuperación** (de esos,
    quiénes sí compraron). Tarjetas: total · sin compra en base (%) · recuperados (n de sin-compra, %).
    Detalle de recuperados con las mismas columnas.
    _Validado en vivo (base abr–jun, recup. jul): 2,511 sin compra (12.4%) / 371 recuperados (14.8%)._

---

## v1.3.0 — 2026-07-18 · Históricos (Análisis/Comparativos + Canal) y Seguimientos (IEGT por nivel)

- **Seguimientos (IEGT): resumen por nivel + variación semanal + puntos.**
  (a) **IEGT recalculado por nivel** (Centro/Supervisor/Compañía) desde las métricas del grupo —
  tasas por suma (visitados/programados, pedidos/visitados), horas por promedio ponderado, y
  conteos (fuera de ruta/PDV) por **promedio por vendedor** (no por suma). Botón "📊 Resumen" por
  fila y "📊 Resumen IEGT + variación" del nivel actual, con la misma tarjeta de sub-scores que el
  vendedor. (b) **Puntos ganados/perdidos** por indicador (nota×peso y (100−nota)×peso; suman IEGT y
  100−IEGT) en el detalle de vendedor y de nivel, ordenados por lo que más cuesta. (c) **Variación
  semana a semana (Lun–Sáb)** con IEGT y ▲/▼ vs. semana previa — trae cada semana del rango vía
  `/apiseg/semana` y recalcula el IEGT del subconjunto. Solo frontend (sin cambios de backend).
- **Históricos: Canal transversal.** Canal (Clientes.CanID) agregado como **filtro** y como
  **dimensión de análisis** en Activaciones y Comparativos. Default fijo: todos los canales
  **excepto Institucional** (aplica también al reporte Ventas). Tarjetas en **una sola fila**
  (nowrap, tamaño reducido). Incidencias/tarjetas de caídas rankeadas por **magnitud en $**
  (representativo), no solo por %. Comparativos: botón **Resumido/Detallado** e incidencias
  movidas **al final** (bajo la tabla).
- **Históricos › Análisis › Comparativos** (nuevo). Endpoint `/apihist/comparativo`. Compara
  Ventas o Pedidos entre un rango y (a) otro rango, o (b) el promedio de N periodos anteriores
  **alineados** (mismos días de semana desplazados por semana — "vs periodos anteriores"). Mide
  **ValorBruto** = cant*(precio−desc) y **facturas/pedidos distintos**. Analiza por Supervisor→Vendedor,
  Suplidor→Categoría o Cliente. Tarjetas: monto total, facturas, suplidor ▲/▼ mayor y menor,
  supervisor ▲/▼; sección de **incidencias auto** (mayores caídas/subidas, suplidores que
  desaparecieron). Filtros completos incl. división. Imprimible + Excel.

---

## v1.2.1 — 2026-07-17 · Activaciones: % de cobertura en modo Producto/Suplidor

- En modo **Por Producto/Suplidor** (el que tiene drill-down) ahora también se calcula la
  **cobertura**: trae el **total de clientes asignados** y cada mes muestra su `%` contra ese total.
  Un suplidor no tiene clientes asignados propios, así que el denominador es el **total** (mismo
  para todas las filas). Validado: COLOMBINA abr 8,765/19,841=44% · may 43% · jun 39%.
- La columna se llama **"Total clientes"** en modo Producto y **"Asignados"** en modo Representante.
- Con **"— sin comparar —"** no se hace ningún cálculo (solo los conteos).

## v1.2.0 — 2026-07-17 · Activaciones: jerarquía Centro/Supervisor, drill y división

- **Agrupa por Centro → Supervisor → Vendedor**, con **clientes distintos reales en cada nivel**
  (calculados en SQL con 8 grouping sets, NO sumando vendedores: los clientes se comparten mucho —
  61 vendedores sumarían ~48k pero los distintos reales del rango son 20,958).
- Botón **Resumido / Detallado**: resumido muestra Centro + Supervisores; detallado agrega Vendedores.
- Columna **Asignados** (primera) cuando se compara contra RutaVisitas / RutaVisitasFecha, con el
  total de asignados por centro, supervisor y vendedor (también con distintos reales por nivel).
- Filtro nuevo de **División** de vendedores (RepTipe: 10, 11, 15, 20, 25).
- **Drill-down en modo Por Producto**: Suplidor → Categorías → Productos, con breadcrumb para volver.
  La selección del drill también se imprime y se exporta.
- Excel actualizado a la jerarquía (columna Nivel + Centro/Supervisor + Asignados).

## v1.1.0 — 2026-07-16 · Históricos: reporte de Activaciones (matriz)

Nuevo reporte **Activaciones** (pestaña en Históricos de Ventas). Endpoint `/apihist/activaciones`.
- **Activación = cliente distinto que compró.** Un cliente que compra en enero y febrero cuenta en
  AMBOS meses; el **Total de la fila = clientes distintos de todo el rango** (por eso el total es
  menor que la suma de los periodos). Validado: rep 97 → meses suman 3,052 pero distintos = 917.
- **Matriz** con esquema **General / Mes / Semana / Día** (semana = lunes–domingo, con fechas).
- **Filas**: Por Representante (Supervisor cód+nombre → Vendedor cód+nombre) o Por Producto
  (agrupado por Suplidor / Categoría / Grupo Producto / Producto).
- **Fuente seleccionable**: Ventas (`VWVentas`+Detalle, `VenEstatus<>0`, vendedor=`RepVendedor`) o
  Pedidos (`VWPedidos`+Detalle, vendedor=`RepCodigo`).
- **Filtros multi-selección con buscador**: Centro, Supervisor, Vendedor (dependientes), Suplidor,
  Categoría, Producto, Grupo Producto (`GrupoProductos`/`GrupoProductosDetalle`).
- **Indicadores ▲/▼** por periodo (vs. periodo anterior) y en el Total (vs. **mismo rango del año
  anterior**, YoY).
- **Comparación contra ruta** (opcional): RutaVisitas (Totales) · RutaVisitasFecha (General) ·
  RutaVisitasFecha (Acumulado). Cada celda muestra `120 (58%)` sobre los asignados del vendedor.
- **Tarjetas**: activaciones del rango + YoY, vendedores/ítems, mejor periodo, promedio, cobertura.
- **Imprimible** (`@media print`, horizontal) con encabezado que imprime la **selección aplicada**,
  y **Excel** con la misma cabecera + matriz.
- Rendimiento validado: 6 meses / 61 vendedores ≈ 1.5 s; por producto ≈ 0.3 s.

## v1.0.3 — 2026-07-15 · Torre: reactivación fina de alertas atendidas

- Al atender (contactar) se **apagan todas** las alertas del vendedor. Tras el mute (60 min),
  las que **pueden repetirse** vuelven a evaluarse si el problema persiste/reaparece, con el
  **reloj contando desde el contacto** (se reinician los contadores, ej. pedidos fuera de día).
- **Excepción**: el **inicio tardío** (1er pedido) es un evento único del día → una vez atendido,
  **no vuelve a encenderse** (conjunto `UnaVezAlDia`).
- Aplica en monitor en vivo y en snapshots. Los monitores personalizados se tratan como
  repetibles.

## v1.0.2 — 2026-07-15 · Torre: reportes y atención de alertas

Correcciones tras revisar el reporte del día:
- **Reporte excluye** a los representantes marcados "no medir" (`_REPS_OFF_`).
- **1er pedido tardío** ya no cuenta el "aún no ha pedido" (null) de la mañana: solo cuenta el
  pedido real tardío, y el null solo alarma pasada la hora de gracia (10:00). Bajó de inflado a real.
- **Homologación contacto↔alertas**: un contacto **atiende todas las alertas de ese vendedor ese
  día** (aunque queden sin resolver). El reporte muestra atendidas reales (antes casi 0 porque
  dependía de checkboxes casi nunca usados).
- **Alerta atendida no reaparece**: un vendedor contactado hoy queda atendido el resto del día —
  no genera más snapshots ni alarmas en vivo (reemplaza el "mute 60 min + reactivar").

## v1.0.1 — 2026-07-14

- **Versión visible en pantalla**: el pie del Menú muestra `v<versión>` leída de `/version`,
  para confirmar de un vistazo qué release está desplegado.

## v1.0.0 — 2026-07-14 · Línea base

Primera versión versionada. Consolida todo lo construido hasta la fecha:

### Torre de Control
- Motor de monitores con snapshots automáticos cada 30 min (ventana configurable 8:20–16:00).
- **9 monitores núcleo**: 1er pedido tardío, Cumplimiento, Tiempo de visita, Efectividad,
  Mucho sin sincronizar, Fuera de entorno, Pedidos fuera de día, **Cuadre sin cerrar**,
  **Último pedido temprano**.
- **Monitores personalizados**: metas de venta por grupo (producto/línea/suplidor, por
  clientes/unidades/monto/impactos), con vigencia por fechas y hora de corte opcional.
- Bitácora de contactos con compromiso y seguimiento; representantes SIN alerta; mute
  configurable tras contacto con reinicio de contadores.
- Reglas de negocio: alertas solo dentro de la ventana horaria; alerta **atendida no
  vuelve a encenderse** el día; "terminó su ruta" deja de medirse (salvo cuadre).
- Reportes: filtro por tipo de alerta, por supervisor y por vendedor; **alertas
  atendidas / generadas** (X/Y); export a Excel e impresión.

### Productividad
- Columna **Sin Visitas** (programados no visitados), en rojo.
- Colores de cumplimiento: verde ≥99% · amarillo ≥89.99% · rojo <89.99%.
- Vista **Por Suplidor** con subtotales por suplidor y total general (GROUPING SETS):
  Prom/día × Vend (÷ días hábiles Lun–Sáb) y Efectividad. Esquema imprimible.

### General
- **Presentación ejecutiva** de Torre de Control servida desde la app
  (`/presentacion-torre.html`).
- Endpoint **`/version`** para confirmar la versión desplegada.
- Instalable IIST versionado (`CREAR_INSTALABLE_IIS.ps1`) con `INSTALAR_IIS.ps1` (nuevo)
  y `ACTUALIZAR_IIS.ps1` (preserva `appsettings.json`).
