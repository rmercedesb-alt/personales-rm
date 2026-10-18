
const BASE = '@(Context.Request.PathBase.Value)';
let loteActual = null;
let filasActuales = [];

function fmt(n){ return n==null ? '—' : Number(n).toLocaleString('es-DO',{maximumFractionDigits:2}); }

fetch(BASE+'/apipresupuestogestion/identidad').then(r=>r.json()).then(j=>{
  if (!j.ok) return;
  document.getElementById('rolchip').textContent = j.rol || '—';
  document.getElementById('who').textContent = j.nombre || j.login || '';
}).catch(()=>{});

// Canales para el filtro de "1. Generar" — reusa /apipresupuestos/filtros (misma sesión),
// no hace falta duplicar el catálogo. Default: todos marcados MENOS Institucional (7000),
// mismo criterio ya usado en Presupuestos/Historicos.
fetch(BASE+'/apipresupuestos/filtros').then(r=>r.json()).then(j=>{
  const host = document.getElementById('canalesCheck');
  if (!j.ok || !j.canales || !j.canales.length) { host.innerHTML = '<span class="hint" style="padding:0">(No se pudo cargar el catálogo de canales — se aplicará el default: todos menos Institucional.)</span>'; return; }
  host.innerHTML = j.canales.map(c => `<label><input type="checkbox" value="${c.v}" ${c.v!=='7000'?'checked':''}> ${c.t}</label>`).join('');
}).catch(()=>{ document.getElementById('canalesCheck').innerHTML = '<span class="hint" style="padding:0">(No se pudo cargar el catálogo de canales.)</span>'; });

