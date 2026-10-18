import { useEffect, useState } from 'react'
import { X, Loader2 } from 'lucide-react'
import api, { type Inmueble, type Inquilino, type Contrato } from '../../lib/api'

interface Props { onClose: () => void; onSaved: () => void; inicial?: Contrato | null }

export default function ContratoModal({ onClose, onSaved, inicial }: Props) {
  const esEdicion = !!inicial
  const [inmuebles, setInmuebles] = useState<Inmueble[]>([])
  const [inquilinos, setInquilinos] = useState<Inquilino[]>([])
  const [form, setForm] = useState({
    idInmueble: inicial?.idInmueble ?? '', idInquilino: inicial?.idInquilino ?? '',
    fechaInicio: inicial?.fechaInicio?.toString().split('T')[0] ?? '',
    fechaFin: inicial?.fechaFin?.toString().split('T')[0] ?? '',
    montoAlquiler: inicial?.montoAlquiler ?? 0, periodicidad: inicial?.periodicidad ?? 'MENSUAL',
    diaCobro: inicial?.diaCobro ?? 1, diasGracia: inicial?.diasGracia ?? 5,
    tipoMora: inicial?.tipoMora ?? 'PORCENTAJE', moraPorcentaje: inicial?.moraPorcentaje ?? 5, montoMoraFija: inicial?.montoMoraFija ?? 0,
    deposito: inicial?.deposito ?? 0, estado: inicial?.estado ?? 'VIGENTE', notas: inicial?.notas ?? ''
  })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (esEdicion) return
    Promise.all([
      api.get<{ data: Inmueble[] }>('/inmuebles?soloDisponibles=true'),
      api.get<{ data: Inquilino[] }>('/inquilinos')
    ]).then(([inm, inq]) => {
      setInmuebles(inm.data.data)
      setInquilinos(inq.data.data)
    })
  }, [esEdicion])

  const set = (k: string, v: string | number) => setForm(f => ({ ...f, [k]: v }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setLoading(true); setError('')
    try {
      if (esEdicion) {
        await api.put(`/contratos/${inicial!.id}`, form)
      } else {
        await api.post('/contratos', form)
      }
      onSaved()
    }
    catch (err: any) { setError(err.response?.data?.message || 'Error al guardar') }
    finally { setLoading(false) }
  }

  const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-slate-50 disabled:text-slate-400'
  const F = ({ label, children, colSpan }: { label: string; children: React.ReactNode; colSpan?: boolean }) => (
    <div className={colSpan ? 'col-span-2' : ''}>
      <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
      {children}
    </div>
  )

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[92vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <div>
            <h2 className="font-semibold text-slate-800">{esEdicion ? 'Editar contrato' : 'Nuevo contrato'}</h2>
            {esEdicion && <p className="text-xs text-slate-400 mt-0.5">{inicial!.id} · {inicial!.nombreInquilino}</p>}
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-4">
          <div className="grid grid-cols-2 gap-3">
            {esEdicion ? (
              <>
                <F label="Inmueble" colSpan>
                  <input disabled value={`${inicial!.nombreInmueble} (${inicial!.idInmueble})`} className={inp} />
                </F>
                <F label="Inquilino" colSpan>
                  <input disabled value={inicial!.nombreInquilino} className={inp} />
                </F>
                <F label="Fecha inicio">
                  <input disabled value={form.fechaInicio} className={inp} />
                </F>
              </>
            ) : (
              <>
                <F label="Inmueble disponible *" colSpan>
                  <select required value={form.idInmueble} onChange={e => {
                    const inm = inmuebles.find(i => i.id === e.target.value)
                    setForm(f => ({ ...f, idInmueble: e.target.value, montoAlquiler: inm?.montoAlquiler ?? f.montoAlquiler }))
                  }} className={inp}>
                    <option value="">Seleccionar...</option>
                    {inmuebles.map(i => <option key={i.id} value={i.id}>{i.descripcion} ({i.id})</option>)}
                  </select>
                </F>
                <F label="Inquilino *" colSpan>
                  <select required value={form.idInquilino} onChange={e => set('idInquilino', e.target.value)} className={inp}>
                    <option value="">Seleccionar...</option>
                    {inquilinos.map(i => <option key={i.id} value={i.id}>{i.nombre}</option>)}
                  </select>
                </F>
                <F label="Fecha inicio *">
                  <input type="date" required value={form.fechaInicio} onChange={e => set('fechaInicio', e.target.value)} className={inp} />
                </F>
              </>
            )}
            <F label="Fecha fin *">
              <input type="date" required value={form.fechaFin} onChange={e => set('fechaFin', e.target.value)} className={inp} />
            </F>
            <F label="Monto alquiler (RD$) *">
              <input type="number" required min={0} value={form.montoAlquiler} onChange={e => set('montoAlquiler', +e.target.value)} className={inp} />
            </F>
            <F label="Periodicidad">
              <select value={form.periodicidad} onChange={e => set('periodicidad', e.target.value)} className={inp}>
                <option value="MENSUAL">Mensual</option>
                <option value="QUINCENAL">Quincenal</option>
              </select>
            </F>
            <F label="Día de cobro">
              <input type="number" min={1} max={28} value={form.diaCobro} onChange={e => set('diaCobro', +e.target.value)} className={inp} />
            </F>
            <F label="Días de gracia">
              <input type="number" min={0} max={30} value={form.diasGracia} onChange={e => set('diasGracia', +e.target.value)} className={inp} />
            </F>
            <F label="Tipo mora">
              <select value={form.tipoMora} onChange={e => set('tipoMora', e.target.value)} className={inp}>
                <option value="PORCENTAJE">Porcentaje</option>
                <option value="MONTO_FIJO">Monto fijo</option>
              </select>
            </F>
            <F label={form.tipoMora === 'PORCENTAJE' ? 'Mora % (por período atrasado)' : 'Mora RD$ (por período atrasado)'}>
              {form.tipoMora === 'PORCENTAJE'
                ? <input type="number" min={0} max={100} step={0.1} value={form.moraPorcentaje} onChange={e => set('moraPorcentaje', +e.target.value)} className={inp} />
                : <input type="number" min={0} value={form.montoMoraFija} onChange={e => set('montoMoraFija', +e.target.value)} className={inp} />
              }
            </F>
            <F label="Depósito (RD$)" colSpan={!esEdicion}>
              <input type="number" min={0} value={form.deposito} onChange={e => set('deposito', +e.target.value)} className={inp} />
            </F>
            {esEdicion && (
              <F label="Estado">
                <select value={form.estado} onChange={e => set('estado', e.target.value)} className={inp}>
                  <option value="VIGENTE">Vigente</option>
                  <option value="FINALIZADO">Finalizado</option>
                  <option value="CANCELADO">Cancelado</option>
                </select>
              </F>
            )}
            <F label="Notas" colSpan>
              <textarea value={form.notas} onChange={e => set('notas', e.target.value)} className={inp + ' resize-none'} rows={2} />
            </F>
          </div>
          {esEdicion && (
            <p className="text-xs text-slate-400 bg-slate-50 rounded-lg px-3 py-2">
              Los cambios de mora/gracia/periodicidad aplican a los próximos recálculos automáticos; no modifican CxC ya generadas.
            </p>
          )}
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: '#1B4F8C' }}>
              {loading && <Loader2 size={14} className="animate-spin" />}
              {esEdicion ? 'Guardar cambios' : 'Crear contrato'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
