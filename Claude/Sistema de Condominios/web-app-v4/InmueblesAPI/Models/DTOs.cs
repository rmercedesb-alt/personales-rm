namespace InmueblesAPI.Models;

public record LoginRequest(string Email, string Password);
public record CrearUsuarioRequest(string Email, string Nombre, string Rol, string Activo, string Password);
public record ActualizarUsuarioRequest(string Nombre, string Rol, string Activo, string? Password);
public record LoginResponse(string Token, string Nombre, string Rol, DateTime Expiry);

public record ApiResponse<T>(bool Success, string? Message, T? Data);

public record DashboardKPIs(
    decimal CxCDelMes,
    decimal CobradoMes,
    decimal EnAdministracion,
    decimal LiquidadoMes,
    decimal PendienteCobro,
    decimal TasaCobro,
    int InmueblesOcupados,
    int InmueblesTotal,
    int ContratosPorVencer,
    int EnMora,
    decimal SesionHoy,
    List<AlertaDashboard> Alertas,
    decimal MontoEnMora,
    int Art1762Count,
    decimal MontoDeudaArt1762
);

public record AlertaDashboard(string Tipo, string Mensaje, string? IdRef);

public record MoraDetalleItem(
    string IdCxC,
    string IdContrato,
    string IdPropietario,
    string NombrePropietario,
    string IdInquilino,
    string NombreInquilino,
    string Descripcion,
    DateTime FechaVencimiento,
    int DiasVencido,
    int MesesVencidos,
    decimal MontoDeuda,
    decimal MontoMora,
    decimal TotalAdeudado
);

public record RegistrarPagoRequest(
    string IdCxC,
    decimal MontoAlquiler,
    decimal MontoMora,
    string FormaPago,
    string Referencia,
    string? Notas
);

public record AprobarCuadreRequest(string IdSesion);

public record RegistrarPagoMultipleRequest(
    string IdInquilino,
    List<string> IdsCxC,
    string FormaPago,
    string Referencia,
    string? Notas,
    decimal? MontoRecibido = null,
    List<string>? IdsAbonosAplicar = null,
    DateTime? FechaTransaccion = null,
    Dictionary<string, decimal>? DescuentosMora = null
);

public record AbrirCajaRequest(DateTime FechaTransaccion, string? Notas = null);

public record AnularPagoRequest(string MotivoAnulacion);

public record ProcesarLiquidacionRequest(
    string IdPropietario,
    DateTime FechaDesde,
    DateTime FechaHasta
);

public record CrearPropietarioRequest(
    string Nombre,
    string Cedula,
    string Telefono,
    string Email,
    string Banco,
    string NumeroCuenta,
    string TipoCuenta,
    string TipoComision,
    decimal ComisionPorcentaje,
    decimal ComisionFija,
    string Notas,
    string TipoDueno = "ÚNICO"
);

public record CrearInmuebleRequest(
    string IdPropietario,
    string Tipo,
    string Descripcion,
    string Direccion,
    string Sector,
    string Ciudad,
    decimal Latitud,
    decimal Longitud,
    decimal MontoAlquiler,
    string Notas
);

public record ActualizarPropietarioRequest(
    string Nombre, string Cedula, string Telefono, string Email,
    string Banco, string NumeroCuenta, string TipoCuenta,
    string TipoComision, decimal ComisionPorcentaje, decimal ComisionFija,
    string Estado, string Notas, string TipoDueno = "ÚNICO"
);

public record CrearCoPropietarioRequest(
    string IdPropietario,
    string Nombre,
    string Cedula,
    string Telefono,
    string Email,
    string Banco,
    string NumeroCuenta,
    string TipoCuenta,
    decimal PesoPorc,
    string Notas
);

public record ActualizarCoPropietarioRequest(
    string Nombre, string Cedula, string Telefono, string Email,
    string Banco, string NumeroCuenta, string TipoCuenta,
    decimal PesoPorc, string Estado, string Notas
);
public record ActualizarInquilinoRequest(
    string Nombre, string Cedula, string Telefono, string Email,
    string Ocupacion, string ReferenciaPersonal, string TelefonoReferencia,
    string Estado, string Notas
);
public record ActualizarInmuebleRequest(
    string IdPropietario, string Tipo, string Descripcion,
    string Direccion, string Sector, string Ciudad,
    decimal Latitud, decimal Longitud, decimal MontoAlquiler, string Notas
);

