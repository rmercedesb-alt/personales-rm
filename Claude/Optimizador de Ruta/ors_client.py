"""
Cliente OpenRouteService (ORS) — Optimizador de Rutas
API gratuita: https://openrouteservice.org/dev/#/login
Tier free: 2000 req/dia Matrix, 500 req/dia Directions
"""

import requests
import math
import time

ORS_BASE = "https://api.openrouteservice.org"

# ─── Distancia haversine (fallback sin API) ────────────────────────────────────
def haversine(lat1, lon1, lat2, lon2):
    R = 6371
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp = math.radians(lat2 - lat1)
    dl = math.radians(lon2 - lon1)
    a  = math.sin(dp/2)**2 + math.cos(p1)*math.cos(p2)*math.sin(dl/2)**2
    return R * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a))

# ─── Matrix API (distancias reales por carretera) ─────────────────────────────
def get_matrix(coords_latlon: list, api_key: str, profile="driving-car") -> dict | None:
    """
    Devuelve matriz de distancias reales (km) y tiempos (min) entre todos los puntos.
    coords_latlon: lista de [lat, lon]
    Retorna: {'durations': [[...]], 'distances': [[...]]} o None si falla
    """
    if not api_key or not coords_latlon:
        return None

    # ORS espera [lon, lat]
    locs = [[c[1], c[0]] for c in coords_latlon]

    url     = f"{ORS_BASE}/v2/matrix/{profile}"
    headers = {"Authorization": api_key, "Content-Type": "application/json"}
    body    = {"locations": locs, "metrics": ["distance", "duration"]}

    try:
        r = requests.post(url, json=body, headers=headers, timeout=5)
        r.raise_for_status()
        data = r.json()
        # Convertir distancias de metros a km y durations de seg a min
        m = data.get("distances", [])
        d = data.get("durations", [])
        dist_km  = [[v / 1000  for v in row] for row in m] if m else None
        dur_min  = [[v / 60    for v in row] for row in d] if d else None
        return {"distances_km": dist_km, "durations_min": dur_min}
    except requests.exceptions.Timeout:
        # Sin conectividad ORS → fallback silencioso a haversine
        return {"error": None, "_fallback": True}
    except requests.exceptions.HTTPError as e:
        code = e.response.status_code
        # 401/403 = clave inválida o endpoint no habilitado → fallback silencioso
        if code in (401, 403):
            return {"error": None, "_fallback": True}
        return {"error": f"ORS Matrix HTTP {code}: {e.response.text[:200]}"}
    except Exception as e:
        return {"error": None, "_fallback": True}  # cualquier otro error → fallback

# ─── Directions API (polilínea real por carretera) ────────────────────────────
def get_directions(coords_latlon: list, api_key: str, profile="driving-car") -> dict | None:
    """
    Retorna la polilínea real (lista de [lat,lon]) para la ruta ordenada de puntos.
    ORS limita a 50 waypoints por request — dividimos si hay más.
    coords_latlon: lista de [lat, lon] en el ORDEN de visita
    """
    if not api_key or len(coords_latlon) < 2:
        return None

    # ORS espera [lon, lat]
    locs = [[c[1], c[0]] for c in coords_latlon]

    # Si hay más de 50 puntos los dividimos en segmentos solapados
    CHUNK = 50
    all_coords = []

    chunks = [locs[i:i+CHUNK] for i in range(0, len(locs), CHUNK - 1)]
    if chunks[-1][-1] != locs[-1]:
        chunks[-1].append(locs[-1])

    url     = f"{ORS_BASE}/v2/directions/{profile}/geojson"
    headers = {"Authorization": api_key, "Content-Type": "application/json"}

    for chunk in chunks:
        if len(chunk) < 2:
            continue
        body = {"coordinates": chunk, "instructions": False, "geometry_simplify": False}
        try:
            r = requests.post(url, json=body, headers=headers, timeout=5)
            r.raise_for_status()
            data  = r.json()
            geom  = data["features"][0]["geometry"]["coordinates"]  # [lon, lat]
            # Convertir a [lat, lon] para Leaflet
            seg   = [[pt[1], pt[0]] for pt in geom]
            if all_coords and seg:
                seg = seg[1:]   # evitar duplicar el punto de unión
            all_coords.extend(seg)
            time.sleep(0.1)     # respetar rate limit free tier
        except requests.exceptions.Timeout:
            return {"error": None, "_fallback": True}   # sin conectividad → fallback
        except requests.exceptions.HTTPError as e:
            code = e.response.status_code
            if code in (401, 403):
                return {"error": None, "_fallback": True}   # fallback silencioso
            return {"error": f"ORS Directions HTTP {code}: {e.response.text[:200]}"}
        except Exception as e:
            return {"error": None, "_fallback": True}

    return {"polyline": all_coords}  # [[lat, lon], ...]

