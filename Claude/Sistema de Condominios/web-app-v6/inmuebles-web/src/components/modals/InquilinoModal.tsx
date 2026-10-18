import { useState } from 'react'
import { X, Loader2 } from 'lucide-react'
import api from '../../lib/api'
import type { Inquilino } from '../../lib/api'

interface Props {
  onClose: () => void
  onSaved: () => void
  inicial?: Inquilino | null
}

const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

export default function InquilinoModal({ onClose, onSaved, inicial }: Props) {
  const [form, setForm] = useState({
    nombre:            inicial?.nombre            ?? '',
    cedula:            inicial?.cedula            ?? '',
    pasaporte:         inicial?.pasaporte         ?? '',
    telefono:          inicial?.telefono          ?? '',
    email:             inicial?.email             ?? '',
    ocupacion:         inicial?.ocupacion         ?? '',
    referenciaPersonal: inicial?.referenciaPersonal ?? '',
    telefonoReferencia: inicial?.telefonoReferencia  ?? '',
    notas:             inicial?.notas             ?? '',
    estado:            inicial?.estado            ?? 'ACTIVO',
    enviarWhatsApp:    inicial?.enviarWhatsApp    ?? false,
  })
  const [loading, setLoading] = useState(false)
  const [error,   setError]   = useState('')

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      if (inicial) {
        await api.put(`/inquilinos/${inicial.id}`, form)
      } else {
        await api.post('/inquilinos', form)
      }
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al guardar')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">{inicial ? 'Editar inquilino' : 'Nuevo inquilino'}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>

        <form onSubmit={submit} className="p-5 space-y-4">
          <div className="grid grid-cols-2 gap-3">

            <div className="col-span-2">
              <label className="block text-xs font-medium text-slate-600 mb-1">Nombre completo *</label>
              <input required className={inp} value={form.nombre} onChange={set('nombre')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Cédula</label>
              <input className={inp} placeholder="001-0000000-0" value={form.cedula} onChange={set('cedula')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Pasaporte</label>
              <input className={inp} placeholder="N° de pasaporte" value={form.pasaporte} onChange={set('pasaporte')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Teléfono</label>
              <input className={inp} placeholder="809-000-0000" value={form.telefono} onChange={set('telefono')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Correo electrónico</label>
              <input type="email" className={inp} value={form.email} onChange={set('email')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Ocupación</label>
              <input className={inp} value={form.ocupacion} onChange={set('ocupacion')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Referencia personal</label>
              <input className={inp} value={form.referenciaPersonal} onChange={set('referenciaPersonal')} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Teléfono referencia</label>
              <input className={inp} value={form.telefonoReferencia} onChange={set('telefonoReferencia')} />
            </div>

            <div className="col-span-2">
              <label className="block text-xs font-medium text-slate-600 mb-1">Notas</label>
              <textarea className={inp + ' resize-none'} rows={2} value={form.notas} onChange={set('notas')} />
            </div>

            <div className="col-span-2">
              <label className="flex items-center gap-2 cursor-pointer text-sm text-slate-700 bg-green-50 border border-green-200 rounded-lg px-3 py-2">
                <input type="checkbox" checked={form.enviarWhatsApp}
                  onChange={e => setForm(f => ({ ...f, enviarWhatsApp: e.target.checked }))}
                  className="rounded" />
                📲 Enviar mensajes vía WhatsApp a este inquilino
              </label>
            </div>

          </div>

          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
            style={{ display: error ? '' : 'none' }}>{error}</p>

          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Cancelar
            </button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90 disabled:opacity-60"
              style={{ background: '#1B4F8C' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              {inicial ? 'Guardar cambios' : 'Guardar inquilino'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
