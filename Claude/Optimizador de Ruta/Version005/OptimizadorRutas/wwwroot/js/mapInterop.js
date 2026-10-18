// ── Helpers globales ──────────────────────────────────────────────────────────

// Drag-resize entre mapa y tabla
window.initResize = (handleId, tableId) => {
    const handle = document.getElementById(handleId);
    const table  = document.getElementById(tableId);
    if (!handle || !table) return;

    let startY = 0, startH = 0;

    handle.addEventListener('mousedown', e => {
        e.preventDefault();
        startY = e.clientY;
        startH = table.offsetHeight;

        const onMove = ev => {
            // Arrastrar hacia arriba (delta negativo) → tabla más grande
            const delta = startY - ev.clientY;
            const newH  = Math.max(80, Math.min(window.innerHeight * 0.75, startH + delta));
            table.style.height = newH + 'px';
        };
        const onUp = () => {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup',  onUp);
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup',   onUp);
    });
};

window.getSelectedValues = (elementId) => {
    const el = document.getElementById(elementId);
    if (!el) return [];
    return Array.from(el.selectedOptions).map(o => o.value);
};

window.downloadFile = (filename, base64) => {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const blob  = new Blob([bytes], { type: "application/octet-stream" });
    const url   = URL.createObjectURL(blob);
    const a     = document.createElement("a");
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};

// ── Interop Leaflet ───────────────────────────────────────────────────────────
window.MapInterop = (() => {

    let _map       = null;
    let _markers   = [];
    let _polyline  = null;
    let _allPoints = [];   // { lat, lng, nombre, codigo, cla, dia, pos, cliId }

    // Animación
    let _animTimer   = null;
    let _animStep    = 0;
    let _animMarkers = [];
    let _animPoly    = null;
    let _dotNetRef   = null;

    // Pines arrastrables
    let _pinInicio = null;
    let _pinFin    = null;

    const DayColors = {
        Lunes:"#3498db", Martes:"#e74c3c", Miercoles:"#2ecc71",
        Jueves:"#f39c12", Viernes:"#9b59b6", Sabado:"#1abc9c",
        Domingo:"#95a5a6", default:"#7f8c8d"
    };
    const gc = d => DayColors[d] || DayColors.default;

    // ── Iconos de marcadores (círculos numerados) ─────────────────────────────
    function makeIcon(pos, color, big) {
        const s = big ? 32 : 26;
        return L.divIcon({
            className: "",
            html: `<div style="background:${color};color:#fff;border-radius:50%;
                width:${s}px;height:${s}px;display:flex;align-items:center;
                justify-content:center;font-size:${big?13:11}px;font-weight:bold;
                border:${big?'3px':'2px'} solid #fff;
                box-shadow:0 2px 6px rgba(0,0,0,.5);">${pos}</div>`,
            iconSize:[s,s], iconAnchor:[s/2,s/2], popupAnchor:[0,-s/2]
        });
    }

    // ── Iconos de los pines INICIO / FIN (etiquetas rectangulares) ───────────
    function makePinIcon(tipo) {
        const isIni = tipo === "inicio";
        const color = isIni ? "#2f9e44" : "#e03131";
        const bdr   = isIni ? "#1a7f37" : "#c92a2a";
        const label = isIni ? "▶ INICIO" : "FIN ◀";
        return L.divIcon({
            className: "",
            html: `<div style="background:${color};color:#fff;border-radius:6px;
                   padding:4px 9px;font-size:11px;font-weight:bold;
                   border:2px solid ${bdr};
                   box-shadow:0 3px 10px rgba(0,0,0,.55);
                   white-space:nowrap;cursor:grab;">${label}</div>`,
            iconSize: [72, 26], iconAnchor: [36, 13], popupAnchor: [0, -22]
        });
    }

    // ── Snap: cliente más cercano al punto soltado ────────────────────────────
    function nearestClient(lat, lng) {
        let best = null, bestD = Infinity;
        _allPoints.forEach(pt => {
            const d = Math.hypot(pt.lat - lat, pt.lng - lng);
            if (d < bestD) { bestD = d; best = pt; }
        });
        return best;
    }

    // ── Limpiar capas ─────────────────────────────────────────────────────────
    function clearAnim() {
        if (_animTimer) { clearInterval(_animTimer); _animTimer = null; }
        _animMarkers.forEach(m => { try { _map.removeLayer(m); } catch{} });
        _animMarkers = [];
        if (_animPoly) { try { _map.removeLayer(_animPoly); } catch{} _animPoly = null; }
        _animStep = 0;
    }

    function clearStatic() {
        _markers.forEach(m => { try { _map.removeLayer(m); } catch{} });
        _markers = [];
        if (_polyline) { try { _map.removeLayer(_polyline); } catch{} _polyline = null; }
    }

    function clearPins() {
        if (_pinInicio) { try { _map.removeLayer(_pinInicio); } catch{} _pinInicio = null; }
        if (_pinFin)    { try { _map.removeLayer(_pinFin);    } catch{} _pinFin    = null; }
    }

    // ── API pública ───────────────────────────────────────────────────────────
    return {

        // Inicializar mapa limpio
        init(containerId) {
            if (_map) { _map.remove(); _map = null; }
            clearAnim(); clearStatic(); clearPins(); _allPoints = [];
            _map = L.map(containerId, { zoomControl:true });
            L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
                attribution:"© OpenStreetMap contributors", maxZoom:19
            }).addTo(_map);
            _map.setView([0,0], 2);
        },

        // Renderizar marcadores estáticos + polilínea ORS opcional
        // Cada elemento de `clientes` debe incluir el campo `cliId`
        renderClientes(clientes, orsPolyline) {
            if (!_map) return;
            clearAnim(); clearStatic();
            _allPoints = (clientes || []).filter(c => c.lat && c.lng);
            if (_allPoints.length === 0) return;

            const latLngs = [];
            _allPoints.forEach(c => {
                const color  = gc(c.dia);
                const marker = L.marker([c.lat, c.lng], { icon: makeIcon(c.pos, color, false) })
                    .bindPopup(`<b>${c.nombre}</b><br/>Código: ${c.codigo}
                        <br/>Clasif.: ${c.cla}
                        <br/>Día: <span style="color:${color}">${c.dia||'—'}</span>
                        <br/>Pos: <b>${c.pos}</b>`);
                marker.addTo(_map);
                _markers.push(marker);
                latLngs.push([c.lat, c.lng]);
            });

            const lineCoords = (orsPolyline && orsPolyline.length > 1) ? orsPolyline : latLngs;
            if (lineCoords.length > 1) {
                _polyline = L.polyline(lineCoords, {
                    color:     orsPolyline ? "#2980b9" : "#2c3e50",
                    weight:    orsPolyline ? 3 : 2,
                    opacity:   0.7,
                    dashArray: orsPolyline ? null : "6,4"
                }).addTo(_map);
            }
            if (latLngs.length > 0)
                _map.fitBounds(L.latLngBounds(latLngs), { padding:[30,30] });
        },

        // Crear/reposicionar los dos pines arrastrables INICIO y FIN
        // primerCliId / ultimoCliId: -1 → usar primer/último punto disponible
        // dotNetRef: referencia .NET para callbacks OnPinMoved y OnAnimacionTerminada
        initPins(primerCliId, ultimoCliId, dotNetRef) {
            if (!_map || _allPoints.length === 0) return;
            _dotNetRef = dotNetRef;
            clearPins();

            const ptInicio = primerCliId > 0
                ? (_allPoints.find(p => p.cliId === primerCliId) || _allPoints[0])
                : _allPoints[0];

            const ptFin = ultimoCliId > 0
                ? (_allPoints.find(p => p.cliId === ultimoCliId) || _allPoints[_allPoints.length - 1])
                : _allPoints[_allPoints.length - 1];

            const createPin = (tipo, pt) => {
                const pin = L.marker([pt.lat, pt.lng], {
                    icon: makePinIcon(tipo), draggable: true, zIndexOffset: 2000
                });
                const tip = tipo === "inicio"
                    ? "🟢 Arrastra para elegir INICIO"
                    : "🔴 Arrastra para elegir FIN";
                pin.bindTooltip(tip, { direction:"top", offset:[0,-20], permanent:false });

                pin.on("dragend", () => {
                    const pos  = pin.getLatLng();
                    const near = nearestClient(pos.lat, pos.lng);
                    if (near) {
                        pin.setLatLng([near.lat, near.lng]);
                        if (_dotNetRef)
                            _dotNetRef.invokeMethodAsync("OnPinMoved", tipo, near.cliId)
                                      .catch(() => {});
                    }
                });
                pin.addTo(_map);
                return pin;
            };

            _pinInicio = createPin("inicio", ptInicio);
            _pinFin    = createPin("fin",    ptFin);
        },

        // Mover un pin a las coordenadas de un cliente por su cliId
        movePinTo(tipo, cliId) {
            if (!_map || _allPoints.length === 0) return;
            const pt  = _allPoints.find(p => p.cliId === cliId);
            if (!pt) return;
            const pin = tipo === "inicio" ? _pinInicio : _pinFin;
            if (pin) pin.setLatLng([pt.lat, pt.lng]);
        },

        // ── Animación de ruta ─────────────────────────────────────────────────
        startAnimation(speedLabel, dotNetRef) {
            if (!_map || _allPoints.length === 0) return;
            const ms = { lento:1000, normal:400, rapido:120 }[speedLabel] || 400;
            clearAnim();
            _markers.forEach(m => { try { _map.removeLayer(m); } catch{} });
            _dotNetRef = dotNetRef;
            _animStep  = 0;

            _animTimer = setInterval(() => {
                if (_animStep >= _allPoints.length) {
                    clearInterval(_animTimer); _animTimer = null;
                    _animMarkers.forEach(m => { try { _map.removeLayer(m); } catch{} });
                    _animMarkers = [];
                    if (_animPoly) { try { _map.removeLayer(_animPoly); } catch{} _animPoly = null; }
                    _markers.forEach(m => { try { m.addTo(_map); } catch{} });
                    if (_dotNetRef)
                        _dotNetRef.invokeMethodAsync("OnAnimacionTerminada").catch(() => {});
                    return;
                }

                const pt    = _allPoints[_animStep];
                const color = gc(pt.dia);

                if (_animMarkers.length > 0) {
                    const prev   = _animMarkers[_animMarkers.length - 1];
                    const prevPt = _allPoints[_animStep - 1];
                    try { prev.setIcon(makeIcon(prevPt.pos, gc(prevPt.dia), false)); } catch{}
                }

                const m = L.marker([pt.lat, pt.lng], { icon: makeIcon(pt.pos, color, true) })
                    .bindPopup(`<b>${pt.nombre}</b><br/>Posición: <b>${pt.pos}</b>`)
                    .addTo(_map);
                m.openPopup();
                _animMarkers.push(m);

                const lls = _animMarkers.map((_, i) => [_allPoints[i].lat, _allPoints[i].lng]);
                if (_animPoly) { try { _map.removeLayer(_animPoly); } catch{} }
                if (lls.length > 1)
                    _animPoly = L.polyline(lls, { color:"#e74c3c", weight:3, opacity:.85 }).addTo(_map);

                _map.panTo([pt.lat, pt.lng], { animate:true, duration:0.25 });
                _animStep++;
            }, ms);
        },

        stopAnimation() {
            clearAnim();
            _markers.forEach(m => { try { m.addTo(_map); } catch{} });
        },

        isPlaying()      { return _animTimer !== null; },
        invalidateSize() { if (_map) _map.invalidateSize(); }
    };
})();
