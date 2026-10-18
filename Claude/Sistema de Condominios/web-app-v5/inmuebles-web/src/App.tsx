import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './store/authStore'
import Layout from './components/Layout'
import ErrorBoundary from './components/ErrorBoundary'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Propietarios from './pages/Propietarios'
import Inmuebles from './pages/Inmuebles'
import Inquilinos from './pages/Inquilinos'
import Contratos from './pages/Contratos'
import CxC from './pages/CxC'
import Pagos from './pages/Pagos'
import Caja from './pages/Caja'
import MapaInmuebles from './pages/MapaInmuebles'
import Usuarios from './pages/mantenimiento/Usuarios'
import Estados from './pages/mantenimiento/Estados'
import Empresa from './pages/mantenimiento/Empresa'
import Procesos from './pages/mantenimiento/Procesos'
import Adelantos from './pages/Adelantos'
import GenerarLiquidacion  from './pages/liquidaciones/GenerarLiquidacion'
import ConsultaLiquidaciones from './pages/liquidaciones/ConsultaLiquidaciones'
import ReporteLiquidaciones  from './pages/liquidaciones/ReporteLiquidaciones'
import ReporteBeneficios   from './pages/liquidaciones/ReporteBeneficios'
import LiquidarOtrosIngresos from './pages/liquidaciones/LiquidarOtrosIngresos'
import InformesMaestras  from './pages/mantenimiento/InformesMaestras'
import InformesCobranza  from './pages/cobranza/InformesCobranza'
import ReporteCxCGeneral from './pages/cobranza/ReporteCxCGeneral'

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const token = useAuthStore(s => s.token)
  return token ? <>{children}</> : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <ErrorBoundary>
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
          <Route index element={<Dashboard />} />
          <Route path="mapa" element={<MapaInmuebles />} />

          {/* Mantenimiento */}
          <Route path="mantenimiento/propietarios" element={<Propietarios />} />
          <Route path="mantenimiento/inquilinos"   element={<Inquilinos />} />
          <Route path="mantenimiento/inmuebles"    element={<Inmuebles />} />
          <Route path="mantenimiento/usuarios"     element={<Usuarios />} />
          <Route path="mantenimiento/estados"      element={<Estados />} />
          <Route path="mantenimiento/empresa"      element={<Empresa />} />
          <Route path="mantenimiento/procesos"     element={<Procesos />} />
          <Route path="mantenimiento/informes"     element={<InformesMaestras />} />
          <Route path="adelantos"                  element={<Adelantos />} />

          {/* Redirecciones compatibles con rutas anteriores */}
          <Route path="propietarios" element={<Navigate to="/mantenimiento/propietarios" replace />} />
          <Route path="inquilinos"   element={<Navigate to="/mantenimiento/inquilinos"   replace />} />
          <Route path="inmuebles"    element={<Navigate to="/mantenimiento/inmuebles"    replace />} />

          {/* Contratos */}
          <Route path="contratos" element={<Contratos />} />

          {/* Cobranza */}
          <Route path="cxc"          element={<CxC />} />
          <Route path="pagos"        element={<Pagos />} />
          <Route path="caja"         element={<Caja />} />
          <Route path="cobranza/informes" element={<InformesCobranza />} />
          <Route path="cobranza/reporte-cxc" element={<ReporteCxCGeneral />} />

          {/* Liquidaciones */}
          <Route path="liquidaciones/generar"  element={<GenerarLiquidacion />} />
          <Route path="liquidaciones/consulta" element={<ConsultaLiquidaciones />} />
          <Route path="liquidaciones/reportes" element={<ReporteLiquidaciones />} />
          <Route path="liquidaciones/beneficios" element={<ReporteBeneficios />} />
          <Route path="liquidaciones/otros-ingresos" element={<LiquidarOtrosIngresos />} />

        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
    </ErrorBoundary>
  )
}