document.getElementById('btnGenerar').addEventListener('click', async () => {
  const msg = document.getElementById('msgGenerar');
  msg.textContent = 'Generando...'; msg.className='msg';
  // TODO el handler en un solo try/catch (antes solo cubría el fetch) — si algo
  // revienta silenciosamente ANTES de llamar al servidor (parseo de fechas,
  // un elemento que no se encuentra, etc.) ahora se ve en pantalla en vez de
  // quedarse pegado en "Generando..." para siempre sin pista alguna.
  try {
    console.log('[Generar] click recibido, armando request...');
    const [da, dm] = document.getElementById('destinoDesde').value.split('-').map(Number);
    const [ha, hm] = document.getElementById('destinoHasta').value.split('-').map(Number);
    const destino = [];
    if (da && dm && ha && hm) {
      let a = da, m = dm;
      let guard = 0;
      while ((a < ha || (a === ha && m <= hm)) && guard < 240) {
        destino.push({ anio: a, mes: m });
        m++; if (m > 12) { m = 1; a++; }
        guard++;
      }
    }
    const reps = document.getElementById('reps').value.split(',').map(s=>s.trim()).filter(Boolean);
    const compVal = document.getElementById('compararAnioMes').value; // "2026-08" o vacío
    const [compAnio, compMes] = compVal ? compVal.split('-').map(Number) : [null, null];
    const objetivoRaw = document.getElementById('objetivoMonto').value;
    const objetivoModo = document.getElementById('objetivoModo').value; // 'valor' | 'unidades'
    const objetivoNum = objetivoRaw ? parseFloat(objetivoRaw) : null;
    const canalesMarcados = Array.from(document.querySelectorAll('#canalesCheck input:checked')).map(c => parseInt(c.value));
    const body = {
      baseFechaInicio: document.getElementById('baseIni').value,
      baseFechaFin: document.getElementById('baseFin').value,
      destino, preTipo: 'Unddprod',
      reps: reps.length ? reps : null,
      descripcion: document.getElementById('descripcion').value || null,
      compAnio, compMes,
      montoValorSugerido: (objetivoNum && objetivoModo==='valor') ? objetivoNum : null,
      montoTotalSugerido: (objetivoNum && objetivoModo==='unidades') ? objetivoNum : null,
      canales: canalesMarcados.length ? canalesMarcados : null,
    };
    console.log('[Generar] body armado:', body, 'URL:', BASE+'/apipresupuestogestion/generar');
    const r = await fetch(BASE+'/apipresupuestogestion/generar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
    console.log('[Generar] respuesta recibida, status:', r.status);
    const j = await r.json();
    if (!j.ok) { msg.textContent = j.error || 'Error'; msg.className='msg err'; return; }
    msg.textContent = `Lote generado: ${j.filasInsertadas} filas, ${j.anomalias} anomalías.`; msg.className='msg ok';
    await cargarLote(j.loteId);
  } catch(e) { console.error('[Generar] excepcion:', e); msg.textContent = 'Error: '+e; msg.className='msg err'; }
});

async function cargarLote(loteId){
  loteActual = loteId;
  const r = await fetch(BASE+'/apipresupuestogestion/lote/'+loteId);
  const j = await r.json();
  if (!j.ok) return;
  filasActuales = j.filas;
  document.getElementById('panelLote').style.display='block';
  pintarTabla(j.lote.Estado, j.lote.Descripcion);
  document.getElementById('panelComparativo').style.display='block';
}

function pintarTabla(estado, descripcion){
  const totalProp = filasActuales.reduce((s,f)=>s+f.Propuesto,0);
  const totalAct = filasActuales.reduce((s,f)=>s+(f.Actual||0),0);
  const anomCount = filasActuales.filter(f=>f.Anomalia).length;
  document.getElementById('resumenLote').innerHTML =
    `<span>Estado: <b>${estado}</b></span><span>Filas: <b>${filasActuales.length}</b></span>`+
    `<span>Total propuesto: <b>${fmt(totalProp)}</b></span><span>Total actual: <b>${fmt(totalAct)}</b></span>`+
    `<span>Anomalías: <b>${anomCount}</b></span>` +
    (descripcion ? `<span>${descripcion}</span>` : '');
  const soloLectura = estado !== 'Borrador';
  const tbody = document.getElementById('tbodyLote');
  tbody.innerHTML = filasActuales.map(f => `
    <tr class="${f.Anomalia?'anom':''}" data-id="${f.Id}">
      <td>${f.Id}</td>
      <td>${f.RepCodigo} — ${f.RepNombre||''}</td>
      <td>${f.PreReferencia} — ${f.ProductoNombre||''}</td>
      <td>${f.PreAnio}-${String(f.PreMes).padStart(2,'0')}</td>
      <td class="propuesto" ${soloLectura?'':'onclick="editarCelda(this,'+f.Id+')"'}>${fmt(f.Propuesto)}</td>
      <td>${fmt(f.Actual)}</td>
      <td>${fmt(f.Actual!=null ? f.Propuesto-f.Actual : null)}</td>
      <td>${f.Anomalia ? `<span class="badge warn" title="${f.AnomaliaDetalle||''}">Revisar</span>` : ''}</td>
      <td>${soloLectura?'':'<button class="btn-mini" onclick="eliminarFila('+f.Id+')">Quitar</button>'}</td>
    </tr>`).join('');
}

window.editarCelda = function(td, id){
  const actual = filasActuales.find(f=>f.Id===id).Propuesto;
  td.innerHTML = `<input type="number" step="0.01" value="${actual}">`;
  const input = td.querySelector('input'); input.focus(); input.select();
  const commit = async () => {
    const nuevo = parseFloat(input.value);
    if (isNaN(nuevo)) { pintarTabla('Borrador'); return; }
    await guardarValores([{id, valor: nuevo}]);
  };
  input.addEventListener('blur', commit);
  input.addEventListener('keydown', e => { if (e.key==='Enter') input.blur(); if (e.key==='Escape') pintarTabla('Borrador'); });
};

async function guardarValores(items){
  const msg = document.getElementById('msgLote');
  const r = await fetch(BASE+'/apipresupuestogestion/staging/actualizarvalores', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({loteId: loteActual, items}) });
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error||'Error'; msg.className='msg err'; return; }
  msg.textContent = `${j.actualizados} valor(es) actualizado(s).`; msg.className='msg ok';
  await cargarLote(loteActual);
}

window.eliminarFila = async function(id){
  if (!confirm('¿Quitar esta fila del lote?')) return;
  const msg = document.getElementById('msgLote');
  const r = await fetch(BASE+'/apipresupuestogestion/staging/eliminarfila', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({loteId: loteActual, id}) });
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error||'Error'; msg.className='msg err'; return; }
  await cargarLote(loteActual);
};

