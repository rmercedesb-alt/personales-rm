import { Building2 } from 'lucide-react'
import { useEmpresaStore } from '../store/empresaStore'

interface Props {
  titulo: string
  subtitulo?: string
  filtros?: Record<string, string>
}

export default function PrintHeader({ titulo, subtitulo, filtros }: Props) {
  const { config } = useEmpresaStore()
  return (
    <div className="hidden print:block mb-4">
      <div className="flex items-center gap-4 border-b-2 border-slate-700 pb-3 mb-3">
        <img src={config.logo || ''} alt="Logo" className="h-14 object-contain"
          style={{ display: config.logo ? '' : 'none' }} />
        <div className="w-12 h-12 rounded-lg bg-[#1B4F8C] flex items-center justify-center shrink-0"
          style={{ display: config.logo ? 'none' : '' }}>
          <Building2 size={22} className="text-white" />
        </div>
        <div className="flex-1">
          <p className="font-bold text-slate-900 text-lg leading-tight">{config.nombre}</p>
          <div className="flex flex-wrap gap-x-5 gap-y-0 text-xs text-slate-500 mt-0.5">
            {config.rnc && <span>RNC: {config.rnc}</span>}
            {config.telefono && <span>Tel: {config.telefono}</span>}
            {config.fax && <span>Fax: {config.fax}</span>}
            {config.email && <span>{config.email}</span>}
          </div>
          {(config.direccion || config.direccion2) && (
            <p className="text-xs text-slate-400 mt-0.5">
              {[config.direccion, config.direccion2].filter(Boolean).join(' · ')}
            </p>
          )}
        </div>
        <div className="text-right text-xs text-slate-400">
          <p>{new Date().toLocaleDateString('es-DO', { year: 'numeric', month: 'long', day: 'numeric' })}</p>
          <p>{new Date().toLocaleTimeString('es-DO', { hour: '2-digit', minute: '2-digit' })}</p>
        </div>
      </div>

      <div className="mb-2">
        <h1 className="text-xl font-bold text-slate-800">{titulo}</h1>
        {subtitulo && <p className="text-sm text-slate-500">{subtitulo}</p>}
      </div>

      {filtros && Object.keys(filtros).length > 0 && (
        <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-slate-500 mb-3 p-2 bg-slate-50 rounded border border-slate-200">
          {Object.entries(filtros).map(([k, v]) => (
            <span key={k} style={{ display: v ? '' : 'none' }}><strong>{k}:</strong> {v}</span>
          ))}
        </div>
      )}
    </div>
  )
}
