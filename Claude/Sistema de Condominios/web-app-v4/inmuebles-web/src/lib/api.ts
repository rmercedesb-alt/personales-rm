import axios from 'axios'
import { useAuthStore } from '../store/authStore'

const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use(cfg => {
  const token = useAuthStore.getState().token
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  return cfg
})

api.interceptors.response.use(
  r => r,
  err => {
    if (err.response?.status === 401) useAuthStore.getState().logout()
    return Promise.reject(err)
  }
)

export default api

// ─── Types ────────────────────────────────────────────────────────────────────
export interface ApiResponse<T> { success: boolean; message?: string; data: T }

export interface Propietario {
  id: string; nombre: string; cedula: string; telefono: string; email: string
  banco: string; numeroCuenta: string; tipoCuenta: string
  tipoComision: string; comisionPorcentaje: number; comisionFija: number
  estado: string; notas: string; fechaRegistro: string; tipoDueno: string
}

export interface CoPropietario {
  id: string; idPropietario: string; nombre: string; cedula: string
  telefono: string; email: string; banco: string; numeroCuenta: string
  tipoCuenta: string; pesoPorc: number; estado: string; notas: string
  fechaRegistro: string
}

export interface Inmueble {
  id: string; idPropietario: string; nombrePropietario: string
  tipo: string; descripcion: string; direccion: string; sector: string; ciudad: string
  latitud: number; longitud: number; montoAlquiler: number
  estado: string; nivelMorosidad: string; notas: string; fechaRegistro: string
}

export interface Inquilino {
  id: string; nombre: string; cedula: string; telefono: string; email: string
  ocupacion: string; referenciaPersonal: string; telefonoReferencia: string
  estado: string; notas: string; fechaRegistro: string
}

export interface Contrato {
  id: string; idInmueble: string; nombreInmueble: string
  idInquilino: string; nombreInquilino: string; idPropietario: string
  fechaInicio: string; fechaFin: string; montoAlquiler: number
  periodicidad: string; diaCobro: number; diasGracia: number
  tipoMora: string; moraPorcentaje: number; montoMoraFija: number
  deposito: number; estado: string; notas: string; fechaRegistro: string
}

export interface CxCCargo {
  id: string; idContrato: string; idInquilino: string; nombreInquilino: string
  idInmueble: string; tipoCargo: string; descripcion: string
  periodoInicio: string; periodoFin: string; fechaVencimiento: string
  montoOriginal: number; montoMora: number; montoPagado: number; saldoPendiente: number
  estado: string; diasMora: number; enGracia: boolean; notas: string; fechaRegistro: string
  fechaUltimaActualizacionMora?: string | null
}

export interface ProcesosEstado {
  ultimaActualizacionMora: string | null
  ultimaGeneracionCargos: string | null
}

export interface CxCPago {
  id: string; idCxC: string; idContrato: string; idInquilino: string
  nombreInquilino: string; idInmueble: string; idSesionCaja: string
  fechaPago: string; montoAlquiler: number; montoMora: number; totalPagado: number
  formaPago: string; referencia: string; estadoCustodia: string
  idLiquidacion?: string; notas: string; fechaRegistro: string
  idRecibo: string; anulado: boolean; motivoAnulacion: string; montoAbonoAplicado: number
}

export interface AbonoCuenta {
  id: string; idInquilino: string; nombreInquilino: string; fecha: string
  montoOriginal: number; saldoDisponible: number; estado: string
  idReciboOrigen: string; usuario: string; notas: string; fechaRegistro: string
}

export interface SesionCaja {
  id: string; fecha: string; fechaRegistro?: string; estado: string
  totalEfectivo: number; totalTransferencia: number; totalTarjeta: number
  totalCheque: number; totalGeneral: number; cantidadTransacciones: number
  usuarioApertura?: string; usuarioCierre?: string; fechaCierre?: string; notas?: string
}

export interface Liquidacion {
  id: string; idPropietario: string; nombrePropietario: string
  fechaDesde: string; fechaHasta: string
  totalCobrado: number; comision: number; netoPropietario: number
  estado: string; fechaPago?: string; referenciaPago: string
}

export interface DashboardKPIs {
  cxCDelMes: number; cobradoMes: number; enAdministracion: number
  liquidadoMes: number; pendienteCobro: number; tasaCobro: number
  inmueblesOcupados: number; inmueblesTotal: number
  contratosPorVencer: number; enMora: number; sesionHoy: number
  alertas: { tipo: string; mensaje: string; idRef?: string }[]
  montoEnMora: number; art1762Count: number; montoDeudaArt1762: number
}

export interface MoraDetalleItem {
  idCxC: string; idContrato: string
  idPropietario: string; nombrePropietario: string
  idInquilino: string; nombreInquilino: string
  descripcion: string; fechaVencimiento: string
  diasVencido: number; mesesVencidos: number
  montoDeuda: number; montoMora: number; totalAdeudado: number
}

export interface EstadoCuentaInquilino {
  inquilino: Inquilino
  cargosVigentes: CxCCargo[]
  pagosRecientes: CxCPago[]
  totalPendiente: number
  totalPagadoMes: number
}

export interface PagoEnAdminDetalle {
  id: string; idRecibo: string; fechaPago: string
  montoAlquiler: number; montoMora: number; totalPagado: number
  descripcionInmueble: string; idCxC: string; formaPago: string
}

export interface PropietarioPendiente {
  idPropietario: string; nombrePropietario: string
  totalPagos: number; totalMonto: number
  pagos: PagoEnAdminDetalle[]
}

export interface LiquidacionPreviewLinea {
  id: string; idRecibo: string; idCxC: string; idInmueble: string
  descripcionInmueble: string; fechaPago: string
  montoAlquiler: number; montoMora: number; comisionLinea: number; netoLinea: number
}

export interface EmpresaData {
  nombre: string; direccion: string; direccion2: string
  rnc: string; telefono: string; fax: string; email: string; logo: string
}

export interface LiquidacionPreview {
  propietario: Propietario
  pagos: LiquidacionPreviewLinea[]
  totalAlquiler: number; totalMora: number; totalComision: number; totalNeto: number
  comisionPorcentaje: number; comisionFija: number; tipoComision: string
  fechaDesde: string; fechaHasta: string
}
