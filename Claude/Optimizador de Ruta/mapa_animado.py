"""
Generador de HTML con mapa animado Leaflet para visualización de rutas.
Incluye:
  - Marcadores numerados con RutPosicion
  - Dos capas: ruta actual (azul) y ruta recomendada (verde)
  - Animación Play/Pausa/Reset con vehículo en movimiento
  - Velocidad ajustable (muy lento → muy rápido)
  - Pin de inicio arrastrable (recalcula desde el cliente más cercano)
  - Pin de fin configurable (por código/selector o arrastrable)
  - Sin pin de fin → termina en el extremo geográfico opuesto al inicio
  - Polilínea real por carretera si viene de ORS; fallback línea recta
"""

import json


def make_animated_map(
    route_actual: list,   # [{'id','nombre','lat','lon','cla','cla_id','pos_actual'}, ...]
    route_rec:    list,   # igual pero con 'pos_rec'
    poly_actual:  list,   # [[lat,lon], ...] polilínea real actual  (puede ser [])
    poly_rec:     list,   # [[lat,lon], ...] polilínea real recomendada
    rep_code:     str = "",
    dia:          str = "",
    method:       str = "haversine",
) -> str:
    """Devuelve HTML completo autocontenido con el mapa animado."""

    # Calcular centro
    all_pts = route_actual + route_rec
    if all_pts:
        clat = sum(c["lat"] for c in all_pts) / len(all_pts)
        clon = sum(c["lon"] for c in all_pts) / len(all_pts)
    else:
        clat, clon = 18.5, -69.9

    # Si no hay polilínea ORS usamos sólo los puntos de los clientes
    if not poly_actual and route_actual:
        poly_actual = [[c["lat"], c["lon"]] for c in route_actual]
    if not poly_rec and route_rec:
        poly_rec    = [[c["lat"], c["lon"]] for c in route_rec]

    data = {
        "center":       [clat, clon],
        "route_actual": route_actual,
        "route_rec":    route_rec,
        "poly_actual":  poly_actual,
        "poly_rec":     poly_rec,
        "rep":          rep_code,
        "dia":          dia,
        "method":       method,
    }
    data_json = json.dumps(data, ensure_ascii=False)

    return f"""<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1.0">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Tahoma,sans-serif;background:#0d1117;color:#e6edf3;display:flex;flex-direction:column;height:100vh;overflow:hidden}}
#topbar{{background:#161b22;border-bottom:1px solid #30363d;padding:7px 12px;display:flex;align-items:center;gap:10px;flex-wrap:wrap;z-index:1000}}
#map{{flex:1;min-height:0}}
.btn{{background:#238636;border:none;color:#fff;padding:5px 13px;border-radius:6px;cursor:pointer;font-size:12px;font-weight:700;white-space:nowrap}}
.btn:hover{{background:#2ea043}}
.btn.red{{background:#da3633}}.btn.red:hover{{background:#f85149}}
.btn.gray{{background:#21262d;color:#8b949e;border:1px solid #30363d}}.btn.gray:hover{{background:#30363d;color:#e6edf3}}
.sep{{width:1px;height:22px;background:#30363d;margin:0 2px}}
.lbl{{font-size:11px;color:#8b949e;white-space:nowrap}}
select,input[type=text]{{background:#21262d;border:1px solid #30363d;color:#e6edf3;border-radius:5px;padding:3px 7px;font-size:11px;outline:none}}
select:focus,input:focus{{border-color:#58a6ff}}
.speed-wrap{{display:flex;align-items:center;gap:5px}}
.speed-btn{{background:#21262d;border:1px solid #30363d;color:#8b949e;padding:3px 8px;border-radius:4px;cursor:pointer;font-size:10px;font-weight:600}}
.speed-btn.active{{background:#1f6feb;border-color:#388bfd;color:#fff}}
.route-toggle{{display:flex;gap:3px}}
.rtbtn{{background:#21262d;border:1px solid #30363d;color:#8b949e;padding:3px 9px;border-radius:4px;cursor:pointer;font-size:10px;font-weight:600}}
.rtbtn.on-act{{background:#152535;border-color:#3498db;color:#3498db}}
.rtbtn.on-rec{{background:#152518;border-color:#27ae60;color:#27ae60}}
#progress{{font-size:11px;color:#58a6ff;min-width:140px;padding:3px 8px;background:#0d1117;border-radius:4px;border:1px solid #21262d}}
#method-badge{{font-size:10px;padding:2px 7px;border-radius:10px;white-space:nowrap}}
.badge-ors{{background:#1f3c6e;color:#79b8ff;border:1px solid #1f6feb}}
.badge-hav{{background:#2d2000;color:#e3b341;border:1px solid #d29922}}
/* Leaflet popup customization */
.leaflet-popup-content-wrapper{{background:#1c2128;color:#e6edf3;border:1px solid #30363d;border-radius:8px}}
.leaflet-popup-tip{{background:#1c2128}}
.cli-popup b{{color:#58a6ff}}
</style>
</head>
<body>

<div id="topbar">
  <!-- Play controls -->
  <button class="btn" id="btn-play" onclick="togglePlay()">&#9654; Play</button>
  <button class="btn red" onclick="resetAnim()">&#8635; Reset</button>
  <div class="sep"></div>

  <!-- Velocidad -->
  <span class="lbl">Velocidad:</span>
  <div class="speed-wrap" id="speed-wrap">
    <button class="speed-btn" onclick="setSpeed(3000,'muy-lento',this)">Muy lento</button>
    <button class="speed-btn" onclick="setSpeed(1400,'lento',this)">Lento</button>
    <button class="speed-btn active" id="spd-normal" onclick="setSpeed(600,'normal',this)">Normal</button>
    <button class="speed-btn" onclick="setSpeed(220,'rapido',this)">Rápido</button>
    <button class="speed-btn" onclick="setSpeed(80,'muy-rapido',this)">Muy rápido</button>
  </div>
  <div class="sep"></div>

  <!-- Toggle capas -->
  <div class="route-toggle">
    <button class="rtbtn on-act" id="tb-act" onclick="toggleLayer('act')">&#9632; Actual</button>
    <button class="rtbtn on-rec" id="tb-rec" onclick="toggleLayer('rec')">&#9632; Óptima</button>
  </div>
  <div class="sep"></div>

  <!-- Inicio -->
  <span class="lbl">Inicio:</span>
  <select id="sel-start" onchange="onChangeStart()" style="max-width:150px"></select>

  <!-- Fin -->
  <span class="lbl">Fin:</span>
  <select id="sel-end" onchange="onChangeEnd()" style="max-width:150px">
    <option value="">Auto (opuesto)</option>
  </select>
  <div class="sep"></div>

  <!-- Progreso -->
  <span id="progress">&#9679; Listo</span>

  <!-- Método -->
  <span id="method-badge" class="{('badge-ors' if method.startswith('ors') else 'badge-hav')}">{('&#127758; ORS — Carretera real' if method.startswith('ors') else '&#8968; Línea recta')}</span>
</div>

<div id="map"></div>

<script>
// ── Datos embebidos ───────────────────────────────────────────────────────────
const D = {data_json};

// ── Estado ────────────────────────────────────────────────────────────────────
let playing    = false;
let step       = 0;
let stepDelay  = 600;
let animTimer  = null;
let startIdx   = 0;
let endIdx     = null;   // null = auto
let showAct    = true;
let showRec    = true;

// ── Mapa base ─────────────────────────────────────────────────────────────────
const map = L.map('map', {{zoomControl:true}}).setView(D.center, 13);
L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png',{{
  attribution:'© OpenStreetMap',maxZoom:19
}}).addTo(map);

// ── Iconos ────────────────────────────────────────────────────────────────────
function mkNumIcon(num, bg, border, sz=24){{
  return L.divIcon({{className:'',iconSize:[sz,sz],iconAnchor:[sz/2,sz/2],
    html:`<div style="background:${{bg}};color:#fff;border-radius:50%;width:${{sz}}px;height:${{sz}}px;
            display:flex;align-items:center;justify-content:center;font-size:${{sz<22?9:11}}px;font-weight:700;
            border:2px solid ${{border}};box-shadow:0 2px 6px rgba(0,0,0,.55)">${{num}}</div>`}});
}}
function mkVehicleIcon(){{
  return L.divIcon({{className:'',iconSize:[36,36],iconAnchor:[18,18],
    html:'<div style="font-size:28px;line-height:1;filter:drop-shadow(0 2px 4px rgba(0,0,0,.7))">&#128663;</div>'}});
}}
const ICON_START = L.divIcon({{className:'',iconSize:[36,36],iconAnchor:[18,36],
  html:'<div style="font-size:30px;filter:drop-shadow(0 2px 5px rgba(0,0,0,.7))">&#128994;</div>'}});
const ICON_END = L.divIcon({{className:'',iconSize:[36,36],iconAnchor:[18,36],
  html:'<div style="font-size:30px;filter:drop-shadow(0 2px 5px rgba(0,0,0,.7))">&#127937;</div>'}});
const ICON_START_ACT = L.divIcon({{className:'',iconSize:[36,36],iconAnchor:[18,36],
  html:'<div style="font-size:30px;filter:drop-shadow(0 2px 5px rgba(0,0,0,.7))">&#128309;</div>'}});
const ICON_END_ACT = L.divIcon({{className:'',iconSize:[36,36],iconAnchor:[18,36],
  html:'<div style="font-size:30px;filter:drop-shadow(0 2px 5px rgba(0,0,0,.7))">&#9873;</div>'}});

// ── Capas de polilínea ────────────────────────────────────────────────────────
const lgAct = L.layerGroup().addTo(map);
const lgRec = L.layerGroup().addTo(map);
const lgVeh = L.layerGroup().addTo(map);   // vehículo + pins

let polyActLine = null, polyRecLine = null;

function drawPolylines(){{
  lgAct.clearLayers(); lgRec.clearLayers();
  if(D.poly_actual.length>1){{
    polyActLine = L.polyline(D.poly_actual,{{color:'#3498db',weight:3,opacity:0.65,dashArray:'7,5'}})
      .bindTooltip('Ruta Actual',{{sticky:true}});
    if(showAct) lgAct.addLayer(polyActLine);
  }}
  if(D.poly_rec.length>1){{
    polyRecLine = L.polyline(D.poly_rec,{{color:'#27ae60',weight:3.5,opacity:0.75}})
      .bindTooltip('Ruta Óptima',{{sticky:true}});
    if(showRec) lgRec.addLayer(polyRecLine);
  }}
}}
drawPolylines();

// ── Marcadores de clientes ────────────────────────────────────────────────────
const mActual = [];  // markers for route_actual
const mRec    = [];  // markers for route_rec

function claCls(cla_id){{
  const colors=['','#1a9e3f','#5bc43f','#e3b341','#f08519','#e53e3e','#8b0000'];
  return colors[Math.min(cla_id||9, 6)] || '#95a5a6';
}}

function drawClientMarkers(){{
  // Limpiar
  mActual.forEach(m=>lgAct.removeLayer(m)); mActual.length=0;
  mRec.forEach(m=>lgRec.removeLayer(m));    mRec.length=0;

  D.route_actual.forEach((c,i)=>{{
    const m = L.marker([c.lat,c.lon],{{icon:mkNumIcon(c.pos_actual,'#2980b9','#85c1e9')}})
      .bindPopup(`<div class="cli-popup"><b>[${{c.pos_actual}}] ${{c.nombre}}</b><br>
        Código: ${{c.id}}<br>Clasif: ${{c.cla}} (ClaID ${{c.cla_id}})<br>
        <span style="color:#3498db">Ruta Actual — posición ${{c.pos_actual}}</span></div>`,
        {{maxWidth:220}});
    mActual.push(m);
    if(showAct) lgAct.addLayer(m);
  }});

  D.route_rec.forEach((c,i)=>{{
    const m = L.marker([c.lat,c.lon],{{icon:mkNumIcon(c.pos_rec,'#27ae60','#82e0aa')}})
      .bindPopup(`<div class="cli-popup"><b>[${{c.pos_rec}}] ${{c.nombre}}</b><br>
        Código: ${{c.id}}<br>Clasif: ${{c.cla}} (ClaID ${{c.cla_id}})<br>
        <span style="color:#27ae60">Ruta Óptima — posición ${{c.pos_rec}}</span><br>
        ${{c.pos_actual!==c.pos_rec?'<span style="color:#f39c12">&#9888; Cambio pos: '+c.pos_actual+' → '+c.pos_rec+'</span>':'&#10003; Sin cambio'}}</div>`,
        {{maxWidth:220}});
    mRec.push(m);
    if(showRec) lgRec.addLayer(m);
  }});
}}
drawClientMarkers();

// ── Pins arrastables inicio/fin ───────────────────────────────────────────────
let pinS=null, pinE=null;

function getOrderedRoute(){{
  const r = D.route_rec;
  if(!r.length) return [];
  let ord = [...r];
  // Rotar para que empiece en startIdx
  ord = [...ord.slice(startIdx),...ord.slice(0,startIdx)];

  if(endIdx!==null && endIdx!==startIdx){{
    const endCli = r[endIdx];
    const ei = ord.findIndex(c=>c.id===endCli.id);
    if(ei>-1){{ const [rem]=ord.splice(ei,1); ord.push(rem); }}
  }} else {{
    // Auto: poner el más lejano al inicio al final
    if(ord.length>1){{
      const s=ord[0];
      let maxD=-1, fIdx=ord.length-1;
      ord.forEach((c,i)=>{{
        if(i===0) return;
        const d=Math.hypot(c.lat-s.lat,c.lon-s.lon);
        if(d>maxD){{maxD=d;fIdx=i;}}
      }});
      const [far]=ord.splice(fIdx,1); ord.push(far);
    }}
  }}
  return ord;
}}

function updatePins(){{
  lgVeh.clearLayers();
  if(vehicleM){{ vehicleM=null; }}
  const ord=getOrderedRoute();
  if(!ord.length) return;

  pinS=L.marker([ord[0].lat,ord[0].lon],{{icon:ICON_START,draggable:true,zIndexOffset:2000}})
    .bindTooltip('&#128994; Inicio de ruta — arrastra para cambiar',{{permanent:false}});
  pinS.on('dragend',e=>{{
    const ll=e.target.getLatLng();
    const ni=findNearest(ll.lat,ll.lng,D.route_rec);
    document.getElementById('sel-start').value=ni;
    startIdx=ni; updatePins(); resetAnim();
  }});
  lgVeh.addLayer(pinS);

  const last=ord[ord.length-1];
  pinE=L.marker([last.lat,last.lon],{{icon:ICON_END,draggable:true,zIndexOffset:2000}})
    .bindTooltip('&#127937; Fin de ruta — arrastra para cambiar',{{permanent:false}});
  pinE.on('dragend',e=>{{
    const ll=e.target.getLatLng();
    const ni=findNearest(ll.lat,ll.lng,D.route_rec);
    document.getElementById('sel-end').value=ni;
    endIdx=ni; updatePins(); resetAnim();
  }});
  lgVeh.addLayer(pinE);
}}

function findNearest(lat,lng,r){{
  let best=0,bd=Infinity;
  r.forEach((c,i)=>{{const d=Math.hypot(c.lat-lat,c.lon-lng);if(d<bd){{bd=d;best=i;}}}});
  return best;
}}

// ── Vehículo ──────────────────────────────────────────────────────────────────
let vehicleM = null;

function ensureVehicle(){{
  if(!vehicleM){{
    const ord=getOrderedRoute();
    if(!ord.length) return;
    vehicleM=L.marker([ord[0].lat,ord[0].lon],{{icon:mkVehicleIcon(),zIndexOffset:3000}});
    lgVeh.addLayer(vehicleM);
  }}
}}

// ── Animación ─────────────────────────────────────────────────────────────────
// Segmentos de la polilínea ORS indexados por cliente
// Precalculamos qué índice de D.poly_rec corresponde a cada cliente
let polySegments = []; // [startPolyIdx para cada cliente en route_rec]

function buildPolySegments(){{
  // Para cada cliente en route_rec, encontrar el punto más cercano en la polilínea
  const poly = D.poly_rec;
  polySegments = D.route_rec.map(c=>{{
    let bi=0, bd=Infinity;
    poly.forEach(([lat,lon],i)=>{{
      const d=Math.hypot(lat-c.lat,lon-c.lon);
      if(d<bd){{bd=d;bi=i;}}
    }});
    return bi;
  }});
}}
if(D.poly_rec.length>1) buildPolySegments();

function togglePlay(){{
  playing=!playing;
  document.getElementById('btn-play').innerHTML=playing?'&#9646;&#9646; Pausa':'&#9654; Play';
  if(playing){{
    ensureVehicle();
    if(step===0) resetHighlights();
    doStep();
  }} else {{
    clearTimeout(animTimer);
  }}
}}

function resetAnim(){{
  playing=false;
  step=0;
  clearTimeout(animTimer);
  if(vehicleM){{ lgVeh.removeLayer(vehicleM); vehicleM=null; }}
  document.getElementById('btn-play').innerHTML='&#9654; Play';
  document.getElementById('progress').innerHTML='&#9679; Listo';
  resetHighlights();
  updatePins();
}}

function resetHighlights(){{
  mRec.forEach(m=>{{ m.setOpacity(1); const e=m.getElement(); if(e) e.style.filter=''; }});
  mActual.forEach(m=>{{ m.setOpacity(1); const e=m.getElement(); if(e) e.style.filter=''; }});
}}

function doStep(){{
  if(!playing) return;
  const ord=getOrderedRoute();
  if(step>=ord.length){{
    playing=false;
    document.getElementById('btn-play').innerHTML='&#9654; Play';
    document.getElementById('progress').innerHTML='&#10003; Ruta completa';
    return;
  }}
  const cli=ord[step];
  ensureVehicle();

  // Animar vehículo hacia el cliente actual
  animateTo(vehicleM,[cli.lat,cli.lon], Math.min(stepDelay*0.65,500), ()=>{{
    map.panTo([cli.lat,cli.lon],{{animate:true,duration:0.25}});
    highlightClient(step,ord);
    document.getElementById('progress').innerHTML=
      `&#8594; ${{step+1}}/${{ord.length}}: <b>${{cli.nombre.slice(0,22)}}</b> [pos ${{cli.pos_rec||cli.pos_actual}}]`;
    step++;
    if(playing) animTimer=setTimeout(doStep, stepDelay*0.35);
  }});
}}

function animateTo(marker,target,dur,cb){{
  const t0=performance.now();
  const sl=marker.getLatLng();
  function frame(now){{
    const t=Math.min((now-t0)/dur,1);
    const e=t<.5?2*t*t:-1+(4-2*t)*t;
    marker.setLatLng([sl.lat+(target[0]-sl.lat)*e, sl.lng+(target[1]-sl.lng)*e]);
    t<1?requestAnimationFrame(frame):cb();
  }}
  requestAnimationFrame(frame);
}}

function highlightClient(i,ord){{
  const cli=ord[i];
  // Encontrar el marker en mRec
  D.route_rec.forEach((c,mi)=>{{
    const m=mRec[mi];
    if(!m) return;
    if(c.id===cli.id){{
      const e=m.getElement(); if(e) e.style.filter='brightness(1.5) drop-shadow(0 0 8px gold)';
    }} else {{
      const visited=ord.slice(0,i+1).map(x=>x.id);
      if(visited.includes(c.id)) m.setOpacity(0.35);
    }}
  }});
}}

// ── Toggle capas ──────────────────────────────────────────────────────────────
function toggleLayer(which){{
  if(which==='act'){{
    showAct=!showAct;
    const btn=document.getElementById('tb-act');
    btn.className='rtbtn'+(showAct?' on-act':'');
    if(showAct){{
      if(polyActLine) lgAct.addLayer(polyActLine);
      mActual.forEach(m=>lgAct.addLayer(m));
    }}else{{
      if(polyActLine) lgAct.removeLayer(polyActLine);
      mActual.forEach(m=>lgAct.removeLayer(m));
    }}
  }}else{{
    showRec=!showRec;
    const btn=document.getElementById('tb-rec');
    btn.className='rtbtn'+(showRec?' on-rec':'');
    if(showRec){{
      if(polyRecLine) lgRec.addLayer(polyRecLine);
      mRec.forEach(m=>lgRec.addLayer(m));
    }}else{{
      if(polyRecLine) lgRec.removeLayer(polyRecLine);
      mRec.forEach(m=>lgRec.removeLayer(m));
    }}
  }}
}}

// ── Velocidad ─────────────────────────────────────────────────────────────────
function setSpeed(ms,id,el){{
  stepDelay=ms;
  document.querySelectorAll('.speed-btn').forEach(b=>b.classList.remove('active'));
  el.classList.add('active');
}}

// ── Selectores inicio/fin ─────────────────────────────────────────────────────
function populateSelects(){{
  const ss=document.getElementById('sel-start');
  const es=document.getElementById('sel-end');
  ss.innerHTML=''; es.innerHTML='<option value="">Auto (opuesto)</option>';
  D.route_rec.forEach((c,i)=>{{
    ss.add(new Option(`${{c.pos_rec}} — ${{c.nombre.slice(0,22)}}`,i));
    es.add(new Option(`${{c.pos_rec}} — ${{c.nombre.slice(0,22)}}`,i));
  }});
}}
function onChangeStart(){{
  startIdx=parseInt(document.getElementById('sel-start').value)||0;
  updatePins(); resetAnim();
}}
function onChangeEnd(){{
  const v=document.getElementById('sel-end').value;
  endIdx=v===''?null:parseInt(v);
  updatePins(); resetAnim();
}}

// ── Init ──────────────────────────────────────────────────────────────────────
populateSelects();
updatePins();
if(D.poly_rec.length>1){{
  try{{ map.fitBounds(L.polyline(D.poly_rec).getBounds().pad(0.06)); }}catch(e){{}}
}} else if(D.route_rec.length>0){{
  map.setView([D.route_rec[0].lat,D.route_rec[0].lon],13);
}}
</script>
</body>
</html>"""
