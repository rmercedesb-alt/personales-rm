namespace ADEnterprise.Models;

public class Pago
{
    public string NumTransaccionPago { get; set; } = string.Empty;   // col 0 — Único
    public string NumTransaccionFactura { get; set; } = string.Empty; // col 1
    public int    NumTicket { get; set; }                             // col 2
    public DateTime FechaPago { get; set; }                           // col 3
    public decimal  MontoPagado { get; set; }                         // col 4
    public FormaPago FormaPago { get; set; }                          // col 5
    public string NumChequeReferencia { get; set; } = string.Empty;  // col 6
    public string Notas { get; set; } = string.Empty;                // col 7
    public string   UsuarioRegistro { get; set; } = string.Empty;    // col 8
    public DateTime FechaRegistro   { get; set; } = DateTime.Now;    // col 9

    // Auditoría de modificaciones — cols 10-11
    public DateTime? FechaModificacion   { get; set; }
    public string    UsuarioModificacion { get; set; } = string.Empty;

    // Referencias normalizadas a Compañía y Cliente — cols 12-13
    public string IdCompania { get; set; } = string.Empty;
    public string IdCliente  { get; set; } = string.Empty;

    // ── v1.1: Recibo multi-ticket ────────────────────────────────────────────
    // Número de recibo que agrupa todos los pagos de un instrumento — col 14
    // Formato: REC-YYYYMMDD-NNNN
    public string  NumRecibo        { get; set; } = string.Empty;

    // Monto total del instrumento (cheque, transferencia, efectivo) — col 15
    // Mismo valor en todas las líneas del mismo recibo
    public decimal MontoInstrumento { get; set; }

    // Nombres denormalizados para display sin join — cols 16-17
    public string EmpresaNombre { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
}
