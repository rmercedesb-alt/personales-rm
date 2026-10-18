import { useRef, useState } from 'react'
import { Building2, Save, Upload, X } from 'lucide-react'
import { useEmpresaStore, type EmpresaConfig } from '../../store/empresaStore'

const inp = 'w-full px-3 py-2.5 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white'

export default function Empresa() {
  const { config, saveToApi } = useEmpresaStore()
  const [form, setForm] = useState<EmpresaConfig>({ ...config })
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const set = (k: keyof EmpresaConfig) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm(f => ({ ...f, [k]: e.target.value }))

  const handleLogo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = ev => setForm(f => ({ ...f, logo: ev.target?.result as string }))
    reader.readAsDataURL(file)
  }

  const handleSave = async () => {
    setSaving(true); setError('')
    try {
      await saveToApi(form)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch {
      setError('Error al guardar. Verifique la conexión con la API.')
    } finally { setSaving(false) }
  }

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Datos de la empresa</h1>
        <p className="text-slate-500 text-sm mt-0.5">
          Esta información aparece en el encabezado de todos los informes e impresiones.
        </p>
      </div>

      <div className="bg-white rounded-xl border border-slate-200 p-6 space-y-5">

        {/* Logo */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-2">Logo de la empresa</label>
          <div className="flex items-center gap-4">
            <div className="w-24 h-24 rounded-xl border-2 border-dashed border-slate-200 flex items-center justify-center bg-slate-50 overflow-hidden shrink-0">
              {form.logo
                ? <img src={form.logo} alt="Logo" className="w-full h-full object-contain p-1" />
                : <Building2 size={32} className="text-slate-300" />}
            </div>
            <div className="space-y-2">
              <button type="button" onClick={() => fileRef.current?.click()}
                className="flex items-center gap-2 px-3 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
                <Upload size={14} /> Subir imagen
              </button>
              <button type="button" onClick={() => setForm(f => ({ ...f, logo: '' }))}
                className="flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-red-500 hover:bg-red-50"
                style={{ display: form.logo ? '' : 'none' }}>
                <X size={14} /> Quitar logo
              </button>
              <p className="text-xs text-slate-400">PNG, JPG o SVG. Recomendado: 200×200 px.</p>
            </div>
            <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={handleLogo} />
          </div>
        </div>

        <hr className="border-slate-100" />

        <div className="grid grid-cols-1 gap-4">
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">Nombre de la empresa *</label>
            <input value={form.nombre} onChange={set('nombre')} className={inp}
              placeholder="Fanny Mercedes & Asoc." />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">RNC</label>
              <input value={form.rnc} onChange={set('rnc')} className={inp} placeholder="1-30-12345-6" />
            </div>
            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">Teléfono</label>
              <input value={form.telefono} onChange={set('telefono')} className={inp} placeholder="809-555-0000" />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">Fax</label>
              <input value={form.fax} onChange={set('fax')} className={inp} placeholder="809-555-0001" />
            </div>
            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">Correo electrónico</label>
              <input type="email" value={form.email} onChange={set('email')} className={inp}
                placeholder="info@empresa.com" />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">Dirección</label>
            <input value={form.direccion} onChange={set('direccion')} className={inp}
              placeholder="Calle principal #123" />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">Dirección línea 2</label>
            <input value={form.direccion2} onChange={set('direccion2')} className={inp}
              placeholder="Sector, Ciudad, País" />
          </div>
        </div>

        <hr className="border-slate-100" />

        {/* Vista previa */}
        <div>
          <p className="text-sm font-medium text-slate-700 mb-2">Vista previa del encabezado de impresión</p>
          <div className="border border-slate-200 rounded-lg p-4 bg-slate-50">
            <div className="flex items-center gap-4 border-b border-slate-300 pb-3 mb-3">
              {form.logo
                ? <img src={form.logo} alt="" className="h-12 object-contain" />
                : <div className="w-12 h-12 rounded-lg bg-[#1B4F8C] flex items-center justify-center">
                    <Building2 size={22} className="text-white" />
                  </div>}
              <div>
                <p className="font-bold text-slate-800 text-base">{form.nombre || 'Nombre de la empresa'}</p>
                <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-slate-500 mt-0.5">
                  {form.rnc && <span>RNC: {form.rnc}</span>}
                  {form.telefono && <span>Tel: {form.telefono}</span>}
                  {form.email && <span>{form.email}</span>}
                </div>
                {(form.direccion || form.direccion2) && (
                  <p className="text-xs text-slate-400 mt-0.5">
                    {[form.direccion, form.direccion2].filter(Boolean).join(' · ')}
                  </p>
                )}
              </div>
            </div>
            <p className="text-xs text-slate-400 italic">[ Contenido del informe ]</p>
          </div>
        </div>

        <p className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
          style={{ display: error ? '' : 'none' }}>{error}</p>

        <div className="flex justify-end">
          <button onClick={handleSave} disabled={saving}
            className="flex items-center gap-2 px-5 py-2.5 rounded-lg text-white text-sm font-medium hover:opacity-90 disabled:opacity-60 transition-all"
            style={{ background: saved ? '#16a34a' : '#1B4F8C' }}>
            <Save size={15} />
            {saving ? 'Guardando...' : saved ? '¡Guardado!' : 'Guardar cambios'}
          </button>
        </div>
      </div>
    </div>
  )
}
