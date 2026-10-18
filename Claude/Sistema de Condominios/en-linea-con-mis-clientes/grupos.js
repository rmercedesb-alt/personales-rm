// Grupos de contactos manuales (personas que NO estan en la base de datos).
// Se guardan en data/grupos.json para reutilizarlos en envios futuros.
const fs = require('fs')
const path = require('path')

const FILE = path.join(__dirname, 'data', 'grupos.json')

function load() {
  try { return JSON.parse(fs.readFileSync(FILE, 'utf8')) }
  catch { return [] }
}

function persist(grupos) {
  fs.mkdirSync(path.dirname(FILE), { recursive: true })
  fs.writeFileSync(FILE, JSON.stringify(grupos, null, 2), 'utf8')
}

// Crea o actualiza un grupo. Devuelve el grupo guardado.
function guardar({ id, nombre, contactos }) {
  const grupos = load()
  const limpio = (contactos || [])
    .map(c => ({ nombre: (c.nombre || '').trim(), telefono: (c.telefono || '').trim() }))
    .filter(c => c.telefono)
  if (id) {
    const i = grupos.findIndex(g => g.id === id)
    if (i >= 0) {
      grupos[i] = { ...grupos[i], nombre: nombre || grupos[i].nombre, contactos: limpio, fecha: new Date().toISOString() }
      persist(grupos)
      return grupos[i]
    }
  }
  const nuevo = { id: 'GRP-' + Date.now(), nombre: nombre || 'Grupo sin nombre', contactos: limpio, fecha: new Date().toISOString() }
  grupos.push(nuevo)
  persist(grupos)
  return nuevo
}

function eliminar(id) {
  const grupos = load().filter(g => g.id !== id)
  persist(grupos)
}

module.exports = { load, guardar, eliminar }
