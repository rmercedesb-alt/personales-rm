import { useState, useEffect } from 'react'
import { Settings, Save, CheckCircle, DatabaseBackup } from 'lucide-react'
import api from '../../lib/api'

const BRAND = '#1B4F8C'

interface ConfigBackup {
  carpetaLocal: string; clienteNombre: string; retencionDias: number
  driveHabilitado: boolean; rcloneRemoto: string
}

function BackupConfig() {
  const [cfg, setCfg] = useState<ConfigBackup>({
    carpetaLocal: 'C:\\InmueblesV6-Backups', clienteNombre: '', retencionDias: 8,
    driveHabilitado: false, rcloneRemoto: '',
  })
  const [cargando, setCargando] = useState(true)
  const [guardando, setGuardando] = useState(false)
  const [msg, setMsg] = useState('')
  const set = <K extends keyof ConfigBackup>(k: K, v: ConfigBackup[K]) => setCfg(c => ({ ...c, [k]: v }))

  useEffect(() => {
    api.get<{ data: ConfigBackup }>('/procesos/config-backup')
      .then(r => setCfg(r.data.data))
      .catch(() => {})
      .finally(() => setCargando(false))
  }, [])

  const guardar = async () => {
    setGuardando(true); setMsg('')
    try {
      await api.put('/procesos/config-backup', cfg)
      setMsg('Configuración de respaldo guardada.')
    } catch (e: any) { setMsg(e.response?.data?.message || 'Error al guardar') }
    finally { setGuardando(false) }
  }

  const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

  return (
    <div className="bg-white rounded-xl border border-slate-200 p-5 space-y-4">
      <div className="flex items-center gap-2">
        <DatabaseBackup size={18} className="text-green-600" />
        <h2 className="text-sm font-semibold text-slate-700">Respaldo automático (nocturno)</h2>
      </div>
      <p className="text-xs text-slate-500 -mt-1">
        Cada noche (1:00 AM) se genera el respaldo de la base de datos + un Excel consultable, con copia local y rotación.
        Opcionalmente se sube a tu Google Drive con rclone.
      </p>

      {cargando ? (
        <div className="flex justify-center py-6"><div className="w-6 h-6 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : (
        <div className="space-y-3">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs text-slate-500 mb-1">Carpeta local de respaldos</label>
              <input className={inp} value={cfg.carpetaLocal} onChange={e => set('carpetaLocal', e.target.value)} />
            </div>
            <div>
              <label className="block text-xs text-slate-500 mb-1">Nombre del cliente (para archivos)</label>
              <input className={inp} value={cfg.clienteNombre} onChange={e => set('clienteNombre', e.target.value)} placeholder="Ej: FannyMercedes" />
            </div>
            <div>
              <label className="block text-xs text-slate-500 mb-1">Días a conservar (rotación)</label>
              <input type="number" min={1} className={inp} value={cfg.retencionDias} onChange={e => set('retencionDias', +e.target.value)} />
            </div>
            <div>
              <label className="block text-xs text-slate-500 mb-1">Destino en Drive (rclone)</label>
              <input className={inp} value={cfg.rcloneRemoto} onChange={e => set('rcloneRemoto', e.target.value)} placeholder="Ej: gdrive:Respaldos/FannyMercedes" />
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={cfg.driveHabilitado} onChange={e => set('driveHabilitado', e.target.checked)} />
            Subir el respaldo a Google Drive (requiere rclone instalado y configurado)
          </label>
          <div className="flex items-center gap-3">
            <button onClick={guardar} disabled={guardando}
              className="flex items-center gap-2 px-5 py-2 rounded-lg text-white text-sm disabled:opacity-50" style={{ background: BRAND }}>
              <Save size={15} /> {guardando ? 'Guardando...' : 'Guardar respaldo'}
            </button>
            {msg && <span className="flex items-center gap-1 text-sm text-green-700"><CheckCircle size={15} /> {msg}</span>}
          </div>
          <div className="bg-slate-50 border border-slate-200 rounded-lg p-3 text-xs text-slate-500">
            <p className="font-semibold text-slate-600 mb-1">Para activar la subida a Drive (una sola vez):</p>
            Instala rclone, ejecuta <code>rclone config</code> y crea un remoto de Google Drive (por ej. lo llamas <code>gdrive</code>).
            Luego escribe arriba el destino como <code>gdrive:Carpeta/DelCliente</code>, marca la casilla y prueba con
            "Respaldar ahora" en <strong>Procesos</strong>.
          </div>
        </div>
      )}
    </div>
  )
}

