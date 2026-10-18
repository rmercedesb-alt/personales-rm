<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="inc/config.asp"-->
<!--#include file="inc/auth.asp"-->
<!--#include file="inc/empresa.asp"-->
<%
Response.Buffer  = True
Response.CharSet = "utf-8"
RequireLogin()
%><!DOCTYPE html>
<html lang="es">
<head>
<meta charset="UTF-8">
<title>Dashboard Visitas &mdash; AM COMERCIAL</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{background:#0d1117;color:#e6edf3;font-family:'Segoe UI',Tahoma,sans-serif;padding:14px 20px;height:100vh;overflow:hidden;display:flex;flex-direction:column}

/* ── COMPANY HEADER ── */
.co-header{display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;padding-bottom:12px;border-bottom:1px solid #21262d;flex-shrink:0}
.co-left{display:flex;align-items:center;gap:14px}
.co-name{font-size:19px;font-weight:800;color:#e6edf3;letter-spacing:.4px}
.co-subtitle{font-size:12px;color:#58a6ff;font-weight:500;margin-top:3px}
.co-right{display:flex;flex-direction:column;align-items:flex-end;gap:3px}
.co-clock{font-family:monospace;font-size:21px;color:#58a6ff;font-weight:700}
.live{display:flex;align-items:center;gap:7px;font-size:11px;color:#8b949e}
.dot{width:8px;height:8px;background:#3fb950;border-radius:50%;animation:pulse 2s infinite;flex-shrink:0}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.25}}

/* ── CARDS ── */
.cards{display:grid;grid-template-columns:repeat(6,1fr);gap:11px;margin-bottom:12px;flex-shrink:0}
.card{background:#161b22;border:1px solid #30363d;border-radius:10px;padding:12px 14px;position:relative;overflow:hidden}
.card::before{content:'';position:absolute;top:0;left:0;right:0;height:3px;border-radius:10px 10px 0 0}
.c-blue::before  {background:#58a6ff}
.c-yellow::before{background:#e3b341}
.c-green::before {background:#3fb950}
.c-orange::before{background:#f0883e}
.c-red::before   {background:#b71c1c}
.card-lbl{font-size:10px;color:#8b949e;text-transform:uppercase;letter-spacing:.7px;margin-bottom:6px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.card-val{font-size:42px;font-weight:700;line-height:1;margin-bottom:4px;transition:font-size .15s}
.c-blue   .card-val{color:#58a6ff}
.c-yellow .card-val{color:#e3b341}
.c-green  .card-val{color:#3fb950}
.c-orange .card-val{color:#f0883e}
.c-red    .card-val{color:#ff7b7b}
.card-hint{font-size:10px;color:#8b949e;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.urgente-list{margin-top:6px}
.urgente-item{font-size:11px;padding:4px 0;border-bottom:1px solid #21262d;color:#ffaaaa;line-height:1.35}
.urgente-item:last-child{border-bottom:none}
.urgente-time{color:#8b949e;font-family:monospace;font-size:10px}
.urgente-empty{font-size:12px;color:#3fb950;margin-top:6px}
.top3-list{margin-top:6px}
.top3-item{font-size:11px;padding:4px 0;border-bottom:1px solid #21262d;color:#e6edf3;line-height:1.35}
.top3-item:last-child{border-bottom:none}
.top3-num{color:#ff7b7b;font-weight:800;font-size:12px;margin-right:5px}

/* ── TABLE WRAPPER ── */
.tbl-wrap{background:#161b22;border:1px solid #30363d;border-radius:10px;overflow:hidden;flex:1;display:flex;flex-direction:column;min-height:0}
.tbl-bar{display:flex;justify-content:space-between;align-items:center;padding:10px 16px;border-bottom:1px solid #30363d;flex-shrink:0}
.tbl-bar h2{font-size:13px;font-weight:600}
.cd-badge  {background:#21262d;border-radius:20px;padding:3px 12px;font-size:11px;color:#8b949e;white-space:nowrap}
.hist-badge{background:#2d2000;color:#e3b341;border-radius:20px;padding:3px 12px;font-size:11px;white-space:nowrap}

/* ── FILTER BAR ── */
.filter-bar{display:flex;gap:8px;align-items:center;padding:9px 16px;border-bottom:1px solid #30363d;flex-wrap:wrap;background:#0d1117;flex-shrink:0}
.f-lbl{font-size:11px;color:#8b949e;white-space:nowrap}
.filter-bar input[type=date],
.filter-bar input[type=text],
.filter-bar select{background:#21262d;border:1px solid #30363d;color:#e6edf3;border-radius:6px;padding:4px 8px;font-size:12px;outline:none;transition:border .15s;height:29px}
.filter-bar input[type=date]:focus,
.filter-bar input[type=text]:focus,
.filter-bar select:focus{border-color:#58a6ff}
.filter-bar input[type=text]{width:185px}
.filter-bar select{min-width:165px}
.filter-bar input[type=date]{width:138px;color-scheme:dark}
.f-count{font-size:11px;color:#8b949e;margin-left:auto;white-space:nowrap}
.f-sep{width:1px;height:18px;background:#30363d;flex-shrink:0}
.btn-clear{background:none;border:1px solid #30363d;color:#8b949e;border-radius:6px;padding:4px 10px;font-size:11px;cursor:pointer;height:29px;transition:all .15s}
.btn-clear:hover{border-color:#58a6ff;color:#58a6ff}
.btn-filter{background:none;border:1px solid #30363d;color:#8b949e;border-radius:6px;padding:4px 11px;font-size:11px;cursor:pointer;height:29px;transition:all .15s;white-space:nowrap;font-weight:500}
.btn-filter:hover{border-color:#8b949e;color:#e6edf3}
.btn-filter.fa-yellow{background:#2d2200;border-color:#e3b341;color:#e3b341;font-weight:700}
.btn-filter.fa-orange{background:#2d1400;border-color:#f0883e;color:#f0883e;font-weight:700}
/* Multi-select supervisor */
.ms-wrap{position:relative;flex-shrink:0}
.ms-trigger{background:#21262d;border:1px solid #30363d;color:#e6edf3;border-radius:6px;padding:0 10px;font-size:12px;cursor:pointer;height:29px;display:flex;align-items:center;gap:6px;min-width:195px;transition:border .15s;text-align:left}
.ms-trigger:hover,.ms-trigger.ms-open{border-color:#58a6ff}
.ms-trigger.ms-active{color:#58a6ff;border-color:#388bfd}
#ms-label{flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.ms-arrow{color:#8b949e;font-size:10px;flex-shrink:0;transition:transform .15s}
.ms-trigger.ms-open .ms-arrow{transform:rotate(180deg)}
.ms-drop{position:absolute;top:calc(100% + 4px);left:0;background:#1c2128;border:1px solid #30363d;border-radius:8px;min-width:220px;max-height:230px;overflow-y:auto;z-index:200;display:none;box-shadow:0 8px 24px rgba(0,0,0,.6)}
.ms-drop.ms-open{display:block}
.ms-opt{display:flex;align-items:center;gap:9px;padding:6px 12px;font-size:12px;cursor:pointer;user-select:none;transition:background .1s}
.ms-opt:hover{background:#21262d}
.ms-opt input[type=checkbox]{accent-color:#58a6ff;cursor:pointer;flex-shrink:0;width:13px;height:13px}
.ms-divider{border:none;border-top:1px solid #30363d;margin:2px 0}
.ms-drop::-webkit-scrollbar{width:5px}
.ms-drop::-webkit-scrollbar-thumb{background:#30363d;border-radius:3px}
/* View toggle */
.view-toggle{display:flex;border:1px solid #30363d;border-radius:6px;overflow:hidden;flex-shrink:0}
.view-btn{background:transparent;border:none;color:#8b949e;padding:0 14px;font-size:11px;cursor:pointer;font-weight:500;transition:all .15s;height:29px;white-space:nowrap}
.view-btn.active{background:#1f3b5c;color:#58a6ff;font-weight:700}
.view-btn:not(.active):hover{background:#21262d;color:#e6edf3}

/* ── TABLE ── */
table{width:100%;border-collapse:collapse}
thead th{padding:8px 13px;text-align:left;font-size:10px;color:#8b949e;text-transform:uppercase;letter-spacing:.4px;background:#1c2128;border-bottom:1px solid #30363d;font-weight:600;white-space:nowrap;cursor:pointer;user-select:none;transition:background .15s,color .15s;position:sticky;top:0;z-index:10}
thead th:hover{background:#24292f;color:#c9d1d9}
thead th .arr{margin-left:4px;opacity:.3;font-style:normal}
thead th.active-col{color:#58a6ff}
thead th.active-col .arr{opacity:1;color:#58a6ff}
thead th.no-sort{cursor:default}
thead th.no-sort:hover{background:#1c2128;color:#8b949e}
tbody td{padding:7px 13px;border-bottom:1px solid #1c2128;font-size:12px;vertical-align:middle}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover td{background:rgba(255,255,255,.018)}
.c-cod{font-weight:700;color:#8b949e;white-space:nowrap}
.c-nom{font-weight:500}
.c-sup{font-size:11px;color:#8b949e}
/* Resumido total row */
.row-total td{background:#1c2128;border-top:2px solid #30363d}
.row-total .c-sup{color:#58a6ff;font-weight:800;font-size:12px;letter-spacing:.5px}

/* ── BADGES ── */
.badge{display:inline-block;padding:3px 9px;border-radius:4px;font-weight:700;font-size:11px;font-family:monospace;min-width:78px;text-align:center;letter-spacing:.3px}
.vg  {background:#1a5c1a;color:#7ee07e}
.vy  {background:#5a4200;color:#ffd54f}
.vly {background:#4a3800;color:#ffe082}
.vlr {background:#5a2020;color:#ef9a9a}
.vdr {background:#8b1515;color:#fff}
.bneutral{background:#21262d;color:#8b949e}
.badge.empty{color:transparent!important;min-width:78px}

/* ── SCROLL AREA ── */
#tbl-cont{flex:1;overflow-y:auto;min-height:0}
#tbl-cont::-webkit-scrollbar{width:6px}
#tbl-cont::-webkit-scrollbar-track{background:transparent}
#tbl-cont::-webkit-scrollbar-thumb{background:#30363d;border-radius:3px}
#tbl-cont::-webkit-scrollbar-thumb:hover{background:#58a6ff}

/* ── ERROR / LOADING ── */
.err{background:#3a1515;border:1px solid #7a1010;border-radius:8px;padding:8px 14px;margin-bottom:10px;font-size:12px;display:none;flex-shrink:0}
.loading{text-align:center;padding:36px;color:#8b949e;font-size:13px}
</style>
</head>
<body>

<!-- COMPANY HEADER -->
<header class="co-header">
  <div class="co-left">
    <% If g_EmpHasLogo Then %>
    <img src="api/logo.asp" alt="Logo" style="height:46px;width:auto;border-radius:6px;object-fit:contain">
    <% Else %>
    <svg width="70" height="46" viewBox="0 0 70 46" xmlns="http://www.w3.org/2000/svg">
      <defs><clipPath id="lc"><rect width="70" height="46" rx="6"/></clipPath></defs>
      <rect width="70" height="46" rx="6" fill="#0f1922"/>
      <g clip-path="url(#lc)">
        <polygon points="1,0 15,0 25,46 11,46"  fill="#2196F3"/>
        <polygon points="19,0 28,0 38,46 29,46" fill="#43A047"/>
        <polygon points="32,0 41,0 51,46 42,46" fill="#43A047"/>
        <polygon points="45,0 54,0 64,46 55,46" fill="#43A047"/>
        <polygon points="58,0 67,0 77,46 68,46" fill="#43A047"/>
      </g>
    </svg>
    <% End If %>
    <div>
      <div class="co-name"><%= Server.HtmlEncode(g_EmpNombre) %></div>
      <div class="co-subtitle">Dashboard Visitas en Vivo</div>
    </div>
  </div>
  <div class="co-right">
    <div style="display:flex;align-items:center;gap:12px">
      <div style="text-align:right">
        <div class="live"><div class="dot"></div><span id="live-txt">Conectando...</span></div>
        <div style="font-size:11px;color:#8b949e;margin-top:2px">
          <span id="user-nombre"></span>
        </div>
      </div>
      <a href="logout.asp" title="Cerrar sesion" style="background:#21262d;border:1px solid #30363d;color:#8b949e;border-radius:6px;padding:5px 10px;font-size:11px;text-decoration:none;transition:all .15s;white-space:nowrap;display:inline-block" onmouseover="this.style.borderColor='#f85149';this.style.color='#f85149'" onmouseout="this.style.borderColor='#30363d';this.style.color='#8b949e'">
        &#x2192; Salir
      </a>
    </div>
    <div class="co-clock" id="clock">--:--:--</div>
  </div>
</header>

<div class="err" id="err-bar"></div>

<!-- TARJETAS (6) -->
<div class="cards">
  <div class="card c-blue">
    <div class="card-lbl" id="c1-lbl">Total Representantes</div>
    <div class="card-val" id="c-total">--</div>
    <div class="card-hint" id="c1-hint">Activos en los ultimos 30 dias</div>
  </div>
  <div class="card c-yellow">
    <div class="card-lbl" id="c2-lbl">Primera Visita Tardia</div>
    <div class="card-val" id="c-late">--</div>
    <div class="card-hint" id="c2-hint">Entrada despues de las 8:20 AM</div>
  </div>
  <div class="card c-green">
    <div class="card-lbl" id="c3-lbl">Avg Primer Pedido</div>
    <div class="card-val" id="c-ped">--</div>
    <div class="card-hint" id="c3-hint">Hora promedio del 1er pedido</div>
  </div>
  <div class="card c-orange">
    <div class="card-lbl" id="c4-lbl">+30 Min Sin Visitas</div>
    <div class="card-val" id="c-inact">--</div>
    <div class="card-hint" id="c4-hint">Sin nueva visita en 30 minutos</div>
  </div>
  <div class="card c-red">
    <div class="card-lbl" id="c5-lbl">Llamar Urgente - Top 3</div>
    <div class="urgente-list" id="c-urgent"><span style="color:#8b949e;font-size:11px">Cargando...</span></div>
  </div>
  <div class="card c-red">
    <div class="card-lbl">Top 3 Sin Visitas</div>
    <div class="top3-list" id="c-top3"><span style="color:#8b949e;font-size:11px">Cargando...</span></div>
  </div>
</div>

<!-- TABLA -->
<div class="tbl-wrap">
  <div class="tbl-bar">
    <h2 id="tbl-title">Detalle por Representante &mdash; <span id="lbl-fecha">Hoy</span></h2>
    <div class="cd-badge" id="cd-badge">Actualizacion en <span id="cd">60</span>s</div>
  </div>

  <!-- FILTROS -->
  <div class="filter-bar">
    <span class="f-lbl">Fecha:</span>
    <input type="date" id="f-date">

    <div class="f-sep"></div>

    <span class="f-lbl">Buscar:</span>
    <input type="text" id="f-name" placeholder="Nombre del representante...">

    <div class="f-sep"></div>

    <span class="f-lbl">Supervisor:</span>
    <div class="ms-wrap" id="ms-wrap">
      <button class="ms-trigger" id="ms-trigger" onclick="toggleMsDrop(event)">
        <span id="ms-label">Todos los supervisores</span>
        <span class="ms-arrow">&#9660;</span>
      </button>
      <div class="ms-drop" id="ms-drop">
        <div class="ms-opt" id="ms-opt-all" onclick="toggleAllSups()">
          <input type="checkbox" id="ms-chk-all" checked onclick="event.stopPropagation()">
          <span>Todos los supervisores</span>
        </div>
        <hr class="ms-divider">
        <div id="ms-options"></div>
      </div>
    </div>

    <div class="f-sep"></div>

    <div class="view-toggle">
      <button class="view-btn active" id="btn-det" onclick="setView('detallado')">Detallado</button>
      <button class="view-btn"        id="btn-res" onclick="setView('resumido')">Resumido</button>
    </div>

    <div class="f-sep"></div>
    <button class="btn-filter" id="btn-flate"  onclick="toggleFilterLate()">&#9679; 1a Visita Tardia</button>
    <button class="btn-filter" id="btn-finact" onclick="toggleFilterInact()">&#9679; +30 Min Sin Visita</button>

    <button class="btn-clear" onclick="clearFilters()">Limpiar filtros</button>
    <span class="f-count" id="f-count"></span>
  </div>

  <div id="tbl-cont"><div class="loading">Cargando datos...</div></div>
</div>

<script>
const REFRESH = 60;
let reps        = [];
let countdown   = REFRESH;
let selectedDate = '';
let sortCol     = 'SupervisorNombre';
let sortDir     = 'asc';
let viewMode    = 'detallado';
let selectedSups = new Set();
let filterLate  = false;
let filterInact = false;

/* =========================================================
   HELPERS DE FECHA
   ========================================================= */
function todayStr() {
  return new Date().toISOString().split('T')[0];
}
function isSelectedToday() {
  return selectedDate === todayStr();
}
function pad(n) { return String(n).padStart(2, '0'); }

function referenceTime() {
  return isSelectedToday()
    ? Date.now()
    : new Date(selectedDate + 'T23:59:59').getTime();
}

function hadVisitOnDate(r) {
  return r.UltimaEntrada != null && r.UltimaEntrada.startsWith(selectedDate);
}

function secsSinVisita(r) {
  if (!hadVisitOnDate(r)) return Infinity;
  return (referenceTime() - new Date(r.UltimaEntrada).getTime()) / 1000;
}

/* =========================================================
   COLORES
   ========================================================= */
function clsBySecs(secsFromMidnight) {
  const m = secsFromMidnight / 60;
  if (m < 8 * 60 + 20)  return 'vg';
  if (m < 8 * 60 + 45)  return 'vy';
  if (m < 9 * 60)       return 'vly';
  if (m <= 9 * 60 + 15) return 'vlr';
  return 'vdr';
}
function cls(isoDate) {
  if (!isoDate) return 'vdr';
  const d = new Date(isoDate);
  return clsBySecs(d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds());
}

/* =========================================================
   FORMATO
   ========================================================= */
function hms(sec) {
  if (sec == null || isNaN(sec) || sec < 0) return '--:--:--';
  sec = Math.floor(sec);
  return `${pad(Math.floor(sec / 3600))}:${pad(Math.floor((sec % 3600) / 60))}:${pad(sec % 60)}`;
}
function fmtHora(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}
function isToday(iso) {
  if (!iso) return false;
  return new Date(iso).toDateString() === new Date().toDateString();
}
function safeId(code) {
  return 'el_' + String(code).replace(/\W/g, '_');
}
function fmtFechaLabel(str) {
  if (!str || str === todayStr()) return 'Hoy';
  const [y, m, d] = str.split('-');
  const meses = ['Ene','Feb','Mar','Abr','May','Jun','Jul','Ago','Sep','Oct','Nov','Dic'];
  return `${parseInt(d)} ${meses[parseInt(m) - 1]} ${y}`;
}

/* =========================================================
   PROMEDIOS
   ========================================================= */
function avgSecsOfDay(list, field) {
  const vals = list
    .filter(r => r[field] && r[field].startsWith(selectedDate))
    .map(r => {
      const d = new Date(r[field]);
      return d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds();
    });
  if (!vals.length) return null;
  return vals.reduce((a, b) => a + b, 0) / vals.length;
}
function secsToTimeStr(secs) {
  if (secs == null || isNaN(secs)) return '--:--:--';
  return hms(Math.round(secs));
}

/* =========================================================
   HELPERS PARA TARJETAS
   ========================================================= */
function setCardNum(id, value) {
  const el = document.getElementById(id);
  el.textContent  = value;
  el.style.fontSize = '42px';
}
function setCardTime(id, secs) {
  const el = document.getElementById(id);
  if (secs != null) {
    el.textContent    = secsToTimeStr(secs);
    el.style.fontSize = '22px';
  } else {
    el.textContent    = '--';
    el.style.fontSize = '42px';
  }
}

/* =========================================================
   ORDENAMIENTO
   ========================================================= */
const COLS = [
  { key: 'SupervisorNombre',  type: 'str'  },
  { key: 'RepCodigo',         type: 'str'  },
  { key: 'RepNombre',         type: 'str'  },
  { key: 'PrimeraEntrada',    type: 'date' },
  { key: 'PrimerPedidoHora',  type: 'date' },
  { key: 'DuracionSeg',       type: 'num'  },
  { key: 'UltimaSalida',      type: 'date' },
];

function cmpVal(av, bv, type, dir) {
  if (av == null && bv == null) return 0;
  if (av == null) return dir === 'desc' ? -1 :  1;
  if (bv == null) return dir === 'desc' ?  1 : -1;
  if (type === 'date') { av = new Date(av).getTime(); bv = new Date(bv).getTime(); }
  else if (type === 'num') { av = parseFloat(av); bv = parseFloat(bv); }
  else { av = String(av).toLowerCase(); bv = String(bv).toLowerCase(); }
  if (av < bv) return dir === 'asc' ? -1 :  1;
  if (av > bv) return dir === 'asc' ?  1 : -1;
  return 0;
}

function sortedReps(list) {
  const col = COLS.find(c => c.key === sortCol) || COLS[0];
  return [...list].sort((a, b) => {
    const r1 = cmpVal(a[sortCol], b[sortCol], col.type, sortDir);
    if (r1 !== 0) return r1;
    if (sortCol !== 'SupervisorNombre') {
      const rs = cmpVal(a.SupervisorNombre, b.SupervisorNombre, 'str', 'asc');
      if (rs !== 0) return rs;
    }
    return cmpVal(a.RepCodigo, b.RepCodigo, 'str', 'asc');
  });
}

function setSort(key) {
  if (sortCol === key) {
    sortDir = sortDir === 'asc' ? 'desc' : 'asc';
  } else {
    sortCol = key;
    sortDir = (COLS.find(c => c.key === key) || {}).type === 'str' ? 'asc' : 'desc';
  }
  buildTable();
}

/* =========================================================
   FILTROS
   ========================================================= */
function filteredReps() {
  const name = document.getElementById('f-name').value.toLowerCase().trim();
  const HALF = 30 * 60;
  return reps.filter(r => {
    if (name && !(r.RepNombre || '').toLowerCase().includes(name)) return false;
    if (selectedSups.size > 0 && !selectedSups.has(r.RepSupervisor)) return false;
    if (filterLate) {
      if (!r.PrimeraEntrada) return false;
      const d = new Date(r.PrimeraEntrada);
      if (d.getHours() * 60 + d.getMinutes() < 8 * 60 + 20) return false;
    }
    if (filterInact) {
      if (secsSinVisita(r) <= HALF) return false;
    }
    return true;
  });
}

function toggleFilterLate() {
  filterLate = !filterLate;
  document.getElementById('btn-flate').classList.toggle('fa-yellow', filterLate);
  buildTable();
}

function toggleFilterInact() {
  filterInact = !filterInact;
  document.getElementById('btn-finact').classList.toggle('fa-orange', filterInact);
  buildTable();
}

function clearFilters() {
  document.getElementById('f-name').value = '';
  selectedSups.clear();
  syncMsCheckboxes();
  updateMsLabel();
  filterLate  = false;
  filterInact = false;
  document.getElementById('btn-flate').classList.remove('fa-yellow');
  document.getElementById('btn-finact').classList.remove('fa-orange');
  buildTable();
}

/* ── Multi-select supervisor ── */
function buildSupMap() {
  const map = {};
  reps.forEach(r => { if (r.RepSupervisor) map[r.RepSupervisor] = r.SupervisorNombre || r.RepSupervisor; });
  return map;
}

function syncMsCheckboxes() {
  document.querySelectorAll('.ms-opt-ind').forEach(chk => {
    chk.checked = selectedSups.has(chk.dataset.cod);
  });
  const all = document.getElementById('ms-chk-all');
  if (!all) return;
  if (selectedSups.size === 0) {
    all.checked = true; all.indeterminate = false;
  } else {
    all.checked = false; all.indeterminate = true;
  }
}

function updateMsLabel() {
  const lbl     = document.getElementById('ms-label');
  const trigger = document.getElementById('ms-trigger');
  if (selectedSups.size === 0) {
    lbl.textContent = 'Todos los supervisores';
    trigger.classList.remove('ms-active');
  } else {
    lbl.textContent = selectedSups.size === 1
      ? buildSupMap()[selectedSups.values().next().value] || '1 supervisor'
      : `${selectedSups.size} supervisores`;
    trigger.classList.add('ms-active');
  }
}

function toggleMsDrop(e) {
  e.stopPropagation();
  const drop    = document.getElementById('ms-drop');
  const trigger = document.getElementById('ms-trigger');
  const open    = drop.classList.toggle('ms-open');
  trigger.classList.toggle('ms-open', open);
}

function toggleAllSups() {
  selectedSups.clear();
  syncMsCheckboxes();
  updateMsLabel();
  buildTable();
}

function toggleSup(cod) {
  if (selectedSups.has(cod)) { selectedSups.delete(cod); }
  else                        { selectedSups.add(cod);    }
  syncMsCheckboxes();
  updateMsLabel();
  buildTable();
}

function populateSupervisores() {
  const map  = buildSupMap();
  const keys = new Set(Object.keys(map));
  selectedSups.forEach(s => { if (!keys.has(s)) selectedSups.delete(s); });

  const opts = Object.entries(map).sort((a, b) => a[1].localeCompare(b[1]));
  document.getElementById('ms-options').innerHTML = opts.map(([cod, nom]) =>
    `<div class="ms-opt" onclick="toggleSup('${cod}')">
       <input type="checkbox" class="ms-opt-ind" data-cod="${cod}"
              ${selectedSups.has(cod) ? 'checked' : ''} onclick="event.stopPropagation()">
       <span>${nom}</span>
     </div>`
  ).join('');
  updateMsLabel();
}

/* =========================================================
   VISTA (DETALLADO / RESUMIDO)
   ========================================================= */
function setView(mode) {
  viewMode = mode;
  document.getElementById('btn-det').classList.toggle('active', mode === 'detallado');
  document.getElementById('btn-res').classList.toggle('active', mode === 'resumido');
  buildTable();
}

/* =========================================================
   TARJETAS
   ========================================================= */
function updateCards() {
  const visible = filteredReps();
  const HALF    = 30 * 60;

  if (viewMode === 'resumido') {
    const sups = new Set(visible.map(r => r.RepSupervisor).filter(Boolean));
    setCardNum('c-total', sups.size);
    document.getElementById('c1-lbl').textContent  = 'Total Supervisores';
    document.getElementById('c1-hint').textContent = 'Supervisores en el filtro activo';

    setCardTime('c-late', avgSecsOfDay(visible, 'PrimeraEntrada'));
    document.getElementById('c2-lbl').textContent  = 'Avg Primera Visita';
    document.getElementById('c2-hint').textContent = 'Hora promedio de 1a entrada';

    setCardTime('c-ped', avgSecsOfDay(visible, 'PrimerPedidoHora'));
    document.getElementById('c3-lbl').textContent  = 'Avg Primer Pedido';
    document.getElementById('c3-hint').textContent = 'Hora promedio del 1er pedido';

    const inact = visible.filter(r => secsSinVisita(r) > HALF).length;
    const pct   = visible.length > 0 ? Math.round(inact / visible.length * 100) : 0;
    setCardNum('c-inact', pct + '%');
    document.getElementById('c4-lbl').textContent  = '% +30 Min Sin Visitas';
    document.getElementById('c4-hint').textContent = isSelectedToday()
      ? 'Del total de representantes' : 'Al cierre del dia';

  } else {
    setCardNum('c-total', visible.length);
    document.getElementById('c1-lbl').textContent  = 'Total Representantes';
    document.getElementById('c1-hint').textContent = 'Activos en los ultimos 30 dias';

    const late = visible.filter(r => {
      if (!r.PrimeraEntrada) return false;
      const d = new Date(r.PrimeraEntrada);
      return d.getHours() * 60 + d.getMinutes() >= 8 * 60 + 20;
    }).length;
    setCardNum('c-late', late);
    document.getElementById('c2-lbl').textContent  = 'Primera Visita Tardia';
    document.getElementById('c2-hint').textContent = 'Entrada despues de las 8:20 AM';

    setCardTime('c-ped', avgSecsOfDay(visible, 'PrimerPedidoHora'));
    document.getElementById('c3-lbl').textContent  = 'Avg Primer Pedido';
    document.getElementById('c3-hint').textContent = 'Hora promedio del 1er pedido';

    const inact = visible.filter(r => secsSinVisita(r) > HALF).length;
    setCardNum('c-inact', inact);
    document.getElementById('c4-lbl').textContent  = '+30 Min Sin Visitas';
    document.getElementById('c4-hint').textContent = isSelectedToday()
      ? 'Sin nueva visita en 30 minutos' : 'Al cierre del dia';
  }

  const urgSorted  = [...visible].sort((a, b) => secsSinVisita(b) - secsSinVisita(a));
  const top3       = urgSorted.slice(0, Math.min(3, urgSorted.length));
  const noVisitLbl = isSelectedToday() ? 'Sin visita hoy' : 'Sin visita ese dia';
  const empty      = '<div class="urgente-empty">Todo al dia</div>';

  document.getElementById('c5-lbl').textContent = isSelectedToday()
    ? 'Llamar Urgente - Top 3' : 'Mayor Tiempo Sin Visita';

  document.getElementById('c-urgent').innerHTML = top3.map(r => {
    const secs = secsSinVisita(r);
    const t    = isFinite(secs) ? hms(secs) : noVisitLbl;
    return `<div class="urgente-item"><strong>${r.RepCodigo}</strong> &middot; ${r.RepNombre || '&mdash;'} <span class="urgente-time">(${t})</span></div>`;
  }).join('') || empty;

  document.getElementById('c-top3').innerHTML = top3.map((r, i) =>
    `<div class="top3-item"><span class="top3-num">${i + 1}.</span>${r.RepNombre || r.RepCodigo}</div>`
  ).join('') || empty;
}

/* =========================================================
   TABLA DETALLADO
   ========================================================= */
function buildDetallado(visible) {
  const isHoy = isSelectedToday();

  const headers = [
    { label: 'Supervisor',          key: 'SupervisorNombre' },
    { label: 'Codigo',              key: 'RepCodigo'        },
    { label: 'Nombre',              key: 'RepNombre'        },
    { label: 'Primera Visita',      key: 'PrimeraEntrada'   },
    { label: 'Primer Pedido',       key: 'PrimerPedidoHora' },
    { label: 'Dur. Ultima Visita',  key: 'DuracionSeg'      },
    { label: 'Desde Ultima Visita', key: 'UltimaSalida'     },
  ];

  const thHtml = headers.map(h => {
    const active = sortCol === h.key ? 'active-col' : '';
    const arrow  = sortCol === h.key
      ? (sortDir === 'asc' ? '&#9650;' : '&#9660;')
      : '&#8693;';
    return `<th class="${active}" onclick="setSort('${h.key}')">${h.label}<span class="arr">${arrow}</span></th>`;
  }).join('');

  let rows = '';
  sortedReps(visible).forEach(r => {
    const c      = cls(r.PrimeraEntrada);
    const cPed   = cls(r.PrimerPedidoHora);
    const pv     = r.PrimeraEntrada   ? fmtHora(r.PrimeraEntrada)   : '';
    const pp     = r.PrimerPedidoHora ? fmtHora(r.PrimerPedidoHora) : '';
    const dur    = r.DuracionSeg != null ? hms(parseInt(r.DuracionSeg)) : '';
    const showEl = isHoy && r.UltimaSalida && isToday(r.UltimaSalida);
    const eid    = safeId(r.RepCodigo);
    const supNom = r.SupervisorNombre || r.RepSupervisor || '&mdash;';

    rows += `<tr>
      <td class="c-sup">${supNom}</td>
      <td class="c-cod">${r.RepCodigo}</td>
      <td class="c-nom">${r.RepNombre || ''}</td>
      <td>${pv
        ? `<span class="badge ${c}">${pv}</span>`
        : `<span class="badge vdr empty">&nbsp;</span>`}</td>
      <td>${pp ? `<span class="badge ${cPed}">${pp}</span>` : ''}</td>
      <td>${dur ? `<span class="badge ${c}">${dur}</span>` : ''}</td>
      <td id="${eid}">${showEl ? `<span class="badge ${c}">--:--:--</span>` : ''}</td>
    </tr>`;
  });

  document.getElementById('tbl-cont').innerHTML =
    `<table><thead><tr>${thHtml}</tr></thead><tbody>${rows}</tbody></table>`;
}

/* =========================================================
   TABLA RESUMIDO
   ========================================================= */
function buildResumido(visible) {
  const HALF = 30 * 60;

  const supMap = {};
  visible.forEach(r => {
    const key = r.RepSupervisor || '__none__';
    if (!supMap[key]) supMap[key] = {
      nombre: r.SupervisorNombre || r.RepSupervisor || '(Sin supervisor)',
      reps: []
    };
    supMap[key].reps.push(r);
  });

  function buildResRow(list, nombre, isTotal) {
    if (!list.length) return '';
    const n = list.length;

    const avgPV = avgSecsOfDay(list, 'PrimeraEntrada');

    const conEntrada = list.filter(r => r.PrimeraEntrada && r.PrimeraEntrada.startsWith(selectedDate));
    const aTiempo    = conEntrada.filter(r => {
      const d = new Date(r.PrimeraEntrada);
      return d.getHours() * 60 + d.getMinutes() < 8 * 60 + 20;
    }).length;
    const pctAt = conEntrada.length > 0 ? Math.round(aTiempo / conEntrada.length * 100) : null;

    const avgPP = avgSecsOfDay(list, 'PrimerPedidoHora');

    const durVals = list.filter(r => r.DuracionSeg != null).map(r => parseInt(r.DuracionSeg));
    const avgDur  = durVals.length ? durVals.reduce((a, b) => a + b, 0) / durVals.length : null;

    const sinVals = list.filter(r => isFinite(secsSinVisita(r))).map(r => secsSinVisita(r));
    const avgSin  = sinVals.length ? sinVals.reduce((a, b) => a + b, 0) / sinVals.length : null;

    const pvBadge = avgPV != null
      ? `<span class="badge ${clsBySecs(avgPV)}">${secsToTimeStr(avgPV)}</span>`
      : '<span style="color:#555">&#8212;</span>';

    const ppBadge = avgPP != null
      ? `<span class="badge ${clsBySecs(avgPP)}">${secsToTimeStr(avgPP)}</span>`
      : '<span style="color:#555">&#8212;</span>';

    const durCell = avgDur != null
      ? `<span class="badge bneutral">${hms(avgDur)}</span>`
      : '<span style="color:#555">&#8212;</span>';

    const sinCell = avgSin != null
      ? `<span class="badge ${avgSin > HALF ? 'vdr' : 'vg'}">${hms(avgSin)}</span>`
      : '<span style="color:#555">&#8212;</span>';

    let pctColor = '#8b949e';
    if (pctAt != null) {
      pctColor = pctAt >= 80 ? '#7ee07e' : pctAt >= 50 ? '#ffd54f' : '#ef9a9a';
    }
    const pctCell = pctAt != null
      ? `<span style="font-weight:700;color:${pctColor}">${pctAt}%</span>`
      : '<span style="color:#555">&#8212;</span>';

    return `<tr class="${isTotal ? 'row-total' : ''}">
      <td class="c-sup">${nombre}</td>
      <td class="c-cod" style="text-align:center">${n}</td>
      <td>${pvBadge}</td>
      <td style="text-align:center">${pctCell}</td>
      <td>${ppBadge}</td>
      <td>${durCell}</td>
      <td>${sinCell}</td>
    </tr>`;
  }

  const headers = ['Supervisor', '# Reps', 'Avg 1a Visita', '% A Tiempo', 'Avg 1er Pedido', 'Avg Duracion', 'Avg Ult. Visita'];
  const thHtml  = headers.map(h => `<th class="no-sort">${h}</th>`).join('');

  const sorted   = Object.values(supMap).sort((a, b) => a.nombre.localeCompare(b.nombre));
  const rows     = sorted.map(s => buildResRow(s.reps, s.nombre, false)).join('');
  const totalRow = buildResRow(visible, 'COMPANIA', true);

  document.getElementById('tbl-cont').innerHTML =
    `<table><thead><tr>${thHtml}</tr></thead><tbody>${rows}${totalRow}</tbody></table>`;
}

/* =========================================================
   DISPATCHER DE TABLA
   ========================================================= */
function buildTable() {
  const visible = filteredReps();
  const total   = reps.length;

  document.getElementById('f-count').textContent = visible.length < total
    ? `Mostrando ${visible.length} de ${total} representantes`
    : `${total} representantes`;

  const modeLabel = viewMode === 'resumido' ? 'Resumen por Supervisor' : 'Detalle por Representante';
  document.getElementById('tbl-title').innerHTML =
    `${modeLabel} &mdash; <span id="lbl-fecha">${fmtFechaLabel(selectedDate)}</span>`;

  if (!visible.length) {
    document.getElementById('tbl-cont').innerHTML =
      '<div class="loading">Sin resultados para los filtros aplicados.</div>';
    updateCards();
    return;
  }

  if (viewMode === 'resumido') {
    buildResumido(visible);
  } else {
    buildDetallado(visible);
  }

  updateCards();
}

/* =========================================================
   TICK EN VIVO (cada 1 segundo)
   ========================================================= */
function updateLive() {
  const now   = Date.now();
  const isHoy = isSelectedToday();

  const n = new Date();
  document.getElementById('clock').textContent =
    `${pad(n.getHours())}:${pad(n.getMinutes())}:${pad(n.getSeconds())}`;

  if (!isHoy) return;

  if (viewMode === 'detallado') {
    reps.forEach(r => {
      const cell = document.getElementById(safeId(r.RepCodigo));
      if (!cell) return;
      const span = cell.querySelector('.badge');
      if (!span || !r.UltimaSalida || !isToday(r.UltimaSalida)) return;
      span.textContent = hms((now - new Date(r.UltimaSalida).getTime()) / 1000);
    });
  }

  updateCards();

  countdown--;
  const cdEl = document.getElementById('cd');
  if (cdEl) cdEl.textContent = Math.max(0, countdown);
  if (countdown <= 0) {
    countdown = REFRESH;
    fetchData();
  }
}

/* =========================================================
   FETCH PRINCIPAL
   ========================================================= */
async function fetchData() {
  try {
    const resp = await fetch('api/data.asp?fecha=' + selectedDate);
    if (!resp.ok) throw new Error('HTTP ' + resp.status);
    const json = await resp.json();
    if (!json.ok) throw new Error(json.error || 'Error del servidor');

    reps         = json.reps;
    selectedDate = json.fecha;

    populateSupervisores();
    buildTable();

    const cdBadge = document.getElementById('cd-badge');
    if (isSelectedToday()) {
      cdBadge.className = 'cd-badge';
      cdBadge.innerHTML = `Actualizacion en <span id="cd">${countdown}</span>s`;
    } else {
      cdBadge.className   = 'hist-badge';
      cdBadge.textContent = 'Vista historica';
    }

    document.getElementById('err-bar').style.display = 'none';
    document.getElementById('live-txt').textContent =
      'Actualizado a las ' + new Date().toLocaleTimeString('es-DO');

    if (isSelectedToday()) countdown = REFRESH;
  } catch (e) {
    const bar = document.getElementById('err-bar');
    bar.textContent   = 'Error de conexion: ' + e.message;
    bar.style.display = 'block';
    document.getElementById('live-txt').textContent = 'Sin conexion';
  }
}

/* =========================================================
   INICIALIZACION
   ========================================================= */
function init() {
  const dp  = document.getElementById('f-date');
  const hoy = todayStr();
  const min = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().split('T')[0];
  dp.value  = hoy;
  dp.max    = hoy;
  dp.min    = min;
  selectedDate = hoy;

  dp.addEventListener('change', () => {
    selectedDate = dp.value || hoy;
    countdown    = REFRESH;
    fetchData();
  });

  document.getElementById('f-name').addEventListener('input', buildTable);

  document.addEventListener('click', e => {
    const wrap = document.getElementById('ms-wrap');
    if (wrap && !wrap.contains(e.target)) {
      document.getElementById('ms-drop').classList.remove('ms-open');
      document.getElementById('ms-trigger').classList.remove('ms-open');
    }
  });

  fetch('api/me.asp').then(r => r.json()).then(j => {
    if (j.ok) document.getElementById('user-nombre').textContent = j.nombre || j.login;
  }).catch(() => {});

  fetchData();
  setInterval(updateLive, 1000);
}

init();
</script>
</body>
</html>
