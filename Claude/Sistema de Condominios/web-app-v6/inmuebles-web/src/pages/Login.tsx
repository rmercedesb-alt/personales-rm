import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Building2, Eye, EyeOff, Loader2 } from 'lucide-react'
import api from '../lib/api'
import { useAuthStore } from '../store/authStore'
import { cn } from '../lib/utils'

export default function Login() {
  const navigate = useNavigate()
  const login = useAuthStore(s => s.login)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [show, setShow] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const { data } = await api.post('/auth/login', { email, password })
      if (data.success) {
        login(data.data.token, data.data.nombre, data.data.rol, email)
        navigate('/')
      } else {
        setError(data.message || 'Usuario o contraseña incorrectos')
      }
    } catch (err: any) {
      // Distinguir error de red vs credenciales incorrectas
      if (!err.response) {
        setError('⚠️ No se puede conectar con el servidor. Verifica que la ventana "API – Inmuebles" esté abierta y sin errores.')
      } else if (err.response.status === 401) {
        setError('Usuario o contraseña incorrectos')
      } else {
        setError(`Error del servidor (${err.response.status}): ${err.response.data?.message ?? 'intenta de nuevo'}`)
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div
      className="min-h-screen flex items-center justify-center p-4"
      style={{
        background: 'linear-gradient(135deg, #06102a 0%, #0e2f55 50%, #1B4F8C 100%)'
      }}
    >
      {/* Subtle building silhouette overlay */}
      <div className="absolute inset-0 opacity-5"
        style={{
          backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1440 320'%3E%3Cpath fill='%23ffffff' d='M0,320 L0,200 L60,200 L60,120 L80,120 L80,100 L100,100 L100,120 L120,120 L120,160 L180,160 L180,80 L200,80 L200,60 L220,60 L220,80 L240,80 L240,160 L300,160 L300,200 L360,200 L360,100 L380,100 L380,40 L400,40 L400,20 L420,20 L420,40 L440,40 L440,100 L500,100 L500,160 L560,160 L560,80 L580,80 L580,60 L600,60 L600,80 L620,80 L620,160 L680,160 L680,120 L700,120 L700,80 L720,80 L720,60 L740,60 L740,80 L760,80 L760,120 L780,120 L780,160 L840,160 L840,100 L860,100 L860,40 L880,40 L880,20 L900,20 L900,40 L920,40 L920,100 L980,100 L980,160 L1040,160 L1040,80 L1060,80 L1060,60 L1080,60 L1080,80 L1100,80 L1100,160 L1160,160 L1160,120 L1180,120 L1180,100 L1200,100 L1200,120 L1220,120 L1220,200 L1280,200 L1280,160 L1300,160 L1300,100 L1320,100 L1320,160 L1380,160 L1380,200 L1440,200 L1440,320 Z'/%3E%3C/svg%3E")`,
          backgroundRepeat: 'no-repeat',
          backgroundPosition: 'bottom',
          backgroundSize: 'cover'
        }}
      />

      <div className="relative w-full max-w-sm">
        {/* Card */}
        <div className="bg-white rounded-2xl shadow-2xl p-8">
          {/* Logo */}
          <div className="flex flex-col items-center mb-8">
            <div className="w-14 h-14 rounded-2xl flex items-center justify-center mb-3 shadow-lg"
              style={{ background: '#1B4F8C' }}>
              <Building2 size={28} className="text-white" />
            </div>
            <h1 className="text-xl font-semibold text-slate-800">Admin Inmuebles RD</h1>
            <p className="text-sm text-slate-500 mt-0.5">Sistema de Administración</p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1.5">
                Correo electrónico
              </label>
              <input
                type="email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                className="w-full px-3 py-2.5 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent transition-all"
                placeholder="admin@empresa.com"
                required
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1.5">
                Contraseña
              </label>
              <div className="relative">
                <input
                  type={show ? 'text' : 'password'}
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  className="w-full px-3 py-2.5 pr-10 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent transition-all"
                  placeholder="••••••••"
                  required
                />
                <button
                  type="button"
                  onClick={() => setShow(!show)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                >
                  {show ? <EyeOff size={16} /> : <Eye size={16} />}
                </button>
              </div>
            </div>

            <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
              style={{ display: error ? '' : 'none' }}>{error}</p>

            <button
              type="submit"
              disabled={loading}
              className={cn(
                'w-full py-2.5 rounded-lg text-white text-sm font-medium transition-all flex items-center justify-center gap-2',
                loading ? 'opacity-70 cursor-not-allowed' : 'hover:opacity-90 active:scale-[0.98]'
              )}
              style={{ background: '#1B4F8C' }}
            >
              <Loader2 size={16} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              {loading ? 'Verificando...' : 'Ingresar al sistema'}
            </button>
          </form>

          <p className="text-center text-xs text-slate-400 mt-6">
            Primera vez: ingresa cualquier contraseña para activar tu cuenta
          </p>
        </div>

        <p className="text-center text-white/40 text-xs mt-4">
          © 2025 Admin Inmuebles RD
        </p>
        <div className="flex justify-center mt-1.5">
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold"
            style={{ background: 'rgba(27,79,140,0.7)', color: '#7dd3fc', border: '1px solid rgba(125,211,252,0.3)' }}>
            <span className="w-1.5 h-1.5 rounded-full bg-sky-400 inline-block" />
            v6.0
          </span>
        </div>
      </div>
    </div>
  )
}
