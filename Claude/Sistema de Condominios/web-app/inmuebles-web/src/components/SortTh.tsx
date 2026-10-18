import { ChevronUp, ChevronDown, ChevronsUpDown } from 'lucide-react'

interface Props {
  label: string
  sortKey: string
  active: string
  dir: 'asc' | 'desc'
  onSort: (k: string) => void
  className?: string
}

export default function SortTh({ label, sortKey, active, dir, onSort, className = '' }: Props) {
  const isActive = active === sortKey
  return (
    <th
      onClick={() => onSort(sortKey)}
      className={`px-4 py-3 font-medium text-slate-600 cursor-pointer select-none whitespace-nowrap group ${className}`}
    >
      <span className="flex items-center gap-1">
        {label}
        <span className={`transition-colors ${isActive ? 'text-blue-600' : 'text-slate-300 group-hover:text-slate-400'}`}>
          {isActive
            ? dir === 'asc' ? <ChevronUp size={13} /> : <ChevronDown size={13} />
            : <ChevronsUpDown size={13} />}
        </span>
      </span>
    </th>
  )
}
