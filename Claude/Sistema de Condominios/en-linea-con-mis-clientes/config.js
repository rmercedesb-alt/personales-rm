// Configuracion editable (se guarda en data/config.json)
const fs = require('fs')
const path = require('path')

const FILE = path.join(__dirname, 'data', 'config.json')

const DEFAULT = {
  recordatorioActivo: true,
  dias: [1, 5],          // 0=Dom 1=Lun ... 5=Vie 6=Sab
  hora: 9,               // hora del dia (24h) en que se envia
  soloFueraDeGracia: false,
  plantilla:
`Estimado Cliente {nombre}, le informamos que el alquiler correspondiente al mes {meses} ya se encuentra vencido, por lo que le pedimos pasar lo antes posible por nuestras oficinas a realizar el pago.

El monto pendiente al dia de hoy es de {monto}.

Pase feliz resto del dia.

Atentamente,
Fanny Mercedes & Asoc.`,
}

function load() {
  try {
    const raw = JSON.parse(fs.readFileSync(FILE, 'utf8'))
    return { ...DEFAULT, ...raw }
  } catch {
    return { ...DEFAULT }
  }
}

function save(cfg) {
  const merged = { ...load(), ...cfg }
  fs.mkdirSync(path.dirname(FILE), { recursive: true })
  fs.writeFileSync(FILE, JSON.stringify(merged, null, 2), 'utf8')
  return merged
}

module.exports = { load, save, DEFAULT }
