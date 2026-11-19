import glob, io, os, sys, zipfile

# Uso: python3 empaquetar.py base.scmod codigo.dll salida.scmod
# Los .scmod son: 49 bytes de cabecera + un zip con los bytes invertidos.
# NUEVO: tambien mete todos los *.xdb que esten junto a este script (ej. HealthBar.xdb)

base, dll, salida = sys.argv[1:4]
datos = open(base, "rb").read()
# El base puede venir con la cabecera de 49 bytes o sin ella (solo el zip invertido): se detecta.
def abrir(b):
    try:
        z = zipfile.ZipFile(io.BytesIO(b[::-1]))
        return z if z.testzip() is None else None
    except zipfile.BadZipFile:
        return None

# Cabecera de 49 bytes que el juego exige al inicio de todo .scmod
CABECERA = bytes.fromhex("e69c89e5a4b4e69c89e884b8e5a4a9e6898de5b091e5b9b42ce8808de78cb4e8a1a8e6bc94e695a2e4b8bae4babae58588")
origen = abrir(datos)
cabecera = CABECERA          # base sin cabecera: se le pone la correcta
if origen is None:
    cabecera = datos[:49]    # base con cabecera: se conserva
    origen = abrir(datos[49:])
if origen is None:
    sys.exit("ERROR: " + base + " no es un .scmod valido (ni con ni sin cabecera de 49 bytes)")

nuevos = {os.path.basename(dll.replace("\\", "/")): dll}
aqui = os.path.dirname(os.path.abspath(__file__))
for x in glob.glob(os.path.join(aqui, "*.xdb")):
    nuevos[os.path.basename(x)] = x

buf = io.BytesIO()
with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
    for item in origen.infolist():
        if item.filename in nuevos:   # se reemplaza por la version nueva
            continue
        z.writestr(item, origen.read(item))
    for nombre, ruta in nuevos.items():
        zi = zipfile.ZipInfo(nombre)
        zi.compress_type = zipfile.ZIP_DEFLATED
        zi.flag_bits |= 0x800
        z.writestr(zi, open(ruta, "rb").read())
open(salida, "wb").write(cabecera + buf.getvalue()[::-1])

# Comprobacion
final = open(salida, "rb").read()
comprobar = zipfile.ZipFile(io.BytesIO(final[len(cabecera):][::-1]))
assert comprobar.testzip() is None
print("OK:", salida, comprobar.namelist())
