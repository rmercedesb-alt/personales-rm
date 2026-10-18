import { useState, useRef, useEffect } from 'react'
import { ChevronDown, Check } from 'lucide-react'

export interface MultiSelectOption { value: string; label: string }

interface Props {
  options: MultiSelectOption[]
  selected: string[]              // [] = todos
  onChange: (sel: string[]) => void
  allLabel?: string
  placeholder?: string
}

/** Selección múltiple con casilla "Todos". selected vacío = todos. */
export default function MultiSelect({ options, selected, onChange, allLabel = 'Todos', placeholder = 'Seleccionar...' }: Props) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const todos = selected.length === 0
  const toggle = (v: string) => {
    if (selected.includes(v)) onChange(selected.filter(s => s !== v))
    else onChange([...selected, v])
  }

  const texto = todos
    ? allLabel
    : selected.length === 1
      ? options.find(o => o.value === selected[0])?.label ?? `${selected.length} seleccionados`
      : `${selected.length} seleccionados`

  return (
    <div className="relative" ref={ref}>
      <button type="button" onClick={() => setOpen(o => !o)}
        className="w-full flex items-center gap-2 border border-slate-300 rounded-lg px-3 py-2 text-sm text-left bg-white focus:outline-none focus:ring-2 focus:ring-blue-500">
        <span className="flex-1 truncate text-slate-700">{texto || placeholder}</span>
        <ChevronDown size={15} className="text-slate-400" />
      </button>

      {open && (
        <div className="absolute z-20 top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-lg shadow-lg max-h-64 overflow-y-auto">
          <button type="button" onClick={() => onChange([])}
            className="w-full flex items-center gap-2 px-3 py-2 text-sm hover:bg-blue-50 border-b border-slate-100">
            <span className={`w-4 h-4 rounded border flex items-center justify-center ${todos ? 'bg-blue-600 border-blue-600' : 'border-slate-300'}`}>
              {todos && <Check size={12} className="text-white" />}
            </span>
            <span className="font-medium">{allLabel}</span>
          </button>
          {options.map(o => {
            const marcado = selected.includes(o.value)
            return (
              <button key={o.value} type="button" onClick={() => toggle(o.value)}
                className="w-full flex items-center gap-2 px-3 py-2 text-sm hover:bg-blue-50 border-b border-slate-50 last:border-0">
                <span className={`w-4 h-4 rounded border flex items-center justify-center ${marcado ? 'bg-blue-600 border-blue-600' : 'border-slate-300'}`}>
                  {marcado && <Check size={12} className="text-white" />}
                </span>
                <span className="truncate text-slate-700">{o.label}</span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