document.getElementById('btnAgregar').addEventListener('click', async () => {
  const [anio, mes] = document.getElementById('addAnioMes').value.split('-').map(s=>parseInt(s));
  const body = {
    loteId: loteActual,
    repCodigo: document.getElementById('addRep').value.trim(),
    preReferencia: document.getElementById('addRef').value.trim(),
    preAnio: anio, preMes: mes,
    valor: parseFloat(document.getElementById('addValor').value) || 0,
  };
  const msg = document.getElementById('msgLote');
  if (!body.repCodigo || !body.preReferencia || !anio || !mes) { msg.textContent='Completa Rep, Producto y Año-Mes.'; msg.className='msg err'; return; }
  const r = await fetch(BASE+'/apipresupuestogestion/staging/agregarfila', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error||'Error'; msg.className='msg err'; return; }
  msg.textContent = 'Fila agregada.'; msg.className='msg ok';
  await cargarLote(loteActual);
});

document.getElementById('btnExportar').addEventListener('click', () => {
  const head = ['Id','Rep','RepNombre','Producto','ProductoNombre','Anio','Mes','Propuesto','Actual','Anomalia','Detalle'];
  const rows = filasActuales.map(f => [f.Id, f.RepCodigo, f.RepNombre||'', f.PreReferencia, f.ProductoNombre||'', f.PreAnio, f.PreMes, f.Propuesto, f.Actual, f.Anomalia?'SI':'', f.AnomaliaDetalle||'']);
  const ws = XLSX.utils.aoa_to_sheet([head].concat(rows));
  const wb = XLSX.utils.book_new(); XLSX.utils.book_append_sheet(wb, ws, 'Lote');
  XLSX.writeFile(wb, 'presupuesto_lote_'+loteActual+'.xlsx');
});

document.getElementById('inputImportar').addEventListener('change', async (ev) => {
  const file = ev.target.files[0]; if (!file) return;
  const msg = document.getElementById('msgLote');
  try {
    const buf = await file.arrayBuffer();
    const wb = XLSX.read(buf, { type:'array' });
    const ws = wb.Sheets[wb.SheetNames[0]];
    const rows = XLSX.utils.sheet_to_json(ws, { header:1 });
    const head = rows[0].map(h => String(h).trim());
    const idxId = head.indexOf('Id'), idxProp = head.indexOf('Propuesto');
    if (idxId < 0 || idxProp < 0) { msg.textContent = 'El Excel debe tener columnas "Id" y "Propuesto" (usa el exportado por este mismo lote).'; msg.className='msg err'; return; }
    const items = rows.slice(1).filter(r => r.length && r[idxId]!=null && r[idxId]!=='').map(r => ({ id: parseInt(r[idxId]), valor: parseFloat(r[idxProp]) })).filter(it => !isNaN(it.id) && !isNaN(it.valor));
    if (items.length === 0) { msg.textContent = 'No se encontraron filas válidas en el Excel.'; msg.className='msg err'; return; }
    await guardarValores(items);
  } catch(e) { msg.textContent = 'Error leyendo el Excel: '+e; msg.className='msg err'; }
  ev.target.value = '';
});

document.getElementById('btnPublicar').addEventListener('click', async () => {
  if (!loteActual || !confirm('¿Publicar este lote a PresupuestosHistoricos?')) return;
  const msg = document.getElementById('msgLote');
  const r = await fetch(BASE+'/apipresupuestogestion/publicar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({loteId: loteActual}) });
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error||'Error'; msg.className='msg err'; return; }
  msg.textContent = `Publicado: ${j.filasAfectadas} filas, suma ${fmt(j.sumaTotal)}.`; msg.className='msg ok';
  await cargarLote(loteActual);
});

