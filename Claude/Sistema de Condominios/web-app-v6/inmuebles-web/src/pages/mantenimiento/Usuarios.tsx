import { useEffect, useState } from 'react'
import { Plus, Pencil, Check, X, Eye, EyeOff, ShieldCheck } from 'lucide-react'
import api from '../../lib/api'

const ROLES = ['ADMINISTRADOR', 'SUPERVISOR', 'ASISTENTE', 'COBRADOR', 'LECTURA']

interface Usuario {
  email: string
  nombre: string
  rol: string
  activo: string
}

interface Form {
  email: string
  nombre: string
  rol: string
  activo: string
  password: string
}

const emptyForm: Form = { email: '', nombre: '', rol: 'ASISTENTE', activo: 'SI', password: '' }

const ROLE_COLORS: Record<string, string> = {
  ADMINISTRADOR: 'bg-red-100 text-red-800',
  SUPERVISOR:    'bg-orange-100 text-orange-800',
  ASISTENTE:     'bg-blue-100 text-blue-800',
  COBRADOR:      'bg-purple-100 text-purple-800',
  LECTURA:       'bg-slate-100 text-slate-600',
}

export default function Usuarios() {
  const [usuarios, setUsuarios] = useState<Usuario[]>([])
  const [loading, setLoading]   = useState(true)
  const [error, setError]       = useState('')
  const [showModal, setShowModal] = useState(false)
  const [editing, setEditing]     = useState<Usuario | null>(null)
  const [form, setForm]           = useState<Form>(emptyForm)
  const [showPwd, setShowPwd]     = useState(false)
  const [saving, setSaving]       = useState(false)

  const load = async () => {
    try {
      setLoading(true)
      const res = await api.get<{ data: Usuario[] }>('/usuarios')
      setUsuarios(res.data.data ?? [])
    } catch {
      setError('No se pudieron cargar los usuarios.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const openCreate = () => {
    setEditing(null)
    setForm(emptyForm)
    setShowPwd(false)
    setShowModal(true)
  }

  const openEdit = (u: Usuario) => {
    setEditing(u)
    setForm({ email: u.email, nombre: u.nombre, rol: u.rol, activo: u.activo, password: '' })
    setShowPwd(false)
    setShowModal(true)
  }

  const handleSave = async () => {
    if (!form.email || !form.nombre || !form.rol) return
    if (!editing && !form.password) return
    setSaving(true)
    try {
      if (editing) {
        await api.put(`/usuarios/${encodeURIComponent(editing.email)}`, {
          nombre: form.nombre, rol: form.rol, activo: form.activo,
          ...(form.password ? { password: form.password } : {})
        })
      } else {
        await api.post('/usuarios', form)
      }
      setShowModal(false)
      await load()
    } catch {
      alert('Error al guardar usuario.')
    } finally {
      setSaving(false)
    }
  }

  const toggleActivo = async (u: Usuario) => {
    try {
      await api.put(`/usuarios/${encodeURIComponent(u.email)}`, {
        nombre: u.nombre, rol: u.rol,
        activo: u.activo === 'SI' ? 'NO' : 'SI'
      })
      await load()
    } catch {
      alert('Error al actualizar estado.')
    }
  }

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-slate-800">Usuarios</h1>
          <p className="text-slate-500 text-sm mt-1">Gestión de accesos al sistema</p>
        </div>
        <button onClick={openCreate}
          className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white rounded-lg"
          style={{ background: '#1B4F8C' }}>
          <Plus size={16} /> Nuevo usuario
        </button>
      </div>

      <p className="text-red-600 text-sm" style={{ display: error ? '' : 'none' }}>{error}</p>

      <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-slate-400 text-sm">Cargando...</div>
        ) : usuarios.length === 0 ? (
          <div className="p-8 text-center text-slate-400 text-sm">No hay usuarios registrados.</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-slate-50 border-b border-slate-200">
              <tr>
                <th className="text-left px-4 py-3 text-slate-600 font-medium">Nombre</th>
                <th className="text-left px-4 py-3 text-slate-600 font-medium">Email</th>
                <th className="text-left px-4 py-3 text-slate-600 font-medium">Rol</th>
                <th className="text-center px-4 py-3 text-slate-600 font-medium">Activo</th>
                <th className="text-center px-4 py-3 text-slate-600 font-medium">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {usuarios.map(u => (
                <tr key={u.email} className="hover:bg-slate-50 transition-colors">
                  <td className="px-4 py-3 font-medium text-slate-800">
                    <div className="flex items-center gap-2">
                      <div className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold text-white"
                        style={{ background: '#1B4F8C' }}>
                        {u.nombre[0]}
                      </div>
                      {u.nombre}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-slate-500">{u.email}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold ${ROLE_COLORS[u.rol] ?? 'bg-slate-100 text-slate-600'}`}>
                      <ShieldCheck size={11} /> {u.rol}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button onClick={() => toggleActivo(u)}
                      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold transition-colors ${u.activo === 'SI' ? 'bg-green-100 text-green-700 hover:bg-green-200' : 'bg-slate-100 text-slate-500 hover:bg-slate-200'}`}>
                      {u.activo === 'SI' ? <><Check size={11} /> Activo</> : <><X size={11} /> Inactivo</>}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button onClick={() => openEdit(u)}
                      className="p-1.5 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-700 transition-colors">
                      <Pencil size={14} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Modal */}
      {showModal && (
        <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-xl w-full max-w-md">
            <div className="flex items-center justify-between px-5 py-4 border-b border-slate-200">
              <h2 className="font-semibold text-slate-800">{editing ? 'Editar usuario' : 'Nuevo usuario'}</h2>
              <button onClick={() => setShowModal(false)} className="text-slate-400 hover:text-slate-600">
                <X size={18} />
              </button>
            </div>

            <div className="p-5 space-y-4">
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Email *</label>
                <input
                  type="email" value={form.email} disabled={!!editing}
                  onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
                  className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-slate-50 disabled:text-slate-400"
                  placeholder="usuario@dominio.com"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Nombre completo *</label>
                <input
                  type="text" value={form.nombre}
                  onChange={e => setForm(f => ({ ...f, nombre: e.target.value }))}
                  className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  placeholder="Nombre Apellido"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Rol *</label>
                <select value={form.rol} onChange={e => setForm(f => ({ ...f, rol: e.target.value }))}
                  className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
                  {ROLES.map(r => <option key={r} value={r}>{r}</option>)}
                </select>
                <p className="text-xs text-slate-400 mt-1">
                  {{
                    ADMINISTRADOR: 'Acceso total al sistema.',
                    SUPERVISOR:    'Todo excepto gestión de usuarios.',
                    ASISTENTE:     'Cobros, CxC y reportes.',
                    COBRADOR:      'Solo registrar pagos en caja.',
                    LECTURA:       'Solo consultar, sin modificar.',
                  }[form.rol]}
                </p>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">
                  {editing ? 'Nueva clave (dejar vacío para no cambiar)' : 'Clave *'}
                </label>
                <div className="relative">
                  <input
                    type={showPwd ? 'text' : 'password'} value={form.password}
                    onChange={e => setForm(f => ({ ...f, password: e.target.value }))}
                    className="w-full border border-slate-300 rounded-lg px-3 py-2 pr-9 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                    placeholder={editing ? '••••••••' : 'Mínimo 6 caracteres'}
                  />
                  <button type="button" onClick={() => setShowPwd(v => !v)}
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600">
                    {showPwd ? <EyeOff size={15} /> : <Eye size={15} />}
                  </button>
                </div>
              </div>

              {editing && (
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Estado</label>
                  <select value={form.activo} onChange={e => setForm(f => ({ ...f, activo: e.target.value }))}
                    className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
                    <option value="SI">Activo</option>
                    <option value="NO">Inactivo</option>
                  </select>
                </div>
              )}
            </div>

            <div className="flex items-center justify-end gap-3 px-5 py-4 border-t border-slate-200">
              <button onClick={() => setShowModal(false)}
                className="px-4 py-2 text-sm text-slate-600 hover:text-slate-800 transition-colors">
                Cancelar
              </button>
              <button onClick={handleSave} disabled={saving}
                className="px-4 py-2 text-sm font-medium text-white rounded-lg disabled:opacity-60"
                style={{ background: '#1B4F8C' }}>
                {saving ? 'Guardando...' : editing ? 'Guardar cambios' : 'Crear usuario'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