# ─── Nearest Neighbor con matriz de distancias reales ────────────────────────
def nearest_neighbor_matrix(dist_matrix: list[list], start_idx: int = 0,
                             end_idx: int | None = None) -> list[int]:
    """
    Algoritmo del vecino más cercano usando la matriz de distancias reales.
    Retorna la lista de índices ordenados.
    end_idx: si se especifica, ese índice siempre va al final.
    """
    n         = len(dist_matrix)
    visited   = [False] * n
    order     = [start_idx]
    visited[start_idx] = True

    # Si hay end_idx lo reservamos para el final
    reserved = end_idx if (end_idx is not None and end_idx != start_idx) else None

    while len(order) < n:
        last    = order[-1]
        row     = dist_matrix[last]
        best    = None
        best_d  = float("inf")
        for j, d in enumerate(row):
            if visited[j]:
                continue
            if j == reserved and len(order) < n - 1:
                continue  # no visitar el reservado hasta el final
            if d < best_d:
                best_d = d
                best   = j
        if best is None:
            # Solo queda el reservado
            if reserved is not None and not visited[reserved]:
                order.append(reserved)
                visited[reserved] = True
            break
        order.append(best)
        visited[best] = True

    # Asegurar que reserved esté al final si no fue incluido
    if reserved is not None and reserved not in order:
        order.append(reserved)

    return order

# ─── Nearest Neighbor con haversine (sin API) ────────────────────────────────
def nearest_neighbor_haversine(coords_latlon: list, start_idx: int = 0,
                                end_idx: int | None = None) -> list[int]:
    """
    Fallback cuando no hay API key. Usa distancia haversine.
    """
    n        = len(coords_latlon)
    visited  = [False] * n
    order    = [start_idx]
    visited[start_idx] = True
    reserved = end_idx if (end_idx is not None and end_idx != start_idx) else None

    while len(order) < n:
        last  = order[-1]
        la, lo = coords_latlon[last]
        best   = None
        best_d = float("inf")
        for j, (lat2, lon2) in enumerate(coords_latlon):
            if visited[j]: continue
            if j == reserved and len(order) < n - 1: continue
            d = haversine(la, lo, lat2, lon2)
            if d < best_d:
                best_d = d; best = j
        if best is None:
            if reserved is not None and not visited[reserved]:
                order.append(reserved); visited[reserved] = True
            break
        order.append(best); visited[best] = True

    if reserved is not None and reserved not in order:
        order.append(reserved)
    return order

# ─── Función principal de optimización ───────────────────────────────────────
def optimize_route(coords_latlon: list, api_key: str | None = None,
                   start_idx: int = 0, end_idx: int | None = None,
                   profile: str = "driving-car") -> dict:
    """
    Optimiza la secuencia de la ruta.
    Retorna:
        {
          'order':    [idx, idx, ...],      # orden óptimo de indices
          'polyline': [[lat,lon], ...],      # polilínea real (si ORS disponible)
          'method':   'ors' | 'haversine',  # método usado
          'error':    str | None
        }
    """
    method = "haversine"
    order  = []
    poly   = None
    error  = None

    if api_key and len(coords_latlon) >= 2:
        # 1. Obtener matriz de distancias reales
        matrix_result = get_matrix(coords_latlon, api_key, profile)

        if matrix_result and not matrix_result.get("error") and not matrix_result.get("_fallback"):
            dist_m = matrix_result["distances_km"]
            order  = nearest_neighbor_matrix(dist_m, start_idx, end_idx)
            method = "ors_matrix"
            # 2. Solo pedir polilínea si ORS matrix tuvo éxito (misma clave/conectividad)
            if order:
                ordered_coords = [coords_latlon[i] for i in order]
                poly_result    = get_directions(ordered_coords, api_key, profile)
                if poly_result and not poly_result.get("error") and not poly_result.get("_fallback"):
                    poly = poly_result["polyline"]
                elif poly_result and poly_result.get("error"):
                    error = poly_result["error"]
        else:
            # Matrix falló → fallback haversine, sin llamada extra a Directions
            if matrix_result and matrix_result.get("error"):
                error = matrix_result["error"]
            order = nearest_neighbor_haversine(coords_latlon, start_idx, end_idx)
    else:
        order = nearest_neighbor_haversine(coords_latlon, start_idx, end_idx)
        if not api_key:
            error = "Sin API key — usando distancia en línea recta"

    return {"order": order, "polyline": poly, "method": method, "error": error}