document.getElementById('btnDescartar').addEventListener('click', async () => {
  if (!loteActual || !confirm('¿Descartar este lote?')) return;
  const msg = document.getElementById('msgLote');
  const r = await fetch(BASE+'/apipresupuestogestion/descartar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({loteId: loteActual}) });
  const j = await r.json();
  msg.textContent = j.ok && j.actualizado ? 'Lote descartado.' : (j.error||'No se pudo descartar.');
  msg.className = j.ok && j.actualizado ? 'msg ok' : 'msg err';
  if (j.ok && j.actualizado) await cargarLote(loteActual);
});

// ══════════════════ ⚙ Configuración de variables/metodologías ══════════════
let definicionesActuales = [];
let varSeleccionada = null;

document.getElementById('btnConfig').addEventListener('click', async () => {
  document.getElementById('modalConfig').classList.add('open');
  document.getElementById('panelValores').style.display = 'none';
  await cargarDefiniciones();
});
document.getElementById('btnCerrarConfig').addEventListener('click', () => {
  document.getElementById('modalConfig').classList.remove('open');
});

async function cargarDefiniciones(){
  const r = await fetch(BASE+'/apipresupuestogestion/variables/definiciones');
  const j = await r.json();
  if (!j.ok) return;
  definicionesActuales = j.definiciones;
  const tbody = document.getElementById('tbodyDefiniciones');
  tbody.innerHTML = definicionesActuales.map(d => `
    <tr class="def-row ${varSeleccionada===d.VarCodigo?'sel':''}" data-cod="${d.VarCodigo}">
      <td><b>${d.VarCodigo}</b></td>
      <td><input value="${d.VarNombre}" data-f="varNombre"></td>
      <td><input value="${d.VarDescripcion||''}" data-f="varDescripcion"></td>
      <td>
        <select data-f="varTipoDato">
          ${['decimal','int','texto'].map(t=>`<option ${t===d.VarTipoDato?'selected':''}>${t}</option>`).join('')}
        </select>
      </td>
      <td><input value="${d.VarAmbitoPermitido}" data-f="varAmbitoPermitido"></td>
      <td style="text-align:center"><input type="checkbox" data-f="activo" ${d.Activo?'checked':''}></td>
      <td>${d.ValoresCargados} <button class="btn-sm" style="margin-left:4px" onclick="verValores('${d.VarCodigo}')">Ver</button></td>
      <td>
        <button class="btn-sm" onclick="guardarDefinicionFila('${d.VarCodigo}', this)">Guardar</button>
        <button class="btn-sm del" onclick="eliminarDefinicion('${d.VarCodigo}')">Borrar</button>
      </td>
    </tr>`).join('');
}

window.guardarDefinicionFila = async function(varCodigo, btn){
  const tr = btn.closest('tr');
  const body = { varCodigo,
    varNombre: tr.querySelector('[data-f=varNombre]').value,
    varDescripcion: tr.querySelector('[data-f=varDescripcion]').value,
    varTipoDato: tr.querySelector('[data-f=varTipoDato]').value,
    varAmbitoPermitido: tr.querySelector('[data-f=varAmbitoPermitido]').value,
    activo: tr.querySelector('[data-f=activo]').checked,
  };
  const r = await fetch(BASE+'/apipresupuestogestion/variables/definiciones/guardar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  const j = await r.json();
  if (j.ok) { btn.textContent='✓'; setTimeout(()=>btn.textContent='Guardar',1200); } else alert(j.error||'Error');
};

window.eliminarDefinicion = async function(varCodigo){
  if (!confirm(`¿Eliminar la variable "${varCodigo}"?`)) return;
  const r = await fetch(BASE+'/apipresupuestogestion/variables/definiciones/eliminar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({varCodigo}) });
  const j = await r.json();
  if (!j.ok) { alert(j.error||'Error'); return; }
  await cargarDefiniciones();
};

document.getElementById('btnNuevaDefinicion').addEventListener('click', async () => {
  const cod = document.getElementById('nuevoCodigo').value.trim().toUpperCase().replace(/\s+/g,'_');
  if (!cod) { alert('Escribe un código para la variable nueva.'); return; }
  const body = {
    varCodigo: cod,
    varNombre: document.getElementById('nuevoNombre').value || cod,
    varDescripcion: document.getElementById('nuevoDesc').value,
    varTipoDato: document.getElementById('nuevoTipo').value,
    varAmbitoPermitido: document.getElementById('nuevoAmbito').value || 'Global',
    activo: document.getElementById('nuevoActivo').checked,
  };
  const r = await fetch(BASE+'/apipresupuestogestion/variables/definiciones/guardar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  const j = await r.json();
  if (!j.ok) { alert(j.error||'Error'); return; }
  document.getElementById('nuevoCodigo').value=''; document.getElementById('nuevoNombre').value='';
  document.getElementById('nuevoDesc').value=''; document.getElementById('nuevoAmbito').value='';
  await cargarDefiniciones();
});

window.verValores = async function(varCodigo){
  varSeleccionada = varCodigo;
  await cargarDefiniciones();
  document.getElementById('panelValores').style.display = 'block';
  document.getElementById('tituloValores').textContent = `Valores de ${varCodigo}`;
  const r = await fetch(BASE+'/apipresupuestogestion/variables/valores?varCodigo='+encodeURIComponent(varCodigo));
  const j = await r.json();
  if (!j.ok) return;
  const tbody = document.getElementById('tbodyValores');
  tbody.innerHTML = j.valores.map(v => `
    <tr data-id="${v.Id}">
      <td>${v.AmbitoTipo}</td>
      <td>${v.AmbitoValor||'—'}</td>
      <td>${v.PeriodoTipo}</td>
      <td>${v.PeriodoAnio}</td>
      <td>${v.PeriodoTrimestre??''}</td>
      <td>${v.PeriodoMes??''}</td>
      <td><input value="${v.Valor}" data-f="valor" style="width:80px"></td>
      <td>
        <button class="btn-sm" onclick="guardarValorFila(${v.Id},'${varCodigo}',this)">Guardar</button>
        <button class="btn-sm del" onclick="eliminarValorFila(${v.Id},'${varCodigo}')">Borrar</button>
      </td>
    </tr>`).join('');
};

window.guardarValorFila = async function(id, varCodigo, btn){
  const tr = btn.closest('tr');
  const valor = parseFloat(tr.querySelector('[data-f=valor]').value);
  const r = await fetch(BASE+'/apipresupuestogestion/variables/valores/guardar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({
    id, varCodigo,
    ambitoTipo: tr.children[0].textContent, ambitoValor: tr.children[1].textContent==='—'?null:tr.children[1].textContent,
    periodoTipo: tr.children[2].textContent, periodoAnio: parseInt(tr.children[3].textContent),
    periodoTrimestre: tr.children[4].textContent?parseInt(tr.children[4].textContent):null,
    periodoMes: tr.children[5].textContent?parseInt(tr.children[5].textContent):null,
    valor,
  })});
  const j = await r.json();
  if (j.ok) { btn.textContent='✓'; setTimeout(()=>btn.textContent='Guardar',1200); } else alert(j.error||'Error');
};

window.eliminarValorFila = async function(id, varCodigo){
  if (!confirm('¿Eliminar este valor?')) return;
  const r = await fetch(BASE+'/apipresupuestogestion/variables/valores/eliminar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({id}) });
  const j = await r.json();
  if (!j.ok) { alert(j.error||'Error'); return; }
  await window.verValores(varCodigo);
};

document.getElementById('btnNuevoValor').addEventListener('click', async () => {
  if (!varSeleccionada) return;
  const body = {
    id: null, varCodigo: varSeleccionada,
    ambitoTipo: document.getElementById('nvAmbitoTipo').value,
    ambitoValor: document.getElementById('nvAmbitoValor').value || null,
    periodoTipo: document.getElementById('nvPeriodoTipo').value,
    periodoAnio: parseInt(document.getElementById('nvAnio').value),
    periodoTrimestre: document.getElementById('nvTrimestre').value ? parseInt(document.getElementById('nvTrimestre').value) : null,
    periodoMes: document.getElementById('nvMes').value ? parseInt(document.getElementById('nvMes').value) : null,
    valor: parseFloat(document.getElementById('nvValor').value),
  };
  const msg = document.getElementById('msgValores');
  if (!body.periodoAnio || isNaN(body.valor)) { msg.textContent='Completa al menos Año y Valor.'; msg.className='msg err'; return; }
  const r = await fetch(BASE+'/apipresupuestogestion/variables/valores/guardar', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error||'Error'; msg.className='msg err'; return; }
  msg.textContent = 'Agregado.'; msg.className='msg ok';
  document.getElementById('nvAmbitoValor').value=''; document.getElementById('nvValor').value='';
  await window.verValores(varSeleccionada);
});

// ══════════════════ 3. Análisis comparativo (3 cadenas apiladas) ═══════════
// Rediseño 2026-08-30 (pedido explícito del usuario): el drill de antes
// REEMPLAZABA la tabla al bajar de nivel — "si paso de compañía a Centro solo
// puedo ver los del centro X no ambos a la vez". Ahora cada nivel es su PROPIA
// tarjeta que se apila debajo de la anterior (nunca desaparece), con "‹ Ver
// todos" para volver sin perder de vista lo de arriba — mismo patrón ya
// validado por el usuario en Views/Presupuestos/Index.cshtml
// (renderCadenaCentro/Canal/Suplidor). Además del eje Organización (Centro/
// Canal→Supervisor→Vendedor) se agrega el eje Producto (Suplidor→Categoría) y
// TODAS las cadenas terminan en Producto — "hasta llegar al representante y el
// producto", pedido explícito del usuario.
const METRICAS = {
  unidades: { sugerido: 'UnidadesPropuesto', comparar: 'UnidadesComparacion', promedio: 'UnidadesVentaPromedio', lbl: 'Unidades' },
  cajas:    { sugerido: 'CajasPropuesto',    comparar: 'CajasComparacion',    promedio: 'CajasVentaPromedio',    lbl: 'Cajas' },
  monto:    { sugerido: 'MontoPropuesto',    comparar: 'MontoComparacion',    promedio: 'MontoVentaPromedio',    lbl: 'RD$ (estimado)' },
};
const CADENAS = {
  centro: { niveles: [
    { label: 'Centro', dim: f => f.CedCodigo, nombre: f => f.CentroNombre, ambito: v => ({ cedCodigo: v }) },
    { label: 'Supervisor', dim: f => f.SupCod, nombre: f => f.SupNombre || '(Sin supervisor)', ambito: v => ({ supCod: v }) },
    { label: 'Vendedor', dim: f => f.RepCodigo, nombre: f => `${f.RepCodigo} — ${f.RepNombre}`, ambito: v => ({ repCodigo: v }) },
    { label: 'Producto', dim: f => f.ProId, nombre: f => `${f.ProId} — ${f.ProDescripcion}`, ambito: v => ({ proId: Number(v) }) },
  ]},
  canal: { niveles: [
    { label: 'Canal', dim: f => f.CanID, nombre: f => f.CanNombre, ambito: v => ({ canId: Number(v) }) },
    { label: 'Supervisor', dim: f => f.SupCod, nombre: f => f.SupNombre || '(Sin supervisor)', ambito: v => ({ supCod: v }) },
    { label: 'Vendedor', dim: f => f.RepCodigo, nombre: f => `${f.RepCodigo} — ${f.RepNombre}`, ambito: v => ({ repCodigo: v }) },
    { label: 'Producto', dim: f => f.ProId, nombre: f => `${f.ProId} — ${f.ProDescripcion}`, ambito: v => ({ proId: Number(v) }) },
  ]},
  suplidor: { niveles: [
    { label: 'Suplidor', dim: f => f.Cat3ID, nombre: f => f.Cat3Nombre, ambito: v => ({ cat3Id: Number(v) }) },
    { label: 'Categoría', dim: f => f.Cat1ID, nombre: f => f.Cat1Nombre, ambito: v => ({ cat1Id: Number(v) }) },
    { label: 'Producto', dim: f => f.ProId, nombre: f => `${f.ProId} — ${f.ProDescripcion}`, ambito: v => ({ proId: Number(v) }) },
  ]},
};
const CADENA_NAV = { centro: [], canal: [], suplidor: [] }; // por cadena: [{valor,nombre}, ...] elegido nivel a nivel
let AMBITO_REGISTRY = {}; let ambitoSeq = 0;
let compFilas = [];
let compEsPromedioMensual = false;
let compEtiquetaMes = 'Mismo período';
let compMetrica = 'unidades';

document.getElementById('compModoActual').addEventListener('change', () => { document.getElementById('compAnioMes').disabled = true; });
document.getElementById('compModoMes').addEventListener('change', () => { document.getElementById('compAnioMes').disabled = false; });

window.cambiarMetrica = function(m){
  compMetrica = m;
  ['mUnidades','mCajas','mMonto'].forEach(id => document.getElementById(id).classList.remove('on'));
  document.getElementById({unidades:'mUnidades',cajas:'mCajas',monto:'mMonto'}[m]).classList.add('on');
  renderTodasLasCadenas();
};

document.getElementById('btnCargarComparativo').addEventListener('click', async () => {
  if (!loteActual) return;
  await cargarComparativo();
});

async function cargarComparativo(){
  const msg = document.getElementById('msgComparativo');
  msg.textContent = 'Cargando...'; msg.className = 'msg';
  let url = BASE+'/apipresupuestogestion/lote/'+loteActual+'/comparativo';
  compEtiquetaMes = 'Mismo período (ya publicado)';
  if (document.getElementById('compModoMes').checked) {
    const v = document.getElementById('compAnioMes').value; // "2025-09"
    if (!v) { msg.textContent = 'Elige un Año-Mes para comparar.'; msg.className = 'msg err'; return; }
    const [a, m] = v.split('-');
    url += `?compAnio=${a}&compMes=${parseInt(m)}`;
    compEtiquetaMes = `Presupuesto ${v}`;
  }
  const r = await fetch(url);
  const j = await r.json();
  if (!j.ok) { msg.textContent = j.error || 'Error'; msg.className = 'msg err'; return; }
  msg.textContent = '';
  compFilas = j.filas;
  compEsPromedioMensual = j.esPromedioMensual;
  CADENA_NAV.centro = []; CADENA_NAV.canal = []; CADENA_NAV.suplidor = [];
  renderTodasLasCadenas();
}

function sumaGrupo(filas){
  const campos = ['UnidadesPropuesto','UnidadesComparacion','UnidadesVentaPromedio','CajasPropuesto','CajasComparacion','CajasVentaPromedio','MontoPropuesto','MontoComparacion','MontoVentaPromedio'];
  const init = {}; campos.forEach(c => init[c] = 0);
  return filas.reduce((s,f) => { const r = {...s}; campos.forEach(c => r[c] = s[c] + f[c]); return r; }, init);
}

function pctCrecimiento(nuevo, base){
  if (base === 0) return nuevo > 0 ? { txt: 'Nuevo', cls: 'good' } : { txt: '—', cls: 'na' };
  const pct = (nuevo - base) / base * 100;
  return { txt: (pct>=0?'+':'')+pct.toFixed(1)+'%', cls: pct >= 0 ? 'good' : 'bad' };
}

function registrarAmbito(a){ const id = 'a'+(ambitoSeq++); AMBITO_REGISTRY[id] = a; return id; }

window.cadenaAjustar = async function(ambitoId, etiqueta){
  if (!loteActual) return;
  const ambito = AMBITO_REGISTRY[ambitoId] || {};
  const txt = prompt(`Ajustar "${etiqueta}"\n\nEscribe el VALOR al que debe llegar el total propuesto de este nodo (ej. 850000), o un % (ej. 110% = crecer a 110% de lo que tiene hoy). Se reparte proporcional entre todas sus filas.`);
  if (!txt) return;
  const t = txt.trim();
  const modo = t.endsWith('%') ? 'pct' : 'valor';
  const monto = parseFloat(t.replace('%','').replace(/,/g,''));
  if (isNaN(monto)) { alert('Número inválido.'); return; }
  const r = await fetch(BASE+'/apipresupuestogestion/lote/'+loteActual+'/ajustar-nodo', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ ambito, modo, monto }) });
  const j = await r.json();
  if (!j.ok) { alert(j.error||'Error'); return; }
  alert(`Ajustado: ${j.filasActualizadas} fila(s), escala=${j.escala.toFixed(4)}${j.anomalias?`, ${j.anomalias} anomalía(s)`:''}.`);
  await cargarComparativo();
  await cargarLote(loteActual); // "2. Revisar" también cambió (PrePresupuestoPropuesto)
};

