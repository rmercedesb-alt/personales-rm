import { useEffect, useState } from 'react'
import { RefreshCw, Loader2, AlertTriangle, CalendarClock, Percent } from 'lucide-react'
import api, { type ProcesosEstado } from '../../lib/api'

export default function Procesos() {
  const [estado, setEstado] = useState<ProcesosEstado | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [runningMora, setRunningMora] = useState(false)
  const [runningCargos, setRunningCargos] = useState(false)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'error'; texto: string } | null>(null)

  const load = () => {
    setLoading(true); setLoadError('')
    api.get<{ data: ProcesosEstado }>('/procesos/estado')
      .then(r => setEstado(r.data.data))
      .catch(() => setLoadError('No se pudo cargar el estado de los procesos. Verifica que el servidor esté corriendo.'))
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const formatFecha = (f: string | null) =>
    f ? new Date(f).toLocaleString('es-DO', { dateStyle: 'medium', timeStyle: 'short' }) : 'Nunca se ha ejecutado'

  const recalcularMora = async () => {
    setRunningMora(true); setMsg(null)
    try {
      const r = await api.post('/procesos/recalcular-mora')
      setMsg({ tipo: 'ok', texto: r.data.message })
      load()
    } catch (err: any) {
      setMsg({ tipo: 'error', texto: err.response?.data?.message || 'Error al recalcular la mora' })
    } finally { setRunningMora(false) }
  }

  const generarCargos = async () => {
    setRunningCargos(true); setMsg(null)
    try {
      const r = await api.post('/procesos/generar-cargos')
      setMsg({ tipo: 'ok', texto: r.data.message })
      load()
    } catch (err: any) {
      setMsg({ tipo: 'error', texto: err.response?.data?.message || 'Error al generar los cargos' })
    } finally { setRunningCargos(false) }
  }

  return (
    <div className="space-y-4 max-w-3xl">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Procesos automáticos</h1>
        <p className="text-slate-500 text-sm mt-0.5">
          Estos procesos corren automáticamente todas las noches a la 1:00 a.m. También puedes ejecutarlos manualmente aquí.
        </p>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : loadError ? (
        <div className="flex items-center gap-2 rounded-lg px-4 py-3 text-sm bg-red-50 text-red-700 border border-red-200">
          <AlertTriangle size={15} />
          {loadError}
        </div>
      ) : (
        <div className="space-y-4">
          {/* Generación de cargos recurrentes */}
          <div className="bg-white rounded-xl border border-slate-200 p-5">
            <div className="flex items-start justify-between gap-4">
              <div className="flex items-start gap-3">
                <div className="w-10 h-10 rounded-lg bg-blue-50 flex items-center justify-center shrink-0">
                  <CalendarClock size={18} className="text-blue-600" />
                </div>
                <div>
                  <h2 className="font-semibold text-slate-800">Generación de cargos de alquiler</h2>
                  <p className="text-sm text-slate-500 mt-0.5">
                    Crea automáticamente la próxima CxC de ALQUILER de cada contrato vigente, según su día de cobro
                    (mensual) o cada 15 días (quincenal). No duplica cargos ya generados.
                  </p>
                  <p className="text-xs text-slate-400 mt-2">
                    Última ejecución: <span className="font-medium text-slate-600">{formatFecha(estado?.ultimaGeneracionCargos ?? null)}</span>
                  </p>
                </div>
              </div>
              <button onClick={generarCargos} disabled={runningCargos}
                className="flex items-center gap-2 px-3 py-2 rounded-lg text-white text-xs font-medium hover:opacity-90 disabled:opacity-60 shrink-0"
                style={{ background: '#1B4F8C' }}>
                {runningCargos ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
                Generar ahora
              </button>
            </div>
          </div>

          {/* Recálculo de mora */}
          <div className="bg-white rounded-xl border border-slate-200 p-5">
            <div className="flex items-start justify-between gap-4">
              <div className="flex items-start gap-3">
                <div className="w-10 h-10 rounded-lg bg-amber-50 flex items-center justify-center shrink-0">
                  <Percent size={18} className="text-amber-600" />
                </div>
                <div>
                  <h2 className="font-semibold text-slate-800">Recálculo de mora</h2>
                  <p className="text-sm text-slate-500 mt-0.5">
                    Recalcula la mora de todas las CxC pendientes según los días de gracia y el % (o monto fijo) de mora
                    de cada contrato, multiplicado por la cantidad de períodos atrasados.
                  </p>
                  <p className="text-xs text-slate-400 mt-2">
                    Última ejecución: <span className="font-medium text-slate-600">{formatFecha(estado?.ultimaActualizacionMora ?? null)}</span>
                  </p>
                </div>
              </div>
              <button onClick={recalcularMora} disabled={runningMora}
                className="flex items-center gap-2 px-3 py-2 rounded-lg text-white text-xs font-medium hover:opacity-90 disabled:opacity-60 shrink-0"
                style={{ background: '#92400e' }}>
                {runningMora ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
                Recalcular ahora
              </button>
            </div>
          </div>

          {msg && (
            <div className={`flex items-center gap-2 rounded-lg px-4 py-3 text-sm ${
              msg.tipo === 'ok' ? 'bg-green-50 text-green-700 border border-green-200' : 'bg-red-50 text-red-700 border border-red-200'
            }`}>
              {msg.tipo === 'error' && <AlertTriangle size={15} />}
              {msg.texto}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