const OPCIONES = [
  { value: 'INICIO_CICLO', titulo: 'A inicio del ciclo (recomendado)',
    desc: 'El primer día del mes (o de la quincena) se carga la CxC. Para mensuales: día 1. Para quincenales: día 1 y día 16.' },
  { value: 'FIN_CICLO', titulo: 'A fin del ciclo',
    desc: 'El último día del ciclo, a la 1:00 AM, se carga la CxC.' },
  { value: 'DIA_DESPUES', titulo: 'Un día después de cerrado el ciclo',
    desc: 'La CxC se genera el primer día después de terminado el ciclo (comportamiento anterior).' },
]

export default function Configuracion() {
  const [modo, setModo] = useState('INICIO_CICLO')
  const [cargando, setCargando] = useState(true)
  const [guardando, setGuardando] = useState(false)
  const [msg, setMsg] = useState('')

  useEffect(() => {
    api.get<{ data: string }>('/procesos/config-cargos')
      .then(r => setModo(r.data.data || 'INICIO_CICLO'))
      .catch(() => {})
      .finally(() => setCargando(false))
  }, [])

  const guardar = async () => {
    setGuardando(true); setMsg('')
    try {
      await api.put('/procesos/config-cargos', { modo })
      setMsg('Configuración guardada correctamente.')
    } catch (e: any) {
      setMsg(e.response?.data?.message || 'Error al guardar')
    } finally { setGuardando(false) }
  }

  return (
    <div className="max-w-3xl space-y-5">
      <div className="flex items-center gap-3">
        <Settings size={22} style={{ color: BRAND }} />
        <h1 className="text-xl font-semibold text-slate-800">Configuración del sistema</h1>
      </div>

      <div className="bg-white rounded-xl border border-slate-200 p-5 space-y-4">
        <div>
          <h2 className="text-sm font-semibold text-slate-700">¿Cuándo generar los cargos (CxC)?</h2>
          <p className="text-xs text-slate-500 mt-0.5">
            Define en qué momento el proceso nocturno (1:00 AM) crea automáticamente las cuentas por cobrar del alquiler.
          </p>
        </div>

        {cargando ? (
          <div className="flex justify-center py-8"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
        ) : (
          <div className="space-y-2.5">
            {OPCIONES.map(o => (
              <label key={o.value}
                className={`flex gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                  modo === o.value ? 'border-blue-400 bg-blue-50' : 'border-slate-200 hover:bg-slate-50'}`}>
                <input type="radio" name="modo" value={o.value} checked={modo === o.value}
                  onChange={() => setModo(o.value)} className="mt-1" />
                <div>
                  <p className="text-sm font-medium text-slate-800">{o.titulo}</p>
                  <p className="text-xs text-slate-500 mt-0.5">{o.desc}</p>
                </div>
              </label>
            ))}
          </div>
        )}

        <div className="flex items-center gap-3 pt-1">
          <button onClick={guardar} disabled={guardando || cargando}
            className="flex items-center gap-2 px-5 py-2 rounded-lg text-white text-sm disabled:opacity-50" style={{ background: BRAND }}>
            <Save size={15} /> {guardando ? 'Guardando...' : 'Guardar configuración'}
          </button>
          {msg && <span className="flex items-center gap-1 text-sm text-green-700"><CheckCircle size={15} /> {msg}</span>}
        </div>
      </div>

      <BackupConfig />

      <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 text-sm text-amber-800">
        <p className="font-semibold mb-1">Nota sobre contratos quincenales</p>
        <p className="text-xs text-amber-700">
          Los contratos quincenales generan <strong>dos cargos por mes</strong> (día 1 y día 16), con el comentario
          "1ra / 2da Quincena del mes". En los reportes de CxC, el valor mensual de un quincenal es el doble del valor del contrato.
          Si el proceso no corrió (PC apagada), al ejecutarlo manualmente desde Procesos se generan todos los cargos pendientes.
        </p>
      </div>
    </div>
  )
}
