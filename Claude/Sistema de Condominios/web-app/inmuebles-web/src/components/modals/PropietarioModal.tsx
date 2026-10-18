import { useEffect, useState } from 'react'
import { X, Loader2, Plus, Pencil, Trash2, AlertTriangle } from 'lucide-react'
import api from '../../lib/api'
import type { Propietario, CoPropietario } from '../../lib/api'

interface Props { onClose: () => void; onSaved: () => void; inicial?: Propietario | null }

export default function PropietarioModal({ onClose, onSaved, inicial }: Props) {
  const [form, setForm] = useState({
    nombre:             inicial?.nombre             ?? '',
    cedula:             inicial?.cedula             ?? '',
    telefono:           inicial?.telefono           ?? '',
    email:              inicial?.email              ?? '',
    banco:              inicial?.banco              ?? '',
    numeroCuenta:       inicial?.numeroCuenta       ?? '',
    tipoCuenta:         inicial?.tipoCuenta         ?? 'AHORRO',
    tipoComision:       inicial?.tipoComision       ?? 'PORCENTAJE',
    comisionPorcentaje: inicial?.comisionPorcentaje ?? 10,
    comisionFija:       inicial?.comisionFija       ?? 0,
    estado:             inicial?.estado             ?? 'ACTIVO',
    notas:              inicial?.notas              ?? '',
    tipoDueno:          inicial?.tipoDueno          ?? 'ÚNICO',
  })
  const [loading, setLoading]           = useState(false)
  const [error, setError]               = useState('')
  const [coProps, setCoProps]           = useState<CoPropietario[]>([])
  const [coPropModal, setCoPropModal]   = useState<CoPropietario | null | 'nuevo'>(null)

  const set = (k: string, v: string | number) => setForm(f => ({ ...f, [k]: v }))

  const loadCoProps = () => {
    if (inicial?.id && form.tipoDueno === 'FAMILIAR') {
      api.get<{ data: CoPropietario[] }>(`/propietarios/${inicial.id}/copropietarios`)
        .then(r => setCoProps(r.data.data))
    }
  }

  useEffect(() => { loadCoProps() }, [inicial?.id, form.tipoDueno])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      if (inicial) {
        await api.put(`/propietarios/${inicial.id}`, form)
      } else {
        await api.post('/propietarios', form)
      }
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al guardar')
    } finally { setLoading(false) }
  }

  const totalPeso = coProps.reduce((s, c) => s + c.pesoPorc, 0)
  const pesoOk    = Math.abs(totalPeso - 100) < 0.01

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">{inicial ? 'Editar propietario' : 'Nuevo propietario'}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>

        <form onSubmit={submit} className="p-5 space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <Field label="Nombre completo *" colSpan>
              <input required value={form.nombre} onChange={e => set('nombre', e.target.value)}
                className={inputCls} placeholder="Juan Pérez García" />
            </Field>
            <Field label="Cédula">
              <input value={form.cedula} onChange={e => set('cedula', e.target.value)}
                className={inputCls} placeholder="001-0000000-0" />
            </Field>
            <Field label="Teléfono">
              <input value={form.telefono} onChange={e => set('telefono', e.target.value)}
                className={inputCls} placeholder="809-000-0000" />
            </Field>
            <Field label="Email">
              <input type="email" value={form.email} onChange={e => set('email', e.target.value)}
                className={inputCls} placeholder="correo@ejemplo.com" />
            </Field>
            <Field label="Banco">
              <select value={form.banco} onChange={e => set('banco', e.target.value)} className={inputCls}>
                <option value="">Seleccionar...</option>
                {['BanReservas','Popular','BHD León','Scotiabank','Banistmo','Asociación Cibao','Otra'].map(b =>
                  <option key={b}>{b}</option>)}
              </select>
            </Field>
            <Field label="Tipo de cuenta">
              <select value={form.tipoCuenta} onChange={e => set('tipoCuenta', e.target.value)} className={inputCls}>
                <option value="AHORRO">Ahorro</option>
                <option value="CORRIENTE">Corriente</option>
              </select>
            </Field>
            <Field label="Número de cuenta" colSpan>
              <input value={form.numeroCuenta} onChange={e => set('numeroCuenta', e.target.value)}
                className={inputCls} placeholder="000-00000000-0" />
            </Field>
            <Field label="Tipo de comisión">
              <select value={form.tipoComision} onChange={e => set('tipoComision', e.target.value)} className={inputCls}>
                <option value="PORCENTAJE">Porcentaje</option>
                <option value="FIJO">Monto fijo</option>
                <option value="MIXTO">Mixto</option>
              </select>
            </Field>
            <Field label="Comisión %">
              <input type="number" min={0} max={100} step={0.1}
                value={form.comisionPorcentaje} onChange={e => set('comisionPorcentaje', +e.target.value)}
                className={inputCls} disabled={form.tipoComision === 'FIJO'} />
            </Field>
            <Field label="Tipo de Dueño" colSpan>
              <div className="flex gap-3">
                {['ÚNICO', 'FAMILIAR'].map(t => (
                  <label key={t} className={`flex-1 flex items-center justify-center gap-2 py-2 rounded-lg border-2 cursor-pointer text-sm font-medium transition-colors ${
                    form.tipoDueno === t
                      ? 'border-blue-600 bg-blue-50 text-blue-700'
                      : 'border-slate-200 text-slate-600 hover:border-slate-300'
                  }`}>
                    <input type="radio" className="sr-only" value={t}
                      checked={form.tipoDueno === t} onChange={() => set('tipoDueno', t)} />
                    {t === 'ÚNICO' ? '👤 Único' : '👨‍👩‍👧 Familiar'}
                  </label>
                ))}
              </div>
            </Field>
            {inicial && (
              <Field label="Estado">
                <select value={form.estado} onChange={e => set('estado', e.target.value)} className={inputCls}>
                  <option value="ACTIVO">Activo</option>
                  <option value="INACTIVO">Inactivo</option>
                </select>
              </Field>
            )}
            <Field label="Notas" colSpan>
              <textarea value={form.notas} onChange={e => set('notas', e.target.value)}
                className={inputCls + ' resize-none'} rows={2} />
            </Field>
          </div>

          {/* ─── CO-PROPIETARIOS (solo si FAMILIAR + edición existente) ─── */}
          {form.tipoDueno === 'FAMILIAR' && inicial && (
            <div className="border border-slate-200 rounded-xl overflow-hidden">
              <div className="flex items-center justify-between px-4 py-3 bg-slate-50 border-b border-slate-200">
                <div>
                  <h3 className="text-sm font-semibold text-slate-700">Miembros de la familia</h3>
                  <p className={`text-xs mt-0.5 font-medium ${pesoOk ? 'text-green-600' : totalPeso > 100 ? 'text-red-600' : 'text-amber-600'}`}>
                    Peso total: {totalPeso.toFixed(1)}% {!pesoOk && `(debe ser 100%)`}
                  </p>
                </div>
                <button type="button" onClick={() => setCoPropModal('nuevo')}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-white text-xs font-medium hover:opacity-90"
                  style={{ background: '#1B4F8C' }}>
                  <Plus size={13} /> Agregar
                </button>
              </div>
              {coProps.length === 0 ? (
                <div className="px-4 py-6 text-center text-sm text-slate-400">
                  Sin miembros registrados. Haga clic en "Agregar" para añadir el primero.
                </div>
              ) : (
                <table className="w-full text-xs">
                  <thead className="bg-white border-b border-slate-100">
                    <tr>
                      <th className="text-left px-4 py-2 font-medium text-slate-600">Nombre</th>
                      <th className="text-left px-4 py-2 font-medium text-slate-600">Cédula</th>
                      <th className="text-left px-4 py-2 font-medium text-slate-600">Teléfono</th>
                      <th className="text-right px-4 py-2 font-medium text-slate-600">Peso %</th>
                      <th className="px-4 py-2 w-16" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-50 bg-white">
                    {coProps.map(c => (
                      <tr key={c.id} className="hover:bg-slate-50">
                        <td className="px-4 py-2 font-medium text-slate-800">{c.nombre}</td>
                        <td className="px-4 py-2 text-slate-500">{c.cedula}</td>
                        <td className="px-4 py-2 text-slate-500">{c.telefono}</td>
                        <td className="px-4 py-2 text-right font-semibold text-blue-700">{c.pesoPorc}%</td>
                        <td className="px-4 py-2">
                          <div className="flex items-center gap-1 justify-end">
                            <button type="button" onClick={() => setCoPropModal(c)}
                              className="p-1 rounded hover:bg-slate-100 text-slate-400 hover:text-blue-600">
                              <Pencil size={12} />
                            </button>
                            <button type="button" onClick={async () => {
                              if (!confirm(`¿Eliminar a ${c.nombre}?`)) return
                              await api.delete(`/copropietarios/${c.id}`)
                              loadCoProps()
                            }} className="p-1 rounded hover:bg-red-50 text-slate-400 hover:text-red-600">
                              <Trash2 size={12} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              {!pesoOk && coProps.length > 0 && (
                <div className="px-4 py-2 bg-amber-50 border-t border-amber-200 flex items-center gap-2 text-xs text-amber-700">
                  <AlertTriangle size={13} /> La suma de pesos debe ser exactamente 100%. Actualmente: {totalPeso.toFixed(1)}%
                </div>
              )}
            </div>
          )}

          {form.tipoDueno === 'FAMILIAR' && !inicial && (
            <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-xs text-blue-700">
              Guarde el propietario primero; luego podrá agregar los miembros de la familia desde la opción de editar.
            </div>
          )}

          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}

          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Cancelar
            </button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              {loading && <Loader2 size={14} className="animate-spin" />}
              {inicial ? 'Guardar cambios' : 'Guardar propietario'}
            </button>
          </div>
        </form>
      </div>

      {/* Modal de co-propietario */}
      {coPropModal && inicial && (
        <CoPropietarioModal
          idPropietario={inicial.id}
          pesoDisponible={coPropModal === 'nuevo' ? 100 - totalPeso : 100 - totalPeso + (coPropModal as CoPropietario).pesoPorc}
          inicial={coPropModal === 'nuevo' ? null : coPropModal as CoPropietario}
          onClose={() => setCoPropModal(null)}
          onSaved={() => { setCoPropModal(null); loadCoProps() }}
        />
      )}
    </div>
  )
}

// ─── Modal Co-Propietario ───────────────────────────────────────────────────
interface CoPropProps {
  idPropietario: string
  pesoDisponible: number
  inicial: CoPropietario | null
  onClose: () => void
  onSaved: () => void
}

function CoPropietarioModal({ idPropietario, pesoDisponible, inicial, onClose, onSaved }: CoPropProps) {
  const [form, setForm] = useState({
    nombre:       inicial?.nombre       ?? '',
    cedula:       inicial?.cedula       ?? '',
    telefono:     inicial?.telefono     ?? '',
    email:        inicial?.email        ?? '',
    banco:        inicial?.banco        ?? '',
    numeroCuenta: inicial?.numeroCuenta ?? '',
    tipoCuenta:   inicial?.tipoCuenta   ?? 'AHORRO',
    pesoPorc:     inicial?.pesoPorc     ?? Math.max(0, Math.min(pesoDisponible, 100)),
    estado:       inicial?.estado       ?? 'ACTIVO',
    notas:        inicial?.notas        ?? '',
  })
  const [loading, setLoading] = useState(false)
  const [error, setError]     = useState('')

  const setF = (k: string, v: string | number) => setForm(f => ({ ...f, [k]: v }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (form.pesoPorc <= 0) { setError('El peso % debe ser mayor a 0'); return }
    if (form.pesoPorc > pesoDisponible + 0.01) {
      setError(`El peso máximo disponible es ${pesoDisponible.toFixed(1)}%`); return
    }
    setLoading(true); setError('')
    try {
      if (inicial) {
        await api.put(`/copropietarios/${inicial.id}`, form)
      } else {
        await api.post(`/propietarios/${idPropietario}/copropietarios`, { idPropietario, ...form })
      }
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al guardar')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/60 z-[60] flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md max-h-[85vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">
            {inicial ? 'Editar co-propietario' : 'Nuevo co-propietario'}
          </h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <Field label="Nombre completo *" colSpan>
              <input required value={form.nombre} onChange={e => setF('nombre', e.target.value)}
                className={inputCls} placeholder="María Pérez" />
            </Field>
            <Field label="Cédula">
              <input value={form.cedula} onChange={e => setF('cedula', e.target.value)}
                className={inputCls} placeholder="001-0000000-0" />
            </Field>
            <Field label="Teléfono">
              <input value={form.telefono} onChange={e => setF('telefono', e.target.value)}
                className={inputCls} placeholder="809-000-0000" />
            </Field>
            <Field label="Email" colSpan>
              <input type="email" value={form.email} onChange={e => setF('email', e.target.value)}
                className={inputCls} placeholder="correo@ejemplo.com" />
            </Field>
            <Field label="Banco">
              <select value={form.banco} onChange={e => setF('banco', e.target.value)} className={inputCls}>
                <option value="">Seleccionar...</option>
                {['BanReservas','Popular','BHD León','Scotiabank','Banistmo','Asociación Cibao','Otra'].map(b =>
                  <option key={b}>{b}</option>)}
              </select>
            </Field>
            <Field label="Tipo de cuenta">
              <select value={form.tipoCuenta} onChange={e => setF('tipoCuenta', e.target.value)} className={inputCls}>
                <option value="AHORRO">Ahorro</option>
                <option value="CORRIENTE">Corriente</option>
              </select>
            </Field>
            <Field label="Número de cuenta" colSpan>
              <input value={form.numeroCuenta} onChange={e => setF('numeroCuenta', e.target.value)}
                className={inputCls} placeholder="000-00000000-0" />
            </Field>
            <Field label={`Peso % * (disponible: ${pesoDisponible.toFixed(1)}%)`} colSpan>
              <input type="number" required min={0.1} max={100} step={0.1}
                value={form.pesoPorc} onChange={e => setF('pesoPorc', +e.target.value)}
                className={inputCls} />
            </Field>
            {inicial && (
              <Field label="Estado">
                <select value={form.estado} onChange={e => setF('estado', e.target.value)} className={inputCls}>
                  <option value="ACTIVO">Activo</option>
                  <option value="INACTIVO">Inactivo</option>
                </select>
              </Field>
            )}
            <Field label="Notas" colSpan>
              <textarea value={form.notas} onChange={e => setF('notas', e.target.value)}
                className={inputCls + ' resize-none'} rows={2} />
            </Field>
          </div>

          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}

          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Cancelar
            </button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              {loading && <Loader2 size={14} className="animate-spin" />}
              {inicial ? 'Guardar cambios' : 'Agregar miembro'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

const inputCls = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white disabled:bg-slate-50 disabled:text-slate-400'

function Field({ label, children, colSpan }: { label: string; children: React.ReactNode; colSpan?: boolean }) {
  return (
    <div className={colSpan ? 'col-span-2' : ''}>
      <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
      {children}
    </div>
  )
}
