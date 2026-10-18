from shapely.geometry import Polygon, MultiPolygon
from shapely.ops import unary_union
import json

def _stitch(segs):
    """Une segmentos de via en anillos cerrados."""
    segs = [list(s) for s in segs if len(s) >= 2]
    rings = []
    while segs:
        cur = segs.pop(0)
        avanzo = True
        while cur[0] != cur[-1] and avanzo:
            avanzo = False
            for i, s in enumerate(segs):
                if s[0] == cur[-1]:   cur += s[1:];            segs.pop(i); avanzo = True; break
                if s[-1] == cur[-1]:  cur += s[::-1][1:];      segs.pop(i); avanzo = True; break
                if s[-1] == cur[0]:   cur = s[:-1] + cur;      segs.pop(i); avanzo = True; break
                if s[0] == cur[0]:    cur = s[::-1][:-1] + cur;segs.pop(i); avanzo = True; break
        if len(cur) >= 4 and cur[0] == cur[-1]:
            rings.append(cur)
    return rings

def rel_to_polygon(el):
    outer, inner = [], []
    for m in el.get("members", []):
        g = m.get("geometry")
        if not g: continue
        pts = [(round(p["lon"], 9), round(p["lat"], 9)) for p in g]
        (inner if m.get("role") == "inner" else outer).append(pts)
    ro, ri = _stitch(outer), _stitch(inner)
    if not ro: return None
    polys = []
    for r in ro:
        try:
            p = Polygon(r, [h for h in ri if Polygon(r).contains(Polygon(h).representative_point())])
        except Exception:
            p = Polygon(r)
        if not p.is_valid: p = p.buffer(0)
        if not p.is_empty: polys.append(p)
    if not polys: return None
    g = unary_union(polys)
    return g if not g.is_empty else None

def cargar(path, solo_do=True):
    geoms, names = [], []
    for el in json.load(open(path, encoding="utf-8"))["elements"]:
        t = el.get("tags", {})
        iso = t.get("ISO3166-2", "")
        if solo_do and iso and not iso.startswith("DO"): continue
        g = rel_to_polygon(el)
        if g is None: continue
        geoms.append(g); names.append(t.get("name"))
    return geoms, names
