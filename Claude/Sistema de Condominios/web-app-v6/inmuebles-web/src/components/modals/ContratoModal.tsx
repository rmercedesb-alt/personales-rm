import { useEffect, useState } from 'react'
import { X, Loader2, CheckCircle2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import api, { type Inmueble, type Inquilino, type Contrato } from '../../lib/api'

interface Props { onClose: () => void; onSaved: () => void; inicial?: Contrato | null }

type DepositoPrompt = { idInquilino: string; idCxC: string; monto: number }

export default function ContratoModal({ onClose, onSaved, inicial }: Props) {
  const navigate = useNavigate()
  const esEdicion = !!inicial
  const [depositoPrompt, setDepositoPrompt] = useState<DepositoPrompt | null>(null)
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
  const [inmSearch, setInmSearch] = useState(''); const [inmOpen, setInmOpen] = useState(false)
  const [inqSearch, setInqSearch] = useState(''); const [inqOpen, setInqOpen] = useState(false)

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
        onSaved()
      } else {
        const res = await api.post<{ data: { idCxCDeposito: string | null; idInquilino: string; montoDeposito: number } }>('/contratos', form)
        const d = res.data.data
        if (d.idCxCDeposito) {
          // V6 #5 — se generó la CxC del depósito: ofrecer cobrarla de inmediato.
          setDepositoPrompt({ idInquilino: d.idInquilino, idCxC: d.idCxCDeposito, monto: d.montoDeposito })
        } else {
          onSaved()
        }
      }
    }
    catch (err: any) { setError(err.response?.data?.message || 'Error al guardar') }
    finally { setLoading(false) }
  }

  const irACobrarDeposito = () => {
    const d = depositoPrompt!
    onSaved()
    navigate(`/pagos?idInquilino=${d.idInquilino}&idCxC=${d.idCxC}`)
  }

  const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-slate-50 disabled:text-slate-400'

  if (depositoPrompt) {
    return (
      <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
        <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md p-6 text-center">
          <div className="mx-auto w-12 h-12 rounded-full bg-green-100 flex items-center justify-center mb-3">
            <CheckCircle2 size={26} className="text-green-600" />
          </div>
          <h2 className="font-semibold text-slate-800 text-lg">Contrato creado</h2>
          <p className="text-sm text-slate-500 mt-2">
            Se cargó una <strong>CxC de depósito</strong> ({depositoPrompt.idCxC}) por{' '}
            <strong>RD$ {depositoPrompt.monto.toLocaleString('es-DO', { minimumFractionDigits: 2 })}</strong>{' '}
            al inquilino.
          </p>
          <p className="text-sm text-slate-600 mt-3 font-medium">¿Realizar el recibo de pago ahora?</p>
          <div className="flex justify-center gap-3 mt-5">
            <button onClick={() => { onSaved() }}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Más tarde
            </button>
            <button onClick={irACobrarDeposito}
              className="px-5 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: '#1B4F8C' }}>
              Cobrar depósito ahora
            </button>
          </div>
        </div>
      </div>
    )
  }

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
                <div className="col-span-2 relative">
                  <label className="block text-xs font-medium text-slate-600 mb-1">Inmueble disponible *</label>
                  {(() => {
                    const sel = inmuebles.find(i => i.id === form.idInmueble)
                    const filtrados = inmuebles
                      .filter(i => {
                        const q = inmSearch.toLowerCase()
                        return !q || i.id.toLowerCase().includes(q) || i.descripcion.toLowerCase().includes(q) || i.direccion.toLowerCase().includes(q) || i.sector.toLowerCase().includes(q) || i.ciudad.toLowerCase().includes(q)
                      })
                      .sort((a, b) => a.descripcion.localeCompare(b.descripcion, 'es'))
                    return (
                      <>
                        <input value={inmOpen ? inmSearch : (sel ? `${sel.descripcion} (${sel.id})` : '')}
                          onFocus={() => { setInmOpen(true); setInmSearch('') }}
                          onBlur={() => setTimeout(() => setInmOpen(false), 150)}
                          onChange={e => setInmSearch(e.target.value)}
                          className={inp} placeholder="Buscar por descripción, dirección, sector..." />
                        <input type="text" required value={form.idInmueble} readOnly tabIndex={-1} className="sr-only" onChange={() => {}} />
                        {inmOpen && (
                          <div className="absolute z-50 top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-lg shadow-lg max-h-52 overflow-y-auto">
                            {filtrados.length === 0
                              ? <p className="px-3 py-2 text-xs text-slate-400">Sin resultados</p>
                              : filtrados.map(i => (
                                <button key={i.id} type="button"
                                  onMouseDown={() => { setForm(f => ({ ...f, idInmueble: i.id, montoAlquiler: i.montoAlquiler })); setInmOpen(false) }}
                                  className={`w-full text-left px-3 py-2 text-sm hover:bg-blue-50 ${form.idInmueble === i.id ? 'bg-blue-50' : ''}`}>
                                  <span className="font-medium text-slate-800">{i.descripcion}</span>
                                  <span className="text-xs text-slate-400 ml-2">{i.id} · {i.sector} · RD$ {i.montoAlquiler.toLocaleString()}</span>
                                </button>
                              ))
                            }
                          </div>
                        )}
                      </>
                    )
                  })()}
                </div>
                <div className="col-span-2 relative">
                  <label className="block text-xs font-medium text-slate-600 mb-1">Inquilino *</label>
                  {(() => {
                    const sel = inquilinos.find(i => i.id === form.idInquilino)
                    const filtrados = inquilinos
                      .filter(i => {
                        const q = inqSearch.toLowerCase()
                        return !q || i.id.toLowerCase().includes(q) || i.nombre.toLowerCase().includes(q) || i.cedula.toLowerCase().includes(q) || i.telefono.toLowerCase().includes(q)
                      })
                      .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'))
                    return (
                      <>
                        <input value={inqOpen ? inqSearch : (sel?.nombre ?? '')}
                          onFocus={() => { setInqOpen(true); setInqSearch('') }}
                          onBlur={() => setTimeout(() => setInqOpen(false), 150)}
                          onChange={e => setInqSearch(e.target.value)}
                          className={inp} placeholder="Buscar por nombre, cédula o teléfono..." />
                        <input type="text" required value={form.idInquilino} readOnly tabIndex={-1} className="sr-only" onChange={() => {}} />
                        {inqOpen && (
                          <div className="absolute z-50 top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-lg shadow-lg max-h-52 overflow-y-auto">
                            {filtrados.length === 0
                              ? <p className="px-3 py-2 text-xs text-slate-400">Sin resultados</p>
                              : filtrados.map(i => (
                                <button key={i.id} type="button"
                                  onMouseDown={() => { set('idInquilino', i.id); setInqOpen(false) }}
                                  className={`w-full text-left px-3 py-2 text-sm hover:bg-blue-50 ${form.idInquilino === i.id ? 'bg-blue-50' : ''}`}>
                                  <span className="font-medium text-slate-800 flex-1">{i.nombre}</span>
                                  <span className="text-xs text-slate-400 ml-2">{i.cedula} · {i.telefono}</span>
                                </button>
                              ))
                            }
                          </div>
                        )}
                      </>
                    )
                  })()}
                </div>
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
          <p className="text-xs text-slate-400 bg-slate-50 rounded-lg px-3 py-2"
            style={{ display: esEdicion ? '' : 'none' }}>
            Los cambios de mora/gracia/periodicidad aplican a los próximos recálculos automáticos; no modifican CxC ya generadas.
          </p>
          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
            style={{ display: error ? '' : 'none' }}>{error}</p>
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: '#1B4F8C' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              {esEdicion ? 'Guardar cambios' : 'Crear contrato'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

function F({ label, children, colSpan }: { label: string; children: React.ReactNode; colSpan?: boolean }) {
  return (
    <div className={colSpan ? 'col-span-2' : ''}>
      <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
      {children}
    </div>
  )
}
