"""
WhatsApp Sender - AM Comercial
Envía mensajes y archivos por WhatsApp automáticamente
Usa la app de WhatsApp instalada en Windows
Pantalla: 1920x1080
"""

import json
import time
import os
import sys
import subprocess
import urllib.parse
from datetime import datetime

# ─── CONFIGURACION ────────────────────────────────────────────────
RUTA_BASE      = r"C:\Personales RM\Claude"
RUTA_CONTACTOS = os.path.join(RUTA_BASE, "contactos.json")
ESPERA_APP     = 5

# Coordenadas WhatsApp Windows 1920x1080
BTN_ADJUNTAR   = (734, 970)   # botón "+"
BTN_DOCUMENTO  = (732, 595)   # opción "Document"
CAMPO_MENSAJE  = (1000, 970)  # campo de texto

# ─── CARGAR CONTACTOS ─────────────────────────────────────────────
def cargar_contactos():
    if not os.path.exists(RUTA_CONTACTOS):
        print(f"ERROR: No se encontró {RUTA_CONTACTOS}")
        sys.exit(1)
    with open(RUTA_CONTACTOS, "r", encoding="utf-8") as f:
        return json.load(f)

def buscar_contacto_interactivo(datos):
    termino = input("¿A quién enviar? (nombre, grupo o +1809XXXXXXX): ").strip()
    if termino.startswith("+"):
        return [termino]
    for g in datos["grupos"]:
        if g["nombre"].lower() == termino.lower():
            return g["miembros"]
    for c in datos["contactos"]:
        if c["nombre"].lower() == termino.lower():
            return [c.get("numero_principal") or c.get("numero")]
    # Búsqueda parcial
    coincidencias = [c for c in datos["contactos"]
                     if termino.lower() in c["nombre"].lower()]
    if len(coincidencias) == 0:
        print(f"⚠️  No se encontró: {termino}")
        return []
    elif len(coincidencias) == 1:
        num = coincidencias[0].get("numero_principal") or coincidencias[0].get("numero")
        print(f"✅ Encontrado: {coincidencias[0]['nombre']} — {num}")
        return [num]
    else:
        print(f"\n🔍 {len(coincidencias)} coincidencias:")
        for i, c in enumerate(coincidencias[:10]):
            num = c.get("numero_principal") or c.get("numero")
            print(f"  {i+1}. {c['nombre']} — {num}")
        sel = input("\nElige el número (1-10): ").strip()
        if sel.isdigit() and 1 <= int(sel) <= len(coincidencias[:10]):
            c = coincidencias[int(sel)-1]
            return [c.get("numero_principal") or c.get("numero")]
        return []

def listar_contactos(datos):
    print("\n📋 CONTACTOS (primeros 20):")
    print("─" * 45)
    for c in datos["contactos"][:20]:
        num = c.get('numero_principal') or c.get('numero', '—')
        print(f"  👤 {c['nombre']} — {num}")
    print(f"\n  ... y {len(datos['contactos'])-20} más")
    print("\n📋 GRUPOS:")
    print("─" * 45)
    for g in datos["grupos"]:
        print(f"  👥 {g['nombre']} — {len(g['miembros'])} miembros")
    print()

# ─── ABRIR CHAT EN WHATSAPP ───────────────────────────────────────
def abrir_chat(numero):
    import pyautogui
    num_limpio = numero.replace("+", "").replace("-", "").replace(" ", "")
    url = f"whatsapp://send?phone={num_limpio}"
    subprocess.Popen(f'start "" "{url}"', shell=True)
    print(f"  📱 Abriendo chat...")
    time.sleep(ESPERA_APP)
    # Clic en el campo de mensaje para asegurar foco
    pyautogui.click(CAMPO_MENSAJE)
    time.sleep(0.5)

# ─── ENVIO MENSAJE DE TEXTO ───────────────────────────────────────
def enviar_mensaje(numero, mensaje):
    try:
        import pyautogui
        num = numero if numero.startswith("+") else "+" + numero
        num_limpio = num.replace("+", "")
        texto_enc  = urllib.parse.quote(mensaje)
        url = f"whatsapp://send?phone={num_limpio}&text={texto_enc}"
        subprocess.Popen(f'start "" "{url}"', shell=True)
        print(f"  📱 Abriendo WhatsApp...")
        time.sleep(ESPERA_APP)
        pyautogui.press("enter")
        time.sleep(1.5)
        print(f"  ✅ Mensaje enviado a {num}")
        return True
    except Exception as e:
        print(f"  ❌ Error: {e}")
        return False

