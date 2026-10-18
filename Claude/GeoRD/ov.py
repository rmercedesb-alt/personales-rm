import requests, json, sys, time
UA={"User-Agent":"AMComercial-geo/1.0 (r.mercedesb@gmail.com)"}
EPS=["https://overpass.kumi.systems/api/interpreter",
     "https://overpass.private.coffee/api/interpreter",
     "https://overpass-api.de/api/interpreter"]
def run(q, name):
    for intento in range(2):
        for ep in EPS:
            try:
                r = requests.post(ep, data={"data":q}, timeout=600, headers=UA)
                if r.ok and r.content[:1] in (b"{", b"["):
                    d = r.json()
                    json.dump(d, open(name,"w",encoding="utf-8"))
                    print(f"{name}: OK ({len(d['elements'])} elementos) via {ep.split('/')[2]}")
                    return True
                print(f"  {ep.split('/')[2]} -> HTTP {r.status_code}")
            except Exception as e:
                print(f"  {ep.split('/')[2]} -> {type(e).__name__}")
            time.sleep(3)
    print(f"{name}: FALLO")
    return False
