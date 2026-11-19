import glob, io, os, sys, zipfile

# Uso: python3 empaquetar.py base.scmod codigo.dll salida.scmod
# Los .scmod son: 49 bytes de cabecera + un zip con los bytes invertidos.
# NUEVO: tambien mete todos los *.xdb que esten junto a este script (ej. HealthBar.xdb)

base, dll, salida = sys.argv[1:4]
datos = open(base, "rb").read()
cabecera = datos[:49]
origen = zipfile.ZipFile(io.BytesIO(datos[49:][::-1]))

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
comprobar = zipfile.ZipFile(io.BytesIO(final[49:][::-1]))
assert comprobar.testzip() is None
print("OK:", salida, comprobar.namelist())
