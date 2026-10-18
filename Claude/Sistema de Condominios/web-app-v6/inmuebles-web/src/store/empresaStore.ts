import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import api, { type EmpresaData } from '../lib/api'

export interface EmpresaConfig {
  nombre: string
  rnc: string
  telefono: string
  fax: string
  email: string
  logo: string
  direccion: string
  direccion2: string
}

interface EmpresaStore {
  config: EmpresaConfig
  setConfig: (c: EmpresaConfig) => void
  loadFromApi: () => Promise<void>
  saveToApi: (c: EmpresaConfig) => Promise<void>
}

const DEFAULT: EmpresaConfig = {
  nombre: 'Fanny Mercedes & Asoc.',
  rnc: '', telefono: '', fax: '', email: '', logo: '',
  direccion: '', direccion2: '',
}

export const useEmpresaStore = create<EmpresaStore>()(
  persist(
    (set, get) => ({
      config: DEFAULT,
      setConfig: config => set({ config }),

      loadFromApi: async () => {
        try {
          const r = await api.get<{ data: EmpresaData }>('/empresa')
          const d = r.data.data
          if (d?.nombre) {
            set({
              config: {
                nombre: d.nombre, rnc: d.rnc, telefono: d.telefono,
                fax: d.fax, email: d.email, logo: d.logo,
                direccion: d.direccion, direccion2: d.direccion2,
              }
            })
          }
        } catch { /* mantiene cache local si falla */ }
      },

      saveToApi: async (config: EmpresaConfig) => {
        await api.put('/empresa', {
          nombre: config.nombre, direccion: config.direccion,
          direccion2: config.direccion2, rnc: config.rnc,
          telefono: config.telefono, fax: config.fax,
          email: config.email, logo: config.logo,
        })
        set({ config })
      },
    }),
    { name: 'empresa-config' }
  )
)
