const ESTADOS = [
  {
    grupo: 'Cuentas por Cobrar (CxC)',
    color: '#1B4F8C',
    items: [
      { estado: 'PENDIENTE',      desc: 'Cargo generado, aún dentro del plazo de pago.',          badge: 'bg-blue-100 text-blue-800' },
      { estado: 'EN_MORA',        desc: 'Venció el plazo de gracia, se aplica recargo de mora.',   badge: 'bg-red-100 text-red-800' },
      { estado: 'PAGADO',         desc: 'Saldo cancelado en su totalidad.',                        badge: 'bg-green-100 text-green-800' },
      { estado: 'PAGADO_PARCIAL', desc: 'Se realizó un abono pero queda saldo pendiente.',         badge: 'bg-yellow-100 text-yellow-800' },
    ],
  },
  {
    grupo: 'Pagos — Estado de Custodia',
    color: '#4A148C',
    items: [
      { estado: 'COBRADO',           desc: 'Pago registrado en caja, sesión aún no cuadrada.',       badge: 'bg-purple-100 text-purple-800' },
      { estado: 'EN_ADMINISTRACION', desc: 'Sesión de caja cuadrada y aprobada, pendiente de liquidar al propietario.', badge: 'bg-indigo-100 text-indigo-800' },
      { estado: 'LIQUIDADO',         desc: 'Fondos transferidos al propietario. Registro inmutable.', badge: 'bg-green-100 text-green-800' },
    ],
  },
  {
    grupo: 'Propietarios',
    color: '#1B5E20',
    items: [
      { estado: 'ACTIVO',   desc: 'Propietario vigente con inmuebles bajo administración.', badge: 'bg-green-100 text-green-800' },
      { estado: 'INACTIVO', desc: 'Propietario sin contrato activo o dado de baja.',        badge: 'bg-slate-100 text-slate-600' },
    ],
  },
  {
    grupo: 'Inquilinos',
    color: '#E65100',
    items: [
      { estado: 'ACTIVO',   desc: 'Inquilino con contrato vigente.',                badge: 'bg-green-100 text-green-800' },
      { estado: 'INACTIVO', desc: 'Inquilino sin contrato activo o desocupado.', badge: 'bg-slate-100 text-slate-600' },
    ],
  },
  {
    grupo: 'Inmuebles',
    color: '#006064',
    items: [
      { estado: 'DISPONIBLE',      desc: 'Inmueble libre, sin contrato vigente. Disponible para alquilar.', badge: 'bg-blue-100 text-blue-800' },
      { estado: 'OCUPADO',         desc: 'Inmueble con contrato vigente y actualmente habitado.',            badge: 'bg-green-100 text-green-800' },
      { estado: 'EN_MANTENIMIENTO', desc: 'Inmueble fuera de servicio temporalmente por reparaciones.',      badge: 'bg-yellow-100 text-yellow-800' },
      { estado: 'INACTIVO',        desc: 'Inmueble retirado de la administración.',                          badge: 'bg-slate-100 text-slate-600' },
    ],
  },
  {
    grupo: 'Contratos',
    color: '#37474F',
    items: [
      { estado: 'VIGENTE',   desc: 'Contrato activo dentro de su período acordado.',              badge: 'bg-green-100 text-green-800' },
      { estado: 'VENCIDO',   desc: 'Contrato que llegó a su fecha de fin sin renovación.',        badge: 'bg-orange-100 text-orange-800' },
      { estado: 'CANCELADO', desc: 'Contrato terminado antes de su fecha de vencimiento.',        badge: 'bg-red-100 text-red-800' },
    ],
  },
  {
    grupo: 'Nivel de Morosidad (semáforo del mapa)',
    color: '#1B4F8C',
    items: [
      { estado: 'VERDE',    desc: 'Al día — 0 meses de atraso.',              badge: 'bg-green-500 text-white' },
      { estado: 'AMARILLO', desc: '1 mes de atraso.',                          badge: 'bg-yellow-400 text-yellow-900' },
      { estado: 'NARANJA',  desc: '2 meses de atraso. ⚠ Alerta Art. 1762.',   badge: 'bg-orange-400 text-white' },
      { estado: 'ROJO',     desc: '3 o más meses de atraso. Acción inmediata.', badge: 'bg-red-500 text-white' },
      { estado: 'GRIS',     desc: 'Sin contrato activo / vacío.',               badge: 'bg-slate-400 text-white' },
    ],
  },
]

const ROLES = [
  { rol: 'ADMINISTRADOR', desc: 'Acceso total: configuración, usuarios, liquidaciones, reportes y todas las operaciones.', badge: 'bg-red-100 text-red-800' },
  { rol: 'SUPERVISOR',    desc: 'Todo excepto gestión de usuarios y configuración del sistema.', badge: 'bg-orange-100 text-orange-800' },
  { rol: 'ASISTENTE',     desc: 'Puede registrar cobros, ver CxC, pagos y reportes. No puede liquidar ni administrar usuarios.', badge: 'bg-blue-100 text-blue-800' },
  { rol: 'COBRADOR',      desc: 'Solo puede registrar pagos en la sesión de caja. Acceso mínimo.', badge: 'bg-purple-100 text-purple-800' },
  { rol: 'LECTURA',       desc: 'Solo puede consultar información. No puede crear, editar ni eliminar nada.', badge: 'bg-slate-100 text-slate-600' },
]

export default function Estados() {
  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-800">Estados del Sistema</h1>
        <p className="text-slate-500 mt-1 text-sm">Referencia de todos los estados válidos por entidad y roles de acceso.</p>
      </div>

      {/* Estados por entidad */}
      {ESTADOS.map(grupo => (
        <div key={grupo.grupo} className="bg-white rounded-xl border border-slate-200 overflow-hidden">
          <div className="px-5 py-3 border-b border-slate-100" style={{ borderLeftWidth: 4, borderLeftColor: grupo.color }}>
            <h2 className="font-semibold text-slate-700 text-sm">{grupo.grupo}</h2>
          </div>
          <div className="divide-y divide-slate-50">
            {grupo.items.map(item => (
              <div key={item.estado} className="flex items-center gap-4 px-5 py-3">
                <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap ${item.badge}`}>
                  {item.estado}
                </span>
                <span className="text-sm text-slate-600">{item.desc}</span>
              </div>
            ))}
          </div>
        </div>
      ))}

      {/* Roles */}
      <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
        <div className="px-5 py-3 border-b border-slate-100" style={{ borderLeftWidth: 4, borderLeftColor: '#1B4F8C' }}>
          <h2 className="font-semibold text-slate-700 text-sm">Roles de Usuario</h2>
        </div>
        <div className="divide-y divide-slate-50">
          {ROLES.map(item => (
            <div key={item.rol} className="flex items-center gap-4 px-5 py-3">
              <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap ${item.badge}`}>
                {item.rol}
              </span>
              <span className="text-sm text-slate-600">{item.desc}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
