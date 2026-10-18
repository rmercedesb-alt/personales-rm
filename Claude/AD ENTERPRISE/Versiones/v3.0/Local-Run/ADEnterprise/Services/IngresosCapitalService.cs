using ADEnterprise.Models;

namespace ADEnterprise.Services;

public class IngresosCapitalService
{
    private readonly GoogleSheetsService _gs;
    private readonly CajaService         _caja;
    public const string HOJA = "INGRESOS_CAPITAL";

    public IngresosCapitalService(GoogleSheetsService gs, CajaService caja)
    {
        _gs   = gs;
        _caja = caja;
    }

    public async Task<List<IngresoCapital>> ObtenerTodosAsync(
        DateTime? desde = null, DateTime? hasta = null)
    {
        var filas = await _gs.LeerAsync(HOJA);
        var lista = filas.Skip(1).Select((r, i) => Map(r, i + 2)).ToList();

        if (desde.HasValue) lista = lista.Where(x => x.Fecha.Date >= desde.Value.Date).ToList();
        if (hasta.HasValue) lista = lista.Where(x => x.Fecha.Date <= hasta.Value.Date).ToList();

        return lista.OrderByDescending(x => x.FechaCreacion).ToList();
    }

    /// <summary>
    /// Registra un ingreso de capital. Genera automáticamente un CRÉDITO en CAJA.
    /// El usuario se toma del parámetro (session) y no es editable en el formulario.
    /// </summary>
    public async Task<string> RegistrarAsync(IngresoCapital ing, string usuario)
    {
        // 1. Obtener caja abierta (ya fue validada en la página)
        var caja = await _caja.GetCajaAbiertaAsync()
            ?? throw new InvalidOperationException("No hay caja abierta.");

        // 2. Generar secuencia
        var filas = await _gs.LeerAsync(HOJA);
        var hoy   = DateTime.Today.ToString("yyyyMMdd");
        int n     = filas.Skip(1).Count(r => V(r, 0).Contains(hoy)) + 1;
        var seq   = $"ING-{hoy}-{n:D4}";

        var ahora = DateTime.Now;

        // 3. Registrar movimiento en CAJA — CRÉDITO automático
        var seqMov = await _caja.RegistrarMovimientoAsync(
            seqCuadre    : caja.SeqCuadre,
            tipoMov      : "CREDITO",
            categoria    : CategoriaMovCaja.IngresoCapital,
            monto        : ing.Monto,
            concepto     : $"Ingreso de capital — {ing.Nombre}",
            refDocumento : seq,
            empresaNombre: "",
            clienteNombre: ing.Nombre,
            numTicket    : "",
            usuario      : usuario
        );

        // 4. Guardar en hoja INGRESOS_CAPITAL
        var fila = new List<object>
        {
            seq,
            caja.SeqCuadre,
            ing.Nombre,
            ing.Fecha.ToString("yyyy-MM-dd"),
            ing.Monto.ToString("F2"),
            ing.Observaciones,
            usuario,              // auto — no editable
            ahora.ToString("o"),  // FechaCreacion — auto
            seqMov,               // FK → CAJA
        };

        await _gs.AgregarFilaAsync(HOJA, fila);
        return seq;
    }

    private static string V(IList<object> r, int i) =>
        i < r.Count ? r[i]?.ToString()?.Trim() ?? "" : "";

    private static decimal D(IList<object> r, int i) =>
        decimal.TryParse(V(r, i), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private IngresoCapital Map(IList<object> r, int fila) => new()
    {
        Secuencia       = V(r, 0),
        SeqCuadre       = V(r, 1),
        Nombre          = V(r, 2),
        Fecha           = DateTime.TryParse(V(r, 3), out var d) ? d : DateTime.MinValue,
        Monto           = D(r, 4),
        Observaciones   = V(r, 5),
        UsuarioRegistro = V(r, 6),
        FechaCreacion   = DateTime.TryParse(V(r, 7), out var fc) ? fc : DateTime.MinValue,
        SeqMovCaja      = V(r, 8),
        NumFila         = fila,
    };
}
