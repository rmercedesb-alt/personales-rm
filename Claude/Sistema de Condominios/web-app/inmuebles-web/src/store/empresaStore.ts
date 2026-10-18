import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export interface EmpresaConfig {
  nombre: string
  rnc: string
  telefono: string
  email: string
  logo: string   // base64 data URL
}

interface EmpresaStore {
  config: EmpresaConfig
  setConfig: (c: EmpresaConfig) => void
}

const DEFAULT: EmpresaConfig = {
  nombre: 'Fanny Mercedes & Asoc.',
  rnc: '',
  telefono: '',
  email: '',
  logo: '',
}

export const useEmpresaStore = create<EmpresaStore>()(
  persist(
    set => ({
      config: DEFAULT,
      setConfig: config => set({ config }),
    }),
    { name: 'empresa-config' }
  )
)