public record CrearInquilinoRequest(
    string Nombre,
    string Cedula,
    string Telefono,
    string Email,
    string Ocupacion,
    string ReferenciaPersonal,
    string TelefonoReferencia,
    string Notas
);

public record ActualizarContratoRequest(
    DateTime FechaFin,
    decimal MontoAlquiler,
    string Periodicidad,
    int DiaCobro,
    int DiasGracia,
    string TipoMora,
    decimal MoraPorcentaje,
    decimal MontoMoraFija,
    decimal Deposito,
    string Estado,
    string Notas
);

public record AjustarMoraCxCRequest(decimal MontoMora, string MotivoAjuste);

public record ProcesosEstado(
    DateTime? UltimaActualizacionMora,
    DateTime? UltimaGeneracionCargos
);

public record CrearContratoRequest(
    string IdInmueble,
    string IdInquilino,
    DateTime FechaInicio,
    DateTime FechaFin,
    decimal MontoAlquiler,
    string Periodicidad,
    int DiaCobro,
    int DiasGracia,
    string TipoMora,
    decimal MoraPorcentaje,
    decimal MontoMoraFija,
    decimal Deposito,
    string Notas
);

public record CrearCxCManualRequest(
    string IdContrato,
    string IdInquilino,
    string IdInmueble,
    string TipoCargo,
    string Descripcion,
    DateTime PeriodoInicio,
    DateTime PeriodoFin,
    DateTime FechaVencimiento,
    decimal MontoOriginal,
    string Notas
);

public record AjustarCxCRequest(
    decimal MontoOriginal,
    DateTime FechaVencimiento,
    string Descripcion,
    string Notas,
    string MotivoAjuste
);

public record CrearAdelantoRequest(
    string IdPropietario,
    DateTime Fecha,
    string Concepto,
    decimal Monto,
    string Notas
);

public record ReporteCobranzaFiltro(
    DateTime? FechaDesde,
    DateTime? FechaHasta,
    string? IdPropietario,
    string? Estado
);

public record EstadoCuentaInquilino(
    Inquilino Inquilino,
    List<CxCCargo> CargosVigentes,
    List<CxCPago> PagosRecientes,
    decimal TotalPendiente,
    decimal TotalPagadoMes
);

public record PagoEnAdminDetalle(
    string Id, string IdRecibo, DateTime FechaPago,
    decimal MontoAlquiler, decimal MontoMora, decimal TotalPagado,
    string DescripcionInmueble, string IdCxC, string FormaPago
);

public record PropietarioPendiente(
    string IdPropietario, string NombrePropietario,
    int TotalPagos, decimal TotalMonto,
    List<PagoEnAdminDetalle> Pagos
);

public record LiquidacionPreviewLinea(
    string Id,
    string IdRecibo,
    string IdCxC,
    string IdInmueble,
    string DescripcionInmueble,
    DateTime FechaPago,
    decimal MontoAlquiler,
    decimal MontoMora,
    decimal ComisionLinea,
    decimal NetoLinea
);

public record EmpresaDto(
    string Nombre,
    string Direccion,
    string Direccion2,
    string Rnc,
    string Telefono,
    string Fax,
    string Email,
    string Logo
);

public record ActualizarEmpresaRequest(
    string Nombre,
    string Direccion,
    string Direccion2,
    string Rnc,
    string Telefono,
    string Fax,
    string Email,
    string Logo
);

public record LiquidacionPreview(
    Propietario Propietario,
    List<LiquidacionPreviewLinea> Pagos,
    decimal TotalAlquiler,
    decimal TotalMora,
    decimal TotalComision,
    decimal TotalNeto,
    decimal ComisionPorcentaje,
    decimal ComisionFija,
    string TipoComision,
    DateTime FechaDesde,
    DateTime FechaHasta
);
