namespace ADEnterprise.Models;

// ── Movimiento individual de Caja (hoja CAJA) ────────────────────────────────
public class MovimientoCaja
{
    public string SeqMovCaja      { get; set; } = "";   // MOV-YYYYMMDD-NNNN
    public string SeqCuadre       { get; set; } = "";   // FK → CUADRE_CAJA
    public string TipoMov         { get; set; } = "";   // DEBITO / CREDITO
    public string Categoria       { get; set; } = "";   // ver CategoriaMovCaja
    public string RefDocumento    { get; set; } = "";   // NumTransaccion factura/pago/egreso
    public string Concepto        { get; set; } = "";
    public decimal Monto          { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string EmpresaNombre   { get; set; } = "";
    public string ClienteNombre   { get; set; } = "";
    public string NumTicket       { get; set; } = "";
    public string UsuarioRegistro { get; set; } = "";   // auto — no editable
    public DateTime FechaCreacion { get; set; }         // auto — no mostrar en form
}

// ── Cuadre / apertura-cierre diario (hoja CUADRE_CAJA) ──────────────────────
public class CuadreCaja
{
    public string   SeqCuadre            { get; set; } = "";  // CUADRE-YYYYMMDD-NNNN
    public string   Estado               { get; set; } = "";  // ABIERTA / CERRADA
    public DateTime FechaApertura        { get; set; }
    public DateTime? FechaCierre         { get; set; }
    public string   UsuarioApertura      { get; set; } = "";  // auto
    public string   UsuarioCierre        { get; set; } = "";  // auto
    public decimal  BalanceInicial       { get; set; }
    public decimal  BalanceFinal         { get; set; }
    public int      CantFacturas         { get; set; }
    public decimal  MontoFacturado       { get; set; }        // valor nominal total
    public decimal  MontoPagadoFacturas  { get; set; }        // neto pagado a clientes
    public int      CantCobros           { get; set; }
    public decimal  MontoCobros          { get; set; }
    public string   Notas                { get; set; } = "";
    public DateTime FechaCreacion        { get; set; }        // auto
    public int      NumFila              { get; set; }        // posicion en Sheet
}

// ── Egreso (hoja EGRESOS) ─────────────────────────────────────────────────────
public class Egreso
{
    public string   SeqEgreso       { get; set; } = "";  // EGR-YYYYMMDD-NNNN
    public string   SeqCuadre       { get; set; } = "";
    public string   TipoEgreso      { get; set; } = "";  // Nomina / Servicios / OtrosGastos
    public string   ANombreDe       { get; set; } = "";
    public DateTime Fecha           { get; set; }
    public decimal  Monto           { get; set; }
    public string   Nota            { get; set; } = "";
    public string   UsuarioRegistro { get; set; } = "";  // auto
    public DateTime FechaCreacion   { get; set; }        // auto
    public string   SeqMovCaja      { get; set; } = "";  // FK → CAJA
    public int      NumFila         { get; set; }
}

// ── Ingreso de capital (hoja INGRESOS_CAPITAL) ───────────────────────────────
public class IngresoCapital
{
    public string   Secuencia       { get; set; } = "";  // ING-YYYYMMDD-NNNN
    public string   SeqCuadre       { get; set; } = "";
    public string   Nombre          { get; set; } = "";  // fuente / nombre del aportante
    public DateTime Fecha           { get; set; }
    public decimal  Monto           { get; set; }
    public string   Observaciones   { get; set; } = "";
    public string   UsuarioRegistro { get; set; } = "";  // auto
    public DateTime FechaCreacion   { get; set; }        // auto
    public string   SeqMovCaja      { get; set; } = "";  // FK → CAJA
    public int      NumFila         { get; set; }
}

// ── Categorías de movimiento de caja ─────────────────────────────────────────
public static class CategoriaMovCaja
{
    // Pagos Factoring: DÉBITO (dinero que adelantamos al cliente) + CRÉDITO (comisión ganada)
    public const string PagoFactoring       = "PAGO_FACTORING";      // DÉBITO — adelanto al cliente
    public const string ComisionFactoring   = "COMISION_FACTORING";  // CRÉDITO — ingreso por comisión
    public const string CobroCxC            = "COBRO_CXC";           // CRÉDITO — cobro de cartera
    public const string IngresoCapital      = "INGRESO_CAPITAL";     // CRÉDITO — aporte de capital
    public const string EgresoNomina        = "EGRESO_NOMINA";       // DÉBITO
    public const string EgresoServicio      = "EGRESO_SERVICIO";     // DÉBITO
    public const string EgresoOtro          = "EGRESO_OTRO";         // DÉBITO
}
