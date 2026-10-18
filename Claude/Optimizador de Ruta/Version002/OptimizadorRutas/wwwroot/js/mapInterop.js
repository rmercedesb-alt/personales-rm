// ── Helper: leer valores seleccionados de un <select multiple> ────────────────
window.getSelectedValues = (elementId) => {
    const el = document.getElementById(elementId);
    if (!el) return [];
    return Array.from(el.selectedOptions).map(o => o.value);
};

// ── Interop Leaflet para Blazor ───────────────────────────────────────────────
window.MapInterop = (() => {
    let _map = null;
    let _markers = [];
    let _polyline = null;

    // Colores por día de visita
    const DayColors = {
        "Lunes":     "#3498db",
        "Martes":    "#e74c3c",
        "Miercoles": "#2ecc71",
        "Jueves":    "#f39c12",
        "Viernes":   "#9b59b6",
        "Sabado":    "#1abc9c",
        "Domingo":   "#95a5a6",
        "default":   "#7f8c8d"
    };

    function getColor(dia) {
        return DayColors[dia] || DayColors["default"];
    }

    function makeIcon(pos, color) {
        return L.divIcon({
            className: "",
            html: `<div style="
                background:${color};
                color:#fff;
                border-radius:50%;
                width:26px;height:26px;
                display:flex;align-items:center;justify-content:center;
                font-size:11px;font-weight:bold;
                border:2px solid #fff;
                box-shadow:0 1px 4px rgba(0,0,0,.4);
            ">${pos}</div>`,
            iconSize:   [26, 26],
            iconAnchor: [13, 13],
            popupAnchor:[0, -14]
        });
    }

    return {
        // Inicializar mapa en el div con el ID dado
        init(containerId) {
            if (_map) { _map.remove(); _map = null; }
            _markers = []; _polyline = null;

            _map = L.map(containerId, { zoomControl: true });
            L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
                attribution: "© OpenStreetMap contributors",
                maxZoom: 19
            }).addTo(_map);
            _map.setView([0, 0], 2);
        },

        // Renderizar clientes en el mapa
        renderClientes(clientes) {
            if (!_map) return;

            // Limpiar anterior
            _markers.forEach(m => _map.removeLayer(m));
            _markers = [];
            if (_polyline) { _map.removeLayer(_polyline); _polyline = null; }

            if (!clientes || clientes.length === 0) return;

            const latLngs = [];

            clientes.forEach((c, idx) => {
                if (!c.lat || !c.lng || c.lat === 0 || c.lng === 0) return;

                const color = getColor(c.dia);
                const pos   = c.pos || (idx + 1);
                const marker = L.marker([c.lat, c.lng], { icon: makeIcon(pos, color) })
                    .bindPopup(`
                        <b>${c.nombre}</b><br/>
                        Código: ${c.codigo}<br/>
                        Clasificación: ${c.cla}<br/>
                        Día: <span style="color:${color}">${c.dia || "—"}</span><br/>
                        Posición: ${pos}
                    `);
                marker.addTo(_map);
                _markers.push(marker);
                latLngs.push([c.lat, c.lng]);
            });

            // Línea de ruta
            if (latLngs.length > 1) {
                _polyline = L.polyline(latLngs, {
                    color: "#2c3e50",
                    weight: 2,
                    opacity: 0.6,
                    dashArray: "6,4"
                }).addTo(_map);
            }

            // Fit bounds
            if (latLngs.length > 0) {
                _map.fitBounds(L.latLngBounds(latLngs), { padding: [30, 30] });
            }
        },

        // Invalidar tamaño (cuando el panel cambia de tamaño)
        invalidateSize() {
            if (_map) _map.invalidateSize();
        }
    };
})();
