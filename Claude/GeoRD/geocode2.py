import json, csv, math
import numpy as np
from shapely.geometry import shape, Point, Polygon, MultiPolygon
from shapely.strtree import STRtree
from shapely.prepared import prep
from shapely.ops import unary_union
from osmpoly import cargar, rel_to_polygon
from collections import Counter

KM_LAT = 110.9
def km_lon(lat): return 111.32 * math.cos(math.radians(lat))

prov_g, prov_n = cargar("osm_prov4.json")                 # 32 provincias (OSM)
muni_g, muni_n = cargar("osm_muni6.json", solo_do=False)  # 157 municipios (OSM)
prov_tree, muni_tree = STRtree(prov_g), STRtree(muni_g)
prov_prep = [prep(g) for g in prov_g]; muni_prep = [prep(g) for g in muni_g]
print(f"provincias: {len(prov_g)}   municipios: {len(muni_g)}")

rd_prep = prep(unary_union(prov_g).buffer(0.01))

# poligonos de barrio
def ring(c): return [(p["lon"], p["lat"]) for p in c]
bg, bn = [], []
for e in json.load(open("barrios_poly.json", encoding="utf-8"))["elements"]:
    try:
        g = Polygon(ring(e["geometry"])) if e["type"] == "way" and len(e.get("geometry", [])) >= 4 \
            else rel_to_polygon(e)
        if g is None: continue
        if not g.is_valid: g = g.buffer(0)
        if g.is_empty or not rd_prep.contains(g.representative_point()): continue
        bg.append(g); bn.append(e["tags"].get("name"))
    except Exception: pass
btree = STRtree(bg) if bg else None
bprep = [prep(g) for g in bg]
print(f"poligonos de barrio en RD: {len(bg)}")

def load_nodes(path):
    return [(e["tags"]["name"], e["lat"], e["lon"], e["tags"].get("place"))
            for e in json.load(open(path, encoding="utf-8"))["elements"]
            if e["tags"].get("name") and rd_prep.contains(Point(e["lon"], e["lat"]))]
barrios, parajes = load_nodes("barrios_nodos.json"), load_nodes("parajes_nodos.json")
print(f"nodos barrio: {len(barrios)}   nodos paraje/localidad: {len(parajes)}")
def arr(ns): return (np.array([n[1] for n in ns]), np.array([n[2] for n in ns]),
                     [n[0] for n in ns], [n[3] for n in ns])
bLat, bLon, bNom, _ = arr(barrios); pLat, pLon, pNom, pTipo = arr(parajes)
ETIQ = {"town":"Poblado","city":"Ciudad","village":"Paraje","hamlet":"Paraje",
        "isolated_dwelling":"Paraje","locality":"Paraje"}
def nearest(lat, lon, LAT, LON):
    dy = (LAT-lat)*KM_LAT; dx = (LON-lon)*km_lon(lat); d2 = dy*dy+dx*dx
    i = int(np.argmin(d2)); return i, math.sqrt(d2[i])

w = csv.writer(open("clientes_geoloc.csv","w",newline="",encoding="utf-8"))
w.writerow(["CliCodigo","GeoProvincia","GeoCiudad","GeoBarrio","GeoNivelBarrio","GeoDistanciaKm"])
stats = Counter(); n = 0
for r in csv.DictReader(open("clientes_geo.csv", encoding="utf-8")):
    try:   lat, lon = float(r["Lat"] or 0), float(r["Lon"] or 0)
    except: lat = lon = 0.0
    if not lat or not lon: stats["sin coordenadas"] += 1; continue
    pt = Point(lon, lat)
    prov = ciudad = ""
    for i in prov_tree.query(pt):
        if prov_prep[i].contains(pt): prov = prov_n[i]; break
    for i in muni_tree.query(pt):
        if muni_prep[i].contains(pt): ciudad = muni_n[i]; break
    if ciudad == "Santo Domingo de Guzmán": ciudad = "Distrito Nacional"
    if not prov and not ciudad:
        stats["Fuera de RD"] += 1
        w.writerow([r["CliCodigo"],"","","","Fuera de RD",""]); n += 1; continue
    barrio = nivel = ""; dist = None
    if btree is not None:
        for i in btree.query(pt):
            if bprep[i].contains(pt):
                barrio, nivel, dist = bn[i], "Barrio (dentro del poligono)", 0.0; break
    if not barrio:
        ib, db = nearest(lat, lon, bLat, bLon); ip, dp = nearest(lat, lon, pLat, pLon)
        if   db <= 2.0: barrio, nivel, dist = bNom[ib], "Barrio", db
        elif dp <= 2.0: barrio, nivel, dist = pNom[ip], ETIQ.get(pTipo[ip],"Paraje"), dp
        elif db <= 5.0: barrio, nivel, dist = bNom[ib], "Barrio (aproximado)", db
        elif dp <= 5.0: barrio, nivel, dist = pNom[ip], ETIQ.get(pTipo[ip],"Paraje")+" (aproximado)", dp
        elif ciudad:    barrio, nivel, dist = ciudad, "Municipio", None
        else:           barrio, nivel, dist = prov, "Provincia", None
    stats[nivel] += 1; n += 1
    w.writerow([r["CliCodigo"], prov, ciudad, barrio, nivel, "" if dist is None else round(dist,3)])
print(f"\nclientes geocodificados: {n}")
for k,v in stats.most_common(): print(f"  {k}: {v}")
