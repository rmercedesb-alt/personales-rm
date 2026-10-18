import { useEffect, useState } from 'react'
import { X, Loader2 } from 'lucide-react'
import api, { type CxCCargo, type Inquilino, type Inmueble, type Contrato } from '../../lib/api'

interface Props {
  onClose: () => void
  onSaved: () => void
  inicial?: CxCCargo | null   // null = crear, CxCCargo = editar
}

const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white'

const TIPOS_CARGO = ['ALQUILER', 'MORA', 'DEPOSITO', 'CARGO_EXTRA', 'OTRO']

function hoy() { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` }
function primerDiaMes() { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` }
function ultimoDiaMes() { const n = new Date(); const d = new Date(n.getFullYear(), n.getMonth()+1, 0); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` }

export default function CxCModal({ onClose, onSaved, inicial }: Props) {
  const esEdicion = !!inicial

  const [inquilinos, setInquilinos] = useState<Inquilino[]>([])
  const [inmuebles,  setInmuebles]  = useState<Inmueble[]>([])
  const [contratos,  setContratos]  = useState<Contrato[]>([])
  const [loading,    setLoading]    = useState(false)
  const [error,      setError]      = useState('')
  const [buscarInq,  setBuscarInq]  = useState('')

  // Formulario crear
  const [form, setForm] = useState({
    idContrato:      '',
    idInquilino:     '',
    idInmueble:      '',
    tipoCargo:       'ALQUILER',
    descripcion:     '',
    periodoInicio:   primerDiaMes(),
    periodoFin:      ultimoDiaMes(),
    fechaVencimiento: hoy(),
    montoOriginal:   '',
    notas:           '',
  })

  // Formulario ajuste (edición)
  const [ajuste, setAjuste] = useState({
    montoOriginal:    inicial ? String(inicial.montoOriginal) : '',
    fechaVencimiento: inicial ? inicial.fechaVencimiento?.toString().split('T')[0] ?? hoy() : hoy(),
    descripcion:      inicial?.descripcion ?? '',
    notas:            inicial?.notas ?? '',
    motivoAjuste:     '',
  })

  useEffect(() => {
    if (!esEdicion) {
      Promise.all([
        api.get<{ data: Inquilino[] }>('/inquilinos'),
        api.get<{ data: Inmueble[] }>('/inmuebles'),
        api.get<{ data: Contrato[] }>('/contratos'),
      ]).then(([ri, rm, rc]) => {
        setInquilinos(ri.data.data)
        setInmuebles(rm.data.data)
        setContratos(rc.data.data)
      })
    }
  }, [esEdicion])

  // Auto-fill inmueble/contrato cuando cambia inquilino
  const handleInquilinoChange = (idInq: string) => {
    setForm(f => ({ ...f, idInquilino: idInq, idInmueble: '', idContrato: '' }))
    const contrato = contratos.find(c => c.idInquilino === idInq && c.estado === 'VIGENTE')
    if (contrato) {
      setForm(f => ({
        ...f,
        idInquilino:     idInq,
        idInmueble:      contrato.idInmueble,
        idContrato:      contrato.id,
        montoOriginal:   String(contrato.montoAlquiler),
        descripcion:     `Alquiler ${new Date().toLocaleString('es-DO', { month: 'long', year: 'numeric' })}`,
      }))
    }
  }

  const setF = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  const setA = (k: keyof typeof ajuste) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setAjuste(a => ({ ...a, [k]: e.target.value }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      if (esEdicion) {
        if (!ajuste.motivoAjuste.trim()) { setError('Debe indicar el motivo del ajuste'); setLoading(false); return }
        await api.put(`/cxc/${inicial!.id}`, {
          montoOriginal:    parseFloat(ajuste.montoOriginal),
          fechaVencimiento: ajuste.fechaVencimiento,
          descripcion:      ajuste.descripcion,
          notas:            ajuste.notas,
          motivoAjuste:     ajuste.motivoAjuste,
        })
      } else {
        if (!form.idInquilino || !form.idInmueble) { setError('Selecciona inquilino e inmueble'); setLoading(false); return }
        await api.post('/cxc', {
          idContrato:      form.idContrato,
          idInquilino:     form.idInquilino,
          idInmueble:      form.idInmueble,
          tipoCargo:       form.tipoCargo,
          descripcion:     form.descripcion,
          periodoInicio:   form.periodoInicio,
          periodoFin:      form.periodoFin,
          fechaVencimiento: form.fechaVencimiento,
          montoOriginal:   parseFloat(form.montoOriginal || '0'),
          notas:           form.notas,
        })
      }
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al guardar')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[92vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <div>
            <h2 className="font-semibold text-slate-800">
              {esEdicion ? 'Ajustar CxC' : 'Nueva cuenta por cobrar'}
            </h2>
            {esEdicion && (
              <p className="text-xs text-slate-400 mt-0.5">{inicial!.id} · {inicial!.nombreInquilino}</p>
            )}
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>

        <form onSubmit={submit} className="p-5 space-y-4">

          {!esEdicion && (
            <>
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Inquilino *</label>
                <input value={buscarInq} onChange={e => setBuscarInq(e.target.value)}
                  placeholder="Buscar por nombre, cédula, teléfono o email..."
                  className={inp + ' mb-1.5'} />
                <select required value={form.idInquilino}
                  onChange={e => handleInquilinoChange(e.target.value)} className={inp}>
                  <option value="">Seleccionar inquilino...</option>
                  {inquilinos
                    .filter(i => {
                      const t = buscarInq.toLowerCase()
                      return !t || i.nombre.toLowerCase().includes(t) || i.cedula.includes(buscarInq) ||
                        i.telefono.includes(buscarInq) || i.email.toLowerCase().includes(t)
                    })
                    .map(i => <option key={i.id} value={i.id}>{i.nombre} · {i.cedula || 's/cédula'} · {i.telefono}</option>)}
                </select>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Inmueble *</label>
                <select required value={form.idInmueble}
                  onChange={e => setForm(f => ({ ...f, idInmueble: e.target.value }))} className={inp}>
                  <option value="">Seleccionar inmueble...</option>
                  {inmuebles.map(i => <option key={i.id} value={i.id}>{i.descripcion}</option>)}
                </select>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Contrato vinculado</label>
                <select value={form.idContrato}
                  onChange={e => setForm(f => ({ ...f, idContrato: e.target.value }))} className={inp}>
                  <option value="">(Ninguno / cargo manual)</option>
                  {contratos.filter(c => !form.idInquilino || c.idInquilino === form.idInquilino).map(c =>
                    <option key={c.id} value={c.id}>{c.id} · {c.nombreInmueble}</option>
                  )}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Tipo de cargo</label>
                  <select value={form.tipoCargo} onChange={e => setForm(f => ({ ...f, tipoCargo: e.target.value }))} className={inp}>
                    {TIPOS_CARGO.map(t => <option key={t}>{t}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Monto (RD$) *</label>
                  <input type="number" min={0} step="0.01" required value={form.montoOriginal}
                    onChange={setF('montoOriginal')} className={inp} placeholder="0.00" />
                </div>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Descripción *</label>
                <input required value={form.descripcion} onChange={setF('descripcion')} className={inp}
                  placeholder="Alquiler junio 2026..." />
              </div>

              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Período inicio</label>
                  <input type="date" value={form.periodoInicio} onChange={setF('periodoInicio')} className={inp} />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Período fin</label>
                  <input type="date" value={form.periodoFin} onChange={setF('periodoFin')} className={inp} />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Vencimiento</label>
                  <input type="date" value={form.fechaVencimiento} onChange={setF('fechaVencimiento')} className={inp} />
                </div>
              </div>
            </>
          )}

          {esEdicion && (
            <>
              <div className="bg-slate-50 rounded-lg p-3 text-xs text-slate-500 space-y-1">
                <div className="flex justify-between"><span>Tipo:</span><span className="font-medium text-slate-700">{inicial!.tipoCargo}</span></div>
                <div className="flex justify-between"><span>Estado actual:</span><span className="font-medium text-slate-700">{inicial!.estado}</span></div>
                <div className="flex justify-between"><span>Monto pagado:</span><span className="font-medium text-slate-700">RD$ {inicial!.montoPagado.toLocaleString()}</span></div>
              </div>

              <MoraEditor cxc={inicial!} onUpdated={onSaved} />

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Monto original (RD$)</label>
                  <input type="number" min={0} step="0.01" value={ajuste.montoOriginal}
                    onChange={setA('montoOriginal')} className={inp} />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Nuevo vencimiento</label>
                  <input type="date" value={ajuste.fechaVencimiento} onChange={setA('fechaVencimiento')} className={inp} />
                </div>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Descripción</label>
                <input value={ajuste.descripcion} onChange={setA('descripcion')} className={inp} />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">
                  Motivo del ajuste * <span className="text-red-500">(requerido para el log)</span>
                </label>
                <input required value={ajuste.motivoAjuste} onChange={setA('motivoAjuste')} className={inp}
                  placeholder="Ej: Corrección de monto por acuerdo con el inquilino..." />
              </div>
            </>
          )}

          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Notas</label>
            <textarea
              value={esEdicion ? ajuste.notas : form.notas}
              onChange={esEdicion
                ? (e) => setAjuste(a => ({ ...a, notas: e.target.value }))
                : setF('notas')}
              className={inp + ' resize-none'} rows={2} />
          </div>

          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
            style={{ display: error ? '' : 'none' }}>{error}</p>

          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Cancelar
            </button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              {esEdicion ? 'Guardar ajuste' : 'Crear CxC'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

function MoraEditor({ cxc, onUpdated }: { cxc: CxCCargo; onUpdated: () => void }) {
  const [montoMora, setMontoMora] = useState(String(cxc.montoMora))
  const [motivo, setMotivo] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [open, setOpen] = useState(false)

  const guardar = async () => {
    if (!motivo.trim()) { setError('Indique el motivo del ajuste de mora'); return }
    setLoading(true); setError('')
    try {
      await api.put(`/cxc/${cxc.id}/mora`, { montoMora: parseFloat(montoMora || '0'), motivoAjuste: motivo })
      onUpdated()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al ajustar la mora')
    } finally { setLoading(false) }
  }

  return (
    <div className="border border-amber-200 bg-amber-50 rounded-lg p-3 space-y-2">
      <div className="flex items-center justify-between">
        <div className="text-xs text-amber-800">
          <p className="font-medium">Mora actual: RD$ {cxc.montoMora.toLocaleString()}</p>
          <p className="text-amber-600 mt-0.5">
            {cxc.fechaUltimaActualizacionMora
              ? `Última actualización: ${new Date(cxc.fechaUltimaActualizacionMora).toLocaleString('es-DO')}`
              : 'Aún no se ha recalculado automáticamente'}
          </p>
        </div>
        <button type="button" onClick={() => setOpen(o => !o)} className="text-xs text-blue-600 hover:underline">
          {open ? 'Cerrar' : 'Editar mora'}
        </button>
      </div>
      {open && (
        <div className="space-y-2 pt-1 border-t border-amber-200">
          <div>
            <label className="block text-xs font-medium text-amber-800 mb-1">Nueva mora (RD$)</label>
            <input type="number" min={0} step="0.01" value={montoMora} onChange={e => setMontoMora(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-amber-300 text-sm bg-white" />
          </div>
          <div>
            <label className="block text-xs font-medium text-amber-800 mb-1">Motivo del ajuste *</label>
            <input value={motivo} onChange={e => setMotivo(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-amber-300 text-sm bg-white"
              placeholder="Ej: Condonación parcial acordada con el inquilino..." />
          </div>
          <p className="text-xs text-red-600 bg-red-50 rounded px-2 py-1"
            style={{ display: error ? '' : 'none' }}>{error}</p>
          <button type="button" onClick={guardar} disabled={loading}
            className="px-3 py-1.5 rounded-lg text-white text-xs font-medium hover:opacity-90 disabled:opacity-50"
            style={{ background: '#92400e' }}>
            {loading ? 'Guardando...' : 'Guardar mora'}
          </button>
        </div>
      )}
    </div>
  )
}
