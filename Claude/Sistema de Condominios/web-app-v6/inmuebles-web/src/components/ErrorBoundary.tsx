import { Component, type ReactNode } from 'react'
import { AlertTriangle } from 'lucide-react'

interface Props { children: ReactNode }
interface State { error: Error | null }

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  render() {
    if (this.state.error) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-slate-50 p-6">
          <div className="bg-white rounded-2xl border border-red-200 shadow-lg p-8 max-w-lg w-full">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-xl bg-red-100 flex items-center justify-center shrink-0">
                <AlertTriangle size={20} className="text-red-600" />
              </div>
              <div>
                <h1 className="font-semibold text-slate-800">Error en la aplicación</h1>
                <p className="text-xs text-slate-500 mt-0.5">Ocurrió un error inesperado en esta pantalla</p>
              </div>
            </div>
            <div className="bg-red-50 rounded-lg p-4 mb-4 font-mono text-xs text-red-700 overflow-auto max-h-48">
              {this.state.error.message}
            </div>
            <button
              onClick={() => { this.setState({ error: null }); window.location.href = '/' }}
              className="w-full py-2.5 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              Volver al inicio
            </button>
          </div>
        </div>
      )
    }
    return this.props.children
  }
}
