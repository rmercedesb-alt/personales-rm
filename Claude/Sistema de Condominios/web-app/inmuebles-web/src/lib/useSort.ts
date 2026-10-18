import { useState, useMemo } from 'react'

type Dir = 'asc' | 'desc'

export function useSort<T>(data: T[], defaultKey: keyof T, defaultDir: Dir = 'asc') {
  const [key, setKey] = useState<keyof T>(defaultKey)
  const [dir, setDir] = useState<Dir>(defaultDir)

  const toggle = (k: keyof T) => {
    if (k === key) setDir(d => d === 'asc' ? 'desc' : 'asc')
    else { setKey(k); setDir('asc') }
  }

  const sorted = useMemo(() => [...data].sort((a, b) => {
    const av = a[key]; const bv = b[key]
    if (av == null && bv == null) return 0
    if (av == null) return 1
    if (bv == null) return -1
    const cmp = typeof av === 'number' && typeof bv === 'number'
      ? av - bv
      : String(av).localeCompare(String(bv), 'es', { numeric: true })
    return dir === 'asc' ? cmp : -cmp
  }), [data, key, dir])

  return { sorted, sortKey: key, sortDir: dir, toggle }
}