window.cadenaDrill = function(cadenaKey, nivelIdx, valor, nombre){
  CADENA_NAV[cadenaKey] = CADENA_NAV[cadenaKey].slice(0, nivelIdx).concat([{ valor, nombre }]);
  renderNivelCadena(cadenaKey);
};

window.cadenaVolver = function(cadenaKey, hastaIdx){
  CADENA_NAV[cadenaKey] = CADENA_NAV[cadenaKey].slice(0, hastaIdx);
  renderNivelCadena(cadenaKey);
};

function filaNivelHtml(cadenaKey, nivelIdx, nombre, s, clickable, valorClave){
  const m = METRICAS[compMetrica];
  const sugerido = s[m.sugerido], comparar = s[m.comparar], promedio = s[m.promedio];
  const cMes = pctCrecimiento(sugerido, comparar);
  const cProm = pctCrecimiento(sugerido, promedio);
  const onclick = clickable ? `onclick="cadenaDrill('${cadenaKey}',${nivelIdx},'${String(valorClave).replace(/'/g,"\\'")}','${nombre.replace(/'/g,"\\'")}')"` : '';
  return `<tr class="${clickable?'clickable':''}" ${onclick}>
    <td>${nombre}${clickable?' →':''}</td>
    <td>${fmt(comparar)}</td><td>${fmt(promedio)}</td><td>${fmt(sugerido)}</td>
    <td><span class="pct2 ${cMes.cls}">${cMes.txt}</span></td>
    <td><span class="pct2 ${cProm.cls}">${cProm.txt}</span></td>
  </tr>`;
}

