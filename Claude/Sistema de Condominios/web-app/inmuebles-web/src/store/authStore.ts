import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string | null
  nombre: string | null
  rol: string | null
  email: string | null
  login: (token: string, nombre: string, rol: string, email: string) => void
  logout: () => void
  isAdmin: () => boolean
  isAsistente: () => boolean
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      token: null,
      nombre: null,
      rol: null,
      email: null,
      login: (token, nombre, rol, email) => set({ token, nombre, rol, email }),
      logout: () => set({ token: null, nombre: null, rol: null, email: null }),
      isAdmin: () => get().rol === 'ADMINISTRADOR',
      isAsistente: () => get().rol === 'ADMINISTRADOR' || get().rol === 'ASISTENTE',
    }),
    { name: 'inmuebles-auth' }
  )
)
