import { clsx, type ClassValue } from 'clsx'

export function cn(...inputs: ClassValue[]) {
  return clsx(inputs)
}

export function formatCurrency(amount: number) {
  const abs = Math.abs(amount).toFixed(2)
  const [intPart, dec] = abs.split('.')
  const withCommas = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
  return `${amount < 0 ? '-' : ''}RD$ ${withCommas}.${dec}`
}

/** Retorna la fecha local de hoy en formato YYYY-MM-DD (sin conversión UTC) */
export function hoyLocal(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Primer día del mes actual en formato YYYY-MM-DD (local) */
export function primerDiaMesLocal(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}

export function formatDate(date: string | Date | null | undefined) {
  if (!date) return '-'
  const d = new Date(date)
  if (isNaN(d.getTime())) return '-'
  return d.toLocaleDateString('es-DO', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

export function morosidadColor(nivel: string) {
  switch (nivel) {
    case 'VERDE':   return '#22c55e'
    case 'AMARILLO': return '#eab308'
    case 'NARANJA': return '#f97316'
    case 'ROJO':    return '#ef4444'
    default:        return '#94a3b8'
  }
}

export function estadoBadgeClass(estado: string) {
  switch (estado) {
    case 'ACTIVO': case 'VIGENTE': case 'DISPONIBLE': case 'PAGADO':
      return 'bg-green-100 text-green-800'
    case 'PENDIENTE': case 'ABIERTA':
      return 'bg-yellow-100 text-yellow-800'
    case 'EN_MORA': case 'VENCIDO':
      return 'bg-red-100 text-red-800'
    case 'COBRADO': case 'EN_ADMINISTRACION':
      return 'bg-blue-100 text-blue-800'
    case 'LIQUIDADO':
      return 'bg-purple-100 text-purple-800'
    case 'OCUPADO':
      return 'bg-orange-100 text-orange-800'
    default:
      return 'bg-slate-100 text-slate-700'
  }
}