function renderNivelCadena(cadenaKey){
  const { niveles } = CADENAS[cadenaKey];
  const nav = CADENA_NAV[cadenaKey];
  const host = document.getElementById('cadena'+cadenaKey.charAt(0).toUpperCase()+cadenaKey.slice(1));
  let htmlTotal = '';

  for (let i = 0; i < niveles.length; i++) {
    const nivel = niveles[i];
    const filasFiltradas = compFilas.filter(f => nav.slice(0, i).every((sel, j) => String(niveles[j].dim(f)) === String(sel.valor)));
    const grupos = new Map();
    for (const f of filasFiltradas) {
      const k = String(nivel.dim(f));
      if (!grupos.has(k)) grupos.set(k, { nombre: nivel.nombre(f), filas: [] });
      grupos.get(k).filas.push(f);
    }
    const esUltimo = i === niveles.length - 1;
    const m = METRICAS[compMetrica];
    const filasOrdenadas = [...grupos.entries()].sort((a,b) => sumaGrupo(b[1].filas)[m.sugerido] - sumaGrupo(a[1].filas)[m.sugerido]);

    let ambitoBase = {};
    nav.slice(0, i).forEach((sel, j) => Object.assign(ambitoBase, niveles[j].ambito(sel.valor)));
    const ambitoId = registrarAmbito(ambitoBase);
    const titulo = i > 0 ? `${nivel.label} de ${nav[i-1].nombre}` : `Por ${nivel.label}`;
    const volverBtn = i > 0 ? `<span class="crumb2 chico" onclick="cadenaVolver('${cadenaKey}',${i-1})">‹ Ver todos</span>` : '';
    const ajustarBtn = `<button class="btn-out" style="height:24px;padding:2px 9px;font-size:11px" onclick="cadenaAjustar('${ambitoId}','${titulo.replace(/'/g,"\\'")}')">🎯 Sugerir para todo</button>`;

    const filasHtml = filasOrdenadas.map(([k, g]) => filaNivelHtml(cadenaKey, i, g.nombre, sumaGrupo(g.filas), !esUltimo, k)).join('');
    htmlTotal += `<div class="cadena-nivel">
      <div class="cadena-titulo"><span>${titulo}</span>${volverBtn}${ajustarBtn}</div>
      <div style="overflow:auto;max-height:340px"><table class="res"><thead><tr>
        <th></th><th>${compEtiquetaMes}</th><th>Ventas Promedio</th><th>Presupuesto Sugerido</th><th>Var. % vs Mes</th><th>Var. % vs Promedio</th>
      </tr></thead><tbody>${filasHtml}</tbody></table></div></div>`;

    if (nav[i] == null) break; // no se ha drillado más allá de este nivel -> no mostrar los siguientes
  }
  host.innerHTML = htmlTotal;
}

