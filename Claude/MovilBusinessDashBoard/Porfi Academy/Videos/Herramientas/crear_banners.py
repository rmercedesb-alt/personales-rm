import cv2, numpy as np, os, sys
from PIL import Image
src = r"C:\Users\Rudy\Downloads\1er paso para tirar duro.mp4"
root = r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\MovilBusinessDashBoard\Porfi Academy\Videos"
W, H = 1600, 800
NAVY = np.array([32, 16, 9], np.float32)         # #091020 (BGR)
NAVY2 = np.array([80, 39, 26], np.float32)       # #1A2750
ORANGE = np.array([5, 77, 249], np.float32)      # #F94D05
SLANT = 70                                       # inclinación (como la cursiva del logo)
SS = 4                                           # supersampling para bordes suaves
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

def poly_mask(pts, w=W, h=H):
    m = np.zeros((h * SS, w * SS), np.uint8)
    cv2.fillConvexPoly(m, (np.array(pts, np.float32) * SS).astype(np.int32), 255)
    return cv2.resize(m, (w, h), interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0

def over(bg, color, alpha):
    a = alpha[:, :, None]
    return bg * (1 - a) + color * a

def banner(frame):
    h, w = frame.shape[:2]
    fw, fh = int(round(w * H / h)), H                       # el fotograma completo a 800 px de alto
    x0 = 1090 - fw // 2

    # fondo de marca: azul marino → azul más claro a la derecha + resplandor naranja detrás del panel
    t = np.clip(xx / W, 0, 1)[:, :, None]
    bg = NAVY * (1 - 0.65 * t) + NAVY2 * (0.65 * t)
    d = np.hypot(xx - (x0 + fw * 0.7), (yy - 470) * 1.15)
    bg = over(bg, ORANGE, np.exp(-(d / 480) ** 2) * 0.40)

    # trazos de velocidad (como la estela de la pelota del logo), solo en la mitad superior
    for y, length, th, al in ((170, 330, 12, .75), (230, 480, 18, .95), (290, 300, 10, .75),
                              (350, 560, 20, 1.0), (410, 360, 12, .8), (470, 260, 10, .65)):
        xe = x0 + SLANT * (1 - y / H) - 22
        xs = xe - length
        m = poly_mask([(xs, y), (xe, y - th / 2), (xe, y + th / 2)])
        ramp = np.clip((xx - xs) / (xe - xs), 0, 1) ** 1.3
        bg = over(bg, ORANGE, m * ramp * al)

    # sombra del panel
    pm = poly_mask([(x0 + SLANT, 0), (x0 + fw, 0), (x0 + fw - SLANT, H), (x0, H)])
    sh = cv2.GaussianBlur(pm, (0, 0), 20)
    bg = bg * (1 - 0.5 * sh)[:, :, None]

    # primer plano: video nítido (Lanczos + realce leve) dentro del paralelogramo
    fg = cv2.resize(frame, (fw, fh), interpolation=cv2.INTER_LANCZOS4).astype(np.float32)
    fg = np.clip(fg + 0.6 * (fg - cv2.GaussianBlur(fg, (0, 0), 1.2)), 0, 255)
    canvas = np.zeros((H, W, 3), np.float32); canvas[:, x0:x0 + fw] = fg
    bg = bg * (1 - pm)[:, :, None] + canvas * pm[:, :, None]

    # filos naranja del borde izquierdo
    bg = over(bg, ORANGE, poly_mask([(x0 + SLANT - 10, 0), (x0 + SLANT, 0), (x0, H), (x0 - 10, H)]))
    bg = over(bg, ORANGE, poly_mask([(x0 + SLANT - 28, 0), (x0 + SLANT - 22, 0), (x0 - 22, H), (x0 - 28, H)]) * 0.6)
    return cv2.cvtColor(np.clip(bg, 0, 255).astype(np.uint8), cv2.COLOR_BGR2RGB)

if __name__ == "__main__":
    c = cv2.VideoCapture(src); frames = []
    while True:
        ok, f = c.read()
        if not ok: break
        frames.append(f)
    picks = [("1-postura-inicial", 0), ("2-elevacion-de-pierna", 120), ("3-brazo-extendido", 224)]
    out = os.path.join(os.getcwd(), "preview") if (len(sys.argv) > 1 and sys.argv[1] == "preview") else root
    os.makedirs(os.path.join(out, "Banners", "Para subir a la web"), exist_ok=True)
    for name, i in picks:
        img = Image.fromarray(banner(frames[i]))
        png = os.path.join(out, "Banners", f"banner-{name}-1600x800.png")
        jpg = os.path.join(out, "Banners", "Para subir a la web", f"banner-{name}-1600x800.jpg")
        img.save(png, optimize=True)
        img.save(jpg, quality=92, subsampling=0, optimize=True, progressive=True)
        print(name, "PNG", round(os.path.getsize(png) / 1024), "KB | JPG", round(os.path.getsize(jpg) / 1024), "KB")
