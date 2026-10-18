namespace ADEnterprise.Models;

public class Factura
{
    public string NumTransaccion { get; set; } = string.Empty;   // Único, nunca cambia
    public int NumTicket { get; set; }
    public string EmpresaNombre { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTime FechaFactura { get; set; }
    public EstadoFactura Estado { get; set; } = EstadoFactura.Pendiente;
    public string Notas { get; set; } = string.Empty;

    // Auditoría de creación/migración
    public string    ValorAnterior      { get; set; } = string.Empty;    // JSON snapshot antes de última edición
    public OrigenDato Origen            { get; set; } = OrigenDato.Sistema;
    public DateTime? FechaMigracion     { get; set; }
    public string    UsuarioMigracion   { get; set; } = string.Empty;

    // Auditoría de modificaciones (columnas 13 y 14 en el sheet)
    public DateTime? FechaModificacion   { get; set; }
    public string    UsuarioModificacion { get; set; } = string.Empty;

    // Referencias normalizadas a Compañía y Cliente (columnas 15 y 16)
    public string IdCompania { get; set; } = string.Empty;
    public string IdCliente  { get; set; } = string.Empty;

    // v2.0 — Factoring
    /// <summary>Monto neto que se paga al cliente (valor factura − comisión). NO aparece en CxC ni reportes de facturas.</summary>
    public decimal MontoPagado  { get; set; }
    /// <summary>Comisión = Monto - MontoPagado</summary>
    public decimal Comision     => Monto - MontoPagado;
    /// <summary>SeqCuadre de la caja del día en que se creó</summary>
    public string  SeqCuadre    { get; set; } = string.Empty;
    /// <summary>Timestamp de grabación — auto, no editable</summary>
    public DateTime? FechaCreacion { get; set; }

    // PagadoALaFecha: se guarda en la hoja y se actualiza con cada pago
    public decimal PagadoALaFecha { get; set; }
    public decimal SaldoPendiente => Monto - PagadoALaFecha;
    public List<Pago> Pagos { get; set; } = new();

    public bool TieneSaldo => SaldoPendiente > 0;

    public bool EstaVencida(int diasCondicion)
    {
        if (Estado == EstadoFactura.Pagada) return false;
        return (DateTime.Today - FechaFactura).Days > diasCondicion;
    }
}
