// ── Helper: leer valores de <select multiple> ─────────────────────────────────
window.getSelectedValues = (elementId) => {
    const el = document.getElementById(elementId);
    if (!el) return [];
    return Array.from(el.selectedOptions).map(o => o.value);
};

// ── Interop Leaflet ───────────────────────────────────────────────────────────
window.MapInterop = (() => {

    let _map       = null;
    let _markers   = [];
    let _polyline  = null;
    let _allPoints = [];

    // Animación
    let _animTimer   = null;
    let _animStep    = 0;
    let _animMarkers = [];
    let _animPoly    = null;
    let _animPlaying = false;
    let _dotNetRef   = null;

    const DayColors = {
        Lunes:"#3498db", Martes:"#e74c3c", Miercoles:"#2ecc71",
        Jueves:"#f39c12", Viernes:"#9b59b6", Sabado:"#1abc9c",
        Domingo:"#95a5a6", default:"#7f8c8d"
    };
    const gc = d => DayColors[d] || DayColors.default;

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

    function clearAnim() {
        if (_animTimer) { clearInterval(_animTimer); _animTimer = null; }
        _animMarkers.forEach(m => { try { _map.removeLayer(m); } catch{} });
        _animMarkers = [];
        if (_animPoly) { try { _map.removeLayer(_animPoly); } catch{} _animPoly = null; }
        _animStep = 0; _animPlaying = false;
    }

    function clearStatic() {
        _markers.forEach(m => { try { _map.removeLayer(m); } catch{} });
        _markers = [];
        if (_polyline) { try { _map.removeLayer(_polyline); } catch{} _polyline = null; }
    }

    return {

        init(containerId) {
            if (_map) { _map.remove(); _map = null; }
            clearAnim(); clearStatic(); _allPoints = [];
            _map = L.map(containerId, { zoomControl:true });
            L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
                attribution:"© OpenStreetMap contributors", maxZoom:19
            }).addTo(_map);
            _map.setView([0,0], 2);
        },

        // Renderizar todos los puntos estáticos (+ polilínea ORS opcional)
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
                    color: orsPolyline ? "#2980b9" : "#2c3e50",
                    weight: orsPolyline ? 3 : 2,
                    opacity: 0.7,
                    dashArray: orsPolyline ? null : "6,4"
                }).addTo(_map);
            }
            if (latLngs.length > 0)
                _map.fitBounds(L.latLngBounds(latLngs), { padding:[30,30] });
        },

        // Iniciar animación de la ruta
        // speedLabel: "lento"|"normal"|"rapido"
        // dotNetRef: referencia .NET para callback OnAnimacionTerminada
        startAnimation(speedLabel, dotNetRef) {
            if (!_map || _allPoints.length === 0) return;
            const ms = { lento:1000, normal:400, rapido:120 }[speedLabel] || 400;
            clearAnim();
            _markers.forEach(m => { try { _map.removeLayer(m); } catch{} });
            _dotNetRef   = dotNetRef;
            _animPlaying = true;
            _animStep    = 0;

            _animTimer = setInterval(() => {
                if (_animStep >= _allPoints.length) {
                    clearInterval(_animTimer); _animTimer = null; _animPlaying = false;
                    // Restaurar marcadores estáticos
                    _animMarkers.forEach(m => { try { _map.removeLayer(m); } catch{} });
                    _animMarkers = [];
                    if (_animPoly) { try { _map.removeLayer(_animPoly); } catch{} _animPoly = null; }
                    _markers.forEach(m => { try { m.addTo(_map); } catch{} });
                    if (_dotNetRef)
                        _dotNetRef.invokeMethodAsync("OnAnimacionTerminada").catch(()=>{});
                    return;
                }

                const pt    = _allPoints[_animStep];
                const color = gc(pt.dia);

                // Poner anterior en tamaño normal
                if (_animMarkers.length > 0) {
                    const prev   = _animMarkers[_animMarkers.length - 1];
                    const prevPt = _allPoints[_animStep - 1];
                    try { prev.setIcon(makeIcon(prevPt.pos, gc(prevPt.dia), false)); } catch{}
                }

                // Agregar nuevo marcador resaltado
                const m = L.marker([pt.lat, pt.lng], { icon: makeIcon(pt.pos, color, true) })
                    .bindPopup(`<b>${pt.nombre}</b><br/>Posición: <b>${pt.pos}</b>`)
                    .addTo(_map);
                m.openPopup();
                _animMarkers.push(m);

                // Actualizar polilínea progresiva
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

        isPlaying() { return _animTimer !== null; },

        invalidateSize() { if (_map) _map.invalidateSize(); }
    };
})();
