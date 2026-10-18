namespace InmueblesAPI.Models;

public class Propietario
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Cedula { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Email { get; set; } = "";
    public string Banco { get; set; } = "";
    public string NumeroCuenta { get; set; } = "";
    public string TipoCuenta { get; set; } = "";
    public string TipoComision { get; set; } = "PORCENTAJE";
    public decimal ComisionPorcentaje { get; set; }
    public decimal ComisionFija { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
    public string TipoDueno { get; set; } = "ÚNICO";
}

public class CoPropietario
{
    public string Id { get; set; } = "";
    public string IdPropietario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Cedula { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Email { get; set; } = "";
    public string Banco { get; set; } = "";
    public string NumeroCuenta { get; set; } = "";
    public string TipoCuenta { get; set; } = "";
    public decimal PesoPorc { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class Inmueble
{
    public string Id { get; set; } = "";
    public string IdPropietario { get; set; } = "";
    public string NombrePropietario { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Sector { get; set; } = "";
    public string Ciudad { get; set; } = "";
    public decimal Latitud { get; set; }
    public decimal Longitud { get; set; }
    public decimal MontoAlquiler { get; set; }
    public string Estado { get; set; } = "DISPONIBLE";
    public string NivelMorosidad { get; set; } = "VERDE";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class Inquilino
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Cedula { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Email { get; set; } = "";
    public string Ocupacion { get; set; } = "";
    public string ReferenciaPersonal { get; set; } = "";
    public string TelefonoReferencia { get; set; } = "";
    public string Estado { get; set; } = "ACTIVO";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class Contrato
{
    public string Id { get; set; } = "";
    public string IdInmueble { get; set; } = "";
    public string NombreInmueble { get; set; } = "";
    public string IdInquilino { get; set; } = "";
    public string NombreInquilino { get; set; } = "";
    public string IdPropietario { get; set; } = "";
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public decimal MontoAlquiler { get; set; }
    public string Periodicidad { get; set; } = "MENSUAL";
    public int DiaCobro { get; set; } = 1;
    public int DiasGracia { get; set; } = 5;
    public string TipoMora { get; set; } = "PORCENTAJE";
    public decimal MoraPorcentaje { get; set; }
    public decimal MontoMoraFija { get; set; }
    public decimal Deposito { get; set; }
    public string Estado { get; set; } = "VIGENTE";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class CxCCargo
{
    public string Id { get; set; } = "";
    public string IdContrato { get; set; } = "";
    public string IdInquilino { get; set; } = "";
    public string NombreInquilino { get; set; } = "";
    public string IdInmueble { get; set; } = "";
    public string TipoCargo { get; set; } = "ALQUILER";
    public string Descripcion { get; set; } = "";
    public DateTime PeriodoInicio { get; set; }
    public DateTime PeriodoFin { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public decimal MontoOriginal { get; set; }
    public decimal MontoMora { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal SaldoPendiente { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public int DiasMora { get; set; }
    public bool EnGracia { get; set; }
    public DateTime? FechaUltimoPago { get; set; }
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaUltimaActualizacionMora { get; set; }
}

public class CxCPago
{
    public string Id { get; set; } = "";
    public string IdCxC { get; set; } = "";
    public string IdContrato { get; set; } = "";
    public string IdInquilino { get; set; } = "";
    public string NombreInquilino { get; set; } = "";
    public string IdInmueble { get; set; } = "";
    public string IdSesionCaja { get; set; } = "";
    public DateTime FechaPago { get; set; }
    public decimal MontoAlquiler { get; set; }
    public decimal MontoMora { get; set; }
    public decimal TotalPagado { get; set; }
    public string FormaPago { get; set; } = "EFECTIVO";
    public string Referencia { get; set; } = "";
    public string EstadoCustodia { get; set; } = "COBRADO";
    public string? IdLiquidacion { get; set; }
    public string UrlReciboPDF { get; set; } = "";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
    public string IdRecibo { get; set; } = "";
    public bool Anulado { get; set; }
    public string MotivoAnulacion { get; set; } = "";
    public decimal MontoAbonoAplicado { get; set; }
}

public class AbonoCuenta
{
    public string Id { get; set; } = "";
    public string IdInquilino { get; set; } = "";
    public string NombreInquilino { get; set; } = "";
    public DateTime Fecha { get; set; }
    public decimal MontoOriginal { get; set; }
    public decimal SaldoDisponible { get; set; }
    public string Estado { get; set; } = "DISPONIBLE"; // DISPONIBLE | PARCIAL | AGOTADO
    public string IdReciboOrigen { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class SesionCaja
{
    public string Id { get; set; } = "";
    public DateTime Fecha { get; set; }           // Fecha de trabajo (FechaTransaccion, la pone el admin al abrir)
    public DateTime FechaRegistro { get; set; }   // Cuando se creó en el sistema (contabilización)
    public string Estado { get; set; } = "ABIERTA";
    public decimal TotalEfectivo { get; set; }
    public decimal TotalTransferencia { get; set; }
    public decimal TotalTarjeta { get; set; }
    public decimal TotalCheque { get; set; }
    public decimal TotalGeneral { get; set; }
    public int CantidadTransacciones { get; set; }
    public string? UsuarioApertura { get; set; }
    public string? UsuarioCierre { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string UrlCierrePDF { get; set; } = "";
    public string Notas { get; set; } = "";
}

public class Liquidacion
{
    public string Id { get; set; } = "";
    public string IdPropietario { get; set; } = "";
    public string NombrePropietario { get; set; } = "";
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public decimal TotalCobrado { get; set; }
    public decimal Comision { get; set; }
    public decimal NetoPropietario { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public DateTime? FechaPago { get; set; }
    public string ReferenciaPago { get; set; } = "";
    public string UrlPDF { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class LogCustodia
{
    public string Id { get; set; } = "";
    public string IdPago { get; set; } = "";
    public string IdContrato { get; set; } = "";
    public string IdInquilino { get; set; } = "";
    public string NombreInquilino { get; set; } = "";
    public string IdInmueble { get; set; } = "";
    public decimal Monto { get; set; }
    public string EstadoAnterior { get; set; } = "";
    public string EstadoNuevo { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime FechaMovimiento { get; set; }
    public string HashSHA256 { get; set; } = "";
}

public class Adelanto
{
    public string Id { get; set; } = "";
    public string IdPropietario { get; set; } = "";
    public string NombrePropietario { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Concepto { get; set; } = "";
    public decimal Monto { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public string IdLiquidacion { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Notas { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
}

public class UsuarioSistema
{
    public string Email { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Estado { get; set; } = "SI";
}

public class ConfigSistema
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
}

public class Empresa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Direccion2 { get; set; } = "";
    public string Rnc { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Fax { get; set; } = "";
    public string Email { get; set; } = "";
    public string Logo { get; set; } = "";
    public DateTime FechaActualizacion { get; set; }
}
