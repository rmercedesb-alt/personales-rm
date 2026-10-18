using ADEnterprise.Models;

namespace ADEnterprise.Services;

public class EgresosService
{
    private readonly GoogleSheetsService _gs;
    private readonly CajaService         _caja;
    public const string HOJA = "EGRESOS";

    // Tipos de egreso válidos (drill-down en el formulario)
    public static readonly string[] TiposEgreso =
        { "Nomina", "Servicios", "OtrosGastos" };

    public static string LabelTipo(string tipo) => tipo switch
    {
        "Nomina"      => "Pago de Nómina",
        "Servicios"   => "Pago de Servicios",
        "OtrosGastos" => "Otros Gastos",
        _             => tipo,
    };

    public EgresosService(GoogleSheetsService gs, CajaService caja)
    {
        _gs   = gs;
        _caja = caja;
    }

    public async Task<List<Egreso>> ObtenerTodosAsync(
        DateTime? desde = null, DateTime? hasta = null, string? tipo = null)
    {
        var filas = await _gs.LeerAsync(HOJA);
        var lista = filas.Skip(1).Select((r, i) => Map(r, i + 2)).ToList();

        if (desde.HasValue) lista = lista.Where(x => x.Fecha.Date >= desde.Value.Date).ToList();
        if (hasta.HasValue) lista = lista.Where(x => x.Fecha.Date <= hasta.Value.Date).ToList();
        if (!string.IsNullOrEmpty(tipo)) lista = lista.Where(x => x.TipoEgreso == tipo).ToList();

        return lista.OrderByDescending(x => x.FechaCreacion).ToList();
    }

    /// <summary>
    /// Registra un egreso (nómina / servicio / otro). Genera automáticamente un DÉBITO en CAJA.
    /// Usuario se toma de sesión, no editable en formulario.
    /// </summary>
    public async Task<string> RegistrarAsync(Egreso eg, string usuario)
    {
        var caja = await _caja.GetCajaAbiertaAsync()
            ?? throw new InvalidOperationException("No hay caja abierta.");

        var filas = await _gs.LeerAsync(HOJA);
        var hoy   = DateTime.Today.ToString("yyyyMMdd");
        int n     = filas.Skip(1).Count(r => V(r, 0).Contains(hoy)) + 1;
        var seq   = $"EGR-{hoy}-{n:D4}";
        var ahora = DateTime.Now;

        // Categoría de caja según tipo
        var catCaja = eg.TipoEgreso switch
        {
            "Nomina"      => CategoriaMovCaja.EgresoNomina,
            "Servicios"   => CategoriaMovCaja.EgresoServicio,
            _             => CategoriaMovCaja.EgresoOtro,
        };

        // Registrar DÉBITO en CAJA — automático
        var seqMov = await _caja.RegistrarMovimientoAsync(
            seqCuadre    : caja.SeqCuadre,
            tipoMov      : "DEBITO",
            categoria    : catCaja,
            monto        : eg.Monto,
            concepto     : $"{LabelTipo(eg.TipoEgreso)} — {eg.ANombreDe}",
            refDocumento : seq,
            empresaNombre: "",
            clienteNombre: eg.ANombreDe,
            numTicket    : "",
            usuario      : usuario
        );

        var fila = new List<object>
        {
            seq,
            caja.SeqCuadre,
            eg.TipoEgreso,
            eg.ANombreDe,
            eg.Fecha.ToString("yyyy-MM-dd"),
            eg.Monto.ToString("F2"),
            eg.Nota,
            usuario,              // auto
            ahora.ToString("o"),  // FechaCreacion — auto
            seqMov,
        };

        await _gs.AgregarFilaAsync(HOJA, fila);
        return seq;
    }

    private static string V(IList<object> r, int i) =>
        i < r.Count ? r[i]?.ToString()?.Trim() ?? "" : "";

    private static decimal D(IList<object> r, int i) =>
        decimal.TryParse(V(r, i), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private Egreso Map(IList<object> r, int fila) => new()
    {
        SeqEgreso       = V(r, 0),
        SeqCuadre       = V(r, 1),
        TipoEgreso      = V(r, 2),
        ANombreDe       = V(r, 3),
        Fecha           = DateTime.TryParse(V(r, 4), out var d) ? d : DateTime.MinValue,
        Monto           = D(r, 5),
        Nota            = V(r, 6),
        UsuarioRegistro = V(r, 7),
        FechaCreacion   = DateTime.TryParse(V(r, 8), out var fc) ? fc : DateTime.MinValue,
        SeqMovCaja      = V(r, 9),
        NumFila         = fila,
    };
}