# ─── ENVIO ARCHIVO ────────────────────────────────────────────────
def enviar_archivo(numero, ruta_archivo, caption=""):
    try:
        import pyautogui

        if not os.path.exists(ruta_archivo):
            print(f"  ❌ Archivo no encontrado: {ruta_archivo}")
            return False

        num = numero if numero.startswith("+") else "+" + numero

        # Paso 1 — Abrir chat
        abrir_chat(num)

        # Paso 2 — Clic en botón "+"
        print(f"  📎 Haciendo clic en adjuntar...")
        pyautogui.click(BTN_ADJUNTAR)
        time.sleep(1.5)

        # Paso 3 — Clic en "Document" del menú que aparece
        print(f"  📄 Seleccionando Documento...")
        pyautogui.click(BTN_DOCUMENTO)
        time.sleep(2)

        # Paso 4 — Escribir ruta en el diálogo de Windows
        print(f"  📂 Seleccionando archivo...")
        pyautogui.typewrite(ruta_archivo, interval=0.04)
        time.sleep(0.5)
        pyautogui.press("enter")
        time.sleep(2.5)

        # Paso 5 — Caption si hay
        if caption:
            pyautogui.typewrite(caption, interval=0.04)
            time.sleep(0.5)

        # Paso 6 — Enviar
        pyautogui.press("enter")
        time.sleep(1.5)

        print(f"  ✅ Archivo enviado a {num}")
        return True

    except Exception as e:
        print(f"  ❌ Error enviando archivo: {e}")
        return False

# ─── ENVIO A MULTIPLES ────────────────────────────────────────────
def enviar_a_lista(destinatarios, tipo, mensaje="",
                   ruta_archivo=None, caption=""):
    exitosos = 0
    for num in destinatarios:
        print(f"\n  📤 Enviando a {num}...")
        if tipo == "texto":
            ok = enviar_mensaje(num, mensaje)
        elif tipo == "archivo":
            ok = enviar_archivo(num, ruta_archivo, caption)
        elif tipo == "ambos":
            ok1 = enviar_mensaje(num, mensaje)
            time.sleep(5)
            ok2 = enviar_archivo(num, ruta_archivo, caption)
            ok = ok1 and ok2
        else:
            ok = False
        if ok: exitosos += 1
        time.sleep(6)
    return exitosos

# ─── MAIN ─────────────────────────────────────────────────────────
def main():
    print(f"\n{'='*55}")
    print(f"  WHATSAPP SENDER — AM COMERCIAL")
    print(f"  {datetime.now().strftime('%d/%m/%Y %H:%M')}")
    print(f"{'='*55}\n")

    datos = cargar_contactos()

    print("¿Qué deseas hacer?")
    print("  1. Enviar mensaje de texto")
    print("  2. Enviar archivo (PDF o imagen)")
    print("  3. Enviar texto + archivo")
    print("  4. Ver contactos disponibles")
    print()

    opcion = input("Selecciona una opción (1-4): ").strip()

    if opcion == "4":
        listar_contactos(datos)
        return

    numeros = buscar_contacto_interactivo(datos)
    if not numeros:
        print("❌ No se encontraron destinatarios.")
        return

    print(f"\n✅ Destinatarios: {len(numeros)}")
    for n in numeros: print(f"   → {n}")

    mensaje = ruta_arch = caption = ""

    if opcion in ("1", "3"):
        print("\nEscribe el mensaje (línea vacía + Enter para terminar):")
        lineas = []
        while True:
            linea = input()
            if linea == "" and lineas and lineas[-1] == "":
                break
            lineas.append(linea)
        mensaje = "\n".join(lineas).strip()

    if opcion in ("2", "3"):
        ruta_arch = input("\nRuta del archivo: ").strip().strip('"')
        caption   = input("Caption (Enter para omitir): ").strip()

    tipo_map = {"1":"texto","2":"archivo","3":"ambos"}
    tipo = tipo_map.get(opcion, "texto")

    print(f"\n🚀 Iniciando envío...")
    print(f"   ⚠️  No muevas el mouse mientras trabaja\n")

    exitosos = enviar_a_lista(numeros, tipo=tipo, mensaje=mensaje,
                              ruta_archivo=ruta_arch, caption=caption)

    print(f"\n{'='*55}")
    print(f"  ✅ Completados: {exitosos}/{len(numeros)}")
    print(f"{'='*55}\n")

if __name__ == "__main__":
    main()