function renderTodasLasCadenas(){
  if (!compFilas.length) return;
  const m = METRICAS[compMetrica];
  const tot = sumaGrupo(compFilas);
  const cMes = pctCrecimiento(tot[m.sugerido], tot[m.comparar]);
  const cProm = pctCrecimiento(tot[m.sugerido], tot[m.promedio]);
  const etiquetaSug = compEsPromedioMensual ? 'Sugerido (promedio mensual)' : 'Sugerido (total del período)';
  const ambitoId = registrarAmbito({});
  document.getElementById('tarjetaGeneral').innerHTML = `
    <div class="tarjeta"><div class="lbl">${compEtiquetaMes}</div><div class="val">${fmt(tot[m.comparar])}</div><div class="sub3">${m.lbl}</div></div>
    <div class="tarjeta"><div class="lbl">Ventas Promedio (base)</div><div class="val">${fmt(tot[m.promedio])}</div><div class="sub3">${m.lbl}</div></div>
    <div class="tarjeta"><div class="lbl">${etiquetaSug}</div><div class="val">${fmt(tot[m.sugerido])}</div>
      <div class="sub3">vs mes: <span class="pct2 ${cMes.cls}">${cMes.txt}</span> · vs promedio: <span class="pct2 ${cProm.cls}">${cProm.txt}</span>
      <button class="btn-out" style="height:22px;padding:1px 8px;font-size:10.5px;margin-left:8px" onclick="cadenaAjustar('${ambitoId}','toda la Compañía')">🎯 Sugerir para toda la compañía</button></div></div>`;
  renderNivelCadena('centro'); renderNivelCadena('canal'); renderNivelCadena('suplidor');
}
