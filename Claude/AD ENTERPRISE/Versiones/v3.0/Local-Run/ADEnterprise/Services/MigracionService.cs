using ADEnterprise.Models;
using ClosedXML.Excel;

namespace ADEnterprise.Services;

public class ResultadoMigracion
{
    public int TotalFilas    { get; set; }
    public int Exitosas      { get; set; }
    public int Errores       { get; set; }
    public List<string>  MensajesError     { get; set; } = new();
    public List<Factura>  VistaFacturas    { get; set; } = new();
    public List<Pago>     VistaPagos       { get; set; } = new();
    public List<Compania> VistaCompanias   { get; set; } = new();
    public List<Cliente>  VistaClientes    { get; set; } = new();
    // v2.0 — Finanzas
    public List<EgresoMigracion>        VistaEgresos        { get; set; } = new();
    public List<IngresoCapitalMigracion> VistaIngresosCapital { get; set; } = new();
}

// DTOs simplificados para migración (sin SeqCuadre ni SeqMovCaja — son datos históricos)
public class EgresoMigracion
{
    public string TipoEgreso { get; set; } = "";
    public string ANombreDe  { get; set; } = "";
    public DateTime Fecha    { get; set; }
    public decimal  Monto    { get; set; }
    public string   Nota     { get; set; } = "";
}

public class IngresoCapitalMigracion
{
    public string   Nombre        { get; set; } = "";
    public DateTime Fecha         { get; set; }
    public decimal  Monto         { get; set; }
    public string   Observaciones { get; set; } = "";
}

public class MigracionService
{
    private readonly FacturaService  _facturas;
    private readonly CompaniaService _companias;
    private readonly ClienteService  _clientes;
    private readonly GoogleSheetsService _gs;

    public MigracionService(FacturaService facturas, CompaniaService companias,
                            ClienteService clientes, GoogleSheetsService gs)
    {
        _facturas  = facturas;
        _companias = companias;
        _clientes  = clientes;
        _gs        = gs;
    }

    // ─── PLANTILLA EXCEL ─────────────────────────────────────────────────────

    public byte[] GenerarPlantillaExcel()
    {
        using var wb = new XLWorkbook();

        CrearHojaFacturas(wb);
        CrearHojaPagos(wb);
        CrearHojaCompanias(wb);
        CrearHojaClientes(wb);
        CrearHojaEgresos(wb);
        CrearHojaIngresosCapital(wb);
        CrearHojaInstrucciones(wb);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void CrearHojaFacturas(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Facturas");
        string[] cols = {
            "N° Ticket", "Empresa", "Nombre Cliente", "Monto (RD$)",
            "Fecha Factura (YYYY-MM-DD)", "Estado (PAGADA/PENDIENTE/PAGADO PARCIAL)",
            "Pagado a la Fecha (RD$)", "Notas"
        };
        EstiloEncabezado(ws, cols);
        // Ejemplos
        ws.Cell(2, 1).Value = 1001; ws.Cell(2, 2).Value = "COSTA SUR DOMINICANA";
        ws.Cell(2, 3).Value = "Ramon Soriano"; ws.Cell(2, 4).Value = 25000;
        ws.Cell(2, 5).Value = "2024-03-15"; ws.Cell(2, 6).Value = "PAGADA";
        ws.Cell(2, 7).Value = 25000; ws.Cell(2, 8).Value = "";

        ws.Cell(3, 1).Value = 1002; ws.Cell(3, 2).Value = "COSTA SUR DOMINICANA";
        ws.Cell(3, 3).Value = "Maria Lopez"; ws.Cell(3, 4).Value = 50000;
        ws.Cell(3, 5).Value = "2024-06-01"; ws.Cell(3, 6).Value = "PAGADO PARCIAL";
        ws.Cell(3, 7).Value = 20000; ws.Cell(3, 8).Value = "Abonó 2 veces";

        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaPagos(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Pagos");
        string[] cols = {
            "N° Ticket", "Fecha Pago (YYYY-MM-DD)", "Monto Pagado (RD$)",
            "Forma de Pago (Efectivo/Cheque/Transferencia/Abono)",
            "N° Cheque / Referencia", "Notas"
        };
        EstiloEncabezado(ws, cols, "#145a32");
        // Ejemplo de abonos múltiples para ticket 1002
        ws.Cell(2, 1).Value = 1001; ws.Cell(2, 2).Value = "2024-03-20";
        ws.Cell(2, 3).Value = 25000; ws.Cell(2, 4).Value = "Cheque";
        ws.Cell(2, 5).Value = "00123456"; ws.Cell(2, 6).Value = "";

        ws.Cell(3, 1).Value = 1002; ws.Cell(3, 2).Value = "2024-05-10";
        ws.Cell(3, 3).Value = 10000; ws.Cell(3, 4).Value = "Efectivo";
        ws.Cell(3, 5).Value = ""; ws.Cell(3, 6).Value = "Primer abono";

        ws.Cell(4, 1).Value = 1002; ws.Cell(4, 2).Value = "2024-06-15";
        ws.Cell(4, 3).Value = 10000; ws.Cell(4, 4).Value = "Transferencia";
        ws.Cell(4, 5).Value = "TRF-789"; ws.Cell(4, 6).Value = "Segundo abono";

        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaCompanias(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Companias");
        string[] cols = { "Nombre", "RNC", "Telefono", "Direccion", "Correo", "Condicion Pago (Dias)" };
        EstiloEncabezado(ws, cols, "#154360");
        ws.Cell(2, 1).Value = "COSTA SUR DOMINICANA"; ws.Cell(2, 2).Value = "1-01-12345-6";
        ws.Cell(2, 3).Value = "(809) 555-0000"; ws.Cell(2, 4).Value = "Av. Principal 123";
        ws.Cell(2, 5).Value = "contacto@costasur.com"; ws.Cell(2, 6).Value = 30;
        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaClientes(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clientes");
        string[] cols = { "Nombre Completo", "Cedula", "Telefono", "Correo" };
        EstiloEncabezado(ws, cols, "#4a235a");
        ws.Cell(2, 1).Value = "Ramon Soriano Moreta"; ws.Cell(2, 2).Value = "001-0000001-1";
        ws.Cell(2, 3).Value = "(809) 555-1111"; ws.Cell(2, 4).Value = "ramon@correo.com";
        ws.Cell(3, 1).Value = "Maria Lopez"; ws.Cell(3, 2).Value = "001-0000002-2";
        ws.Cell(3, 3).Value = "(809) 555-2222"; ws.Cell(3, 4).Value = "";
        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaEgresos(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Egresos");
        string[] cols = {
            "Tipo Egreso (Nomina/Servicios/OtrosGastos)",
            "A Nombre De",
            "Fecha (YYYY-MM-DD)",
            "Monto (RD$)",
            "Nota"
        };
        EstiloEncabezado(ws, cols, "#7b241c");
        ws.Cell(2, 1).Value = "Nomina";     ws.Cell(2, 2).Value = "Juan Perez";
        ws.Cell(2, 3).Value = "2024-01-15"; ws.Cell(2, 4).Value = 15000; ws.Cell(2, 5).Value = "Quincena enero";
        ws.Cell(3, 1).Value = "Servicios";  ws.Cell(3, 2).Value = "CLARO RD";
        ws.Cell(3, 3).Value = "2024-01-20"; ws.Cell(3, 4).Value = 3500;  ws.Cell(3, 5).Value = "Internet";
        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaIngresosCapital(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("IngresosCapital");
        string[] cols = {
            "Nombre (Aportante)",
            "Fecha (YYYY-MM-DD)",
            "Monto (RD$)",
            "Observaciones"
        };
        EstiloEncabezado(ws, cols, "#1a5276");
        ws.Cell(2, 1).Value = "Fanny Mercedes"; ws.Cell(2, 2).Value = "2024-01-02";
        ws.Cell(2, 3).Value = 500000; ws.Cell(2, 4).Value = "Capital inicial";
        ws.Columns().AdjustToContents();
    }

    private static void CrearHojaInstrucciones(XLWorkbook wb)
    {
        var inst = wb.Worksheets.Add("Instrucciones");
        inst.Cell(1, 1).Value = "AD ALL DIGITAL ENTERPRISE — Plantilla de Importación Masiva";
        inst.Cell(1, 1).Style.Font.Bold = true;
        inst.Cell(1, 1).Style.Font.FontSize = 14;
        inst.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0a2463");

        var items = new[]
        {
            (3,  "PESTAÑA 'Facturas'"),
            (4,  "  • Incluya todas sus facturas históricas y actuales."),
            (5,  "  • Estado: PAGADA, PENDIENTE o PAGADO PARCIAL."),
            (6,  "  • 'Pagado a la Fecha': suma de todos los abonos recibidos (0 si nada pagado)."),
            (8,  "PESTAÑA 'Pagos'"),
            (9,  "  • Incluya el detalle de cada pago realizado."),
            (10, "  • Si una factura tuvo 3 abonos, ponga 3 filas con el mismo N° Ticket."),
            (11, "  • Forma de Pago: Efectivo, Cheque, Transferencia o Abono."),
            (13, "PESTAÑA 'Companias'"),
            (14, "  • Incluya las compañías clientes. Se crearán si no existen."),
            (15, "  • Condición de Pago: número de días (ej: 30, 60, 90)."),
            (17, "PESTAÑA 'Clientes'"),
            (18, "  • Incluya los clientes individuales. Se crearán si no existen."),
            (20, "PESTAÑA 'Egresos' (Datos históricos de gastos / nómina)"),
            (21, "  • Tipo Egreso: Nomina, Servicios u OtrosGastos (exactamente así)."),
            (22, "  • Fecha: formato YYYY-MM-DD."),
            (23, "  • Se importan como registros históricos sin afectar la caja actual."),
            (25, "PESTAÑA 'IngresosCapital' (Datos históricos de aportes de capital)"),
            (26, "  • Nombre: quién aportó el capital."),
            (27, "  • Fecha: formato YYYY-MM-DD."),
            (28, "  • Se importan como registros históricos sin afectar la caja actual."),
            (30, "IMPORTANTE"),
            (31, "  • NO modifique los encabezados (fila 1) de ninguna pestaña."),
            (32, "  • Puede borrar las filas de ejemplo."),
            (33, "  • Guarde el archivo y súbalo desde Mantenimiento > Importar Datos."),
        };

        foreach (var (fila, texto) in items)
        {
            inst.Cell(fila, 1).Value = texto;
            if (!texto.StartsWith(" "))
                inst.Cell(fila, 1).Style.Font.Bold = true;
        }
        inst.Column(1).Width = 80;
    }

    private static void EstiloEncabezado(IXLWorksheet ws, string[] cols, string color = "#0a2463")
    {
        for (int i = 0; i < cols.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = cols[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(color);
            cell.Style.Font.FontColor       = XLColor.White;
        }
    }

    // ─── VALIDACIÓN ──────────────────────────────────────────────────────────

    public ResultadoMigracion Validar(Stream archivoExcel, string usuarioMigracion)
    {
        var resultado = new ResultadoMigracion();
        using var wb  = new XLWorkbook(archivoExcel);

        ValidarFacturas(wb,          resultado, usuarioMigracion);
        ValidarPagos(wb,             resultado);
        ValidarCompanias(wb,         resultado);
        ValidarClientes(wb,          resultado);
        ValidarEgresos(wb,           resultado);
        ValidarIngresosCapital(wb,   resultado);

        return resultado;
    }

    private static void ValidarFacturas(XLWorkbook wb, ResultadoMigracion r, string usuario)
    {
        var ws = wb.Worksheet("Facturas");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            var errores = new List<string>();

            if (!int.TryParse(c(1), out int ticket) || ticket <= 0)
                errores.Add($"Facturas fila {fila}: N° Ticket inválido '{c(1)}'.");
            if (string.IsNullOrWhiteSpace(c(2)))
                errores.Add($"Facturas fila {fila}: Empresa obligatoria.");
            if (string.IsNullOrWhiteSpace(c(3)))
                errores.Add($"Facturas fila {fila}: Cliente obligatorio.");
            if (!decimal.TryParse(c(4), out decimal monto) || monto <= 0)
                errores.Add($"Facturas fila {fila}: Monto inválido '{c(4)}'.");
            if (!DateTime.TryParse(c(5), out DateTime fecha))
                errores.Add($"Facturas fila {fila}: Fecha inválida '{c(5)}'.");
            var estadoStr = c(6).ToUpper();
            if (!new[] { "PAGADA","PENDIENTE","PAGADO PARCIAL" }.Contains(estadoStr))
                errores.Add($"Facturas fila {fila}: Estado '{c(6)}' no válido.");
            decimal.TryParse(c(7), out decimal pagado);

            if (errores.Any()) { r.Errores++; r.MensajesError.AddRange(errores); }
            else
            {
                r.Exitosas++;
                r.VistaFacturas.Add(new Factura
                {
                    NumTicket        = ticket,
                    EmpresaNombre    = c(2),
                    ClienteNombre    = c(3),
                    Monto            = monto,
                    FechaFactura     = fecha,
                    Estado           = estadoStr switch
                    {
                        "PAGADA"         => EstadoFactura.Pagada,
                        "PAGADO PARCIAL" => EstadoFactura.PagadoParcial,
                        _                => EstadoFactura.Pendiente
                    },
                    PagadoALaFecha   = pagado,
                    Notas            = c(8),
                    Origen           = OrigenDato.Migracion,
                    FechaMigracion   = DateTime.Now,
                    UsuarioMigracion = usuario
                });
            }
            fila++;
        }
    }

    private static void ValidarPagos(XLWorkbook wb, ResultadoMigracion r)
    {
        var ws = wb.Worksheet("Pagos");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            var errores = new List<string>();

            if (!int.TryParse(c(1), out int ticket) || ticket <= 0)
                errores.Add($"Pagos fila {fila}: N° Ticket inválido '{c(1)}'.");
            if (!DateTime.TryParse(c(2), out DateTime fechaPago))
                errores.Add($"Pagos fila {fila}: Fecha Pago inválida '{c(2)}'.");
            if (!decimal.TryParse(c(3), out decimal monto) || monto <= 0)
                errores.Add($"Pagos fila {fila}: Monto inválido '{c(3)}'.");

            var formasValidas = new[] { "Efectivo","Cheque","Transferencia","Abono" };
            var forma = c(4);
            if (!formasValidas.Any(f => f.Equals(forma, StringComparison.OrdinalIgnoreCase)))
                errores.Add($"Pagos fila {fila}: Forma de pago '{forma}' no válida.");

            if (errores.Any()) { r.Errores++; r.MensajesError.AddRange(errores); }
            else
            {
                r.Exitosas++;
                r.VistaPagos.Add(new Pago
                {
                    NumTicket           = ticket,
                    FechaPago           = fechaPago,
                    MontoPagado         = monto,
                    FormaPago           = Enum.TryParse<FormaPago>(c(4), true, out var fp)
                                            ? fp : FormaPago.Efectivo,
                    NumChequeReferencia = c(5),
                    Notas               = c(6),
                    UsuarioRegistro     = "MIGRACION",
                    FechaRegistro       = DateTime.Now
                });
            }
            fila++;
        }
    }

    private static void ValidarCompanias(XLWorkbook wb, ResultadoMigracion r)
    {
        var ws = wb.Worksheet("Companias");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(c(1)))
            { r.Errores++; r.MensajesError.Add($"Companias fila {fila}: Nombre obligatorio."); }
            else
            {
                r.Exitosas++;
                int.TryParse(c(6), out int dias);
                r.VistaCompanias.Add(new Compania
                {
                    Nombre = c(1), Rnc = c(2), Telefono = c(3),
                    Direccion = c(4), Correo = c(5),
                    CondicionPagoDias = dias > 0 ? dias : 30,
                    Activo = true
                });
            }
            fila++;
        }
    }

    private static void ValidarClientes(XLWorkbook wb, ResultadoMigracion r)
    {
        var ws = wb.Worksheet("Clientes");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(c(1)))
            { r.Errores++; r.MensajesError.Add($"Clientes fila {fila}: Nombre obligatorio."); }
            else
            {
                r.Exitosas++;
                r.VistaClientes.Add(new Cliente
                {
                    NombreCompleto = c(1), Cedula = c(2),
                    Telefono = c(3), Correo = c(4), Activo = true
                });
            }
            fila++;
        }
    }

    private static void ValidarEgresos(XLWorkbook wb, ResultadoMigracion r)
    {
        var ws = wb.Worksheet("Egresos");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            var errores = new List<string>();

            var tiposValidos = new[] { "Nomina", "Servicios", "OtrosGastos" };
            if (!tiposValidos.Any(t => t.Equals(c(1), StringComparison.OrdinalIgnoreCase)))
                errores.Add($"Egresos fila {fila}: Tipo '{c(1)}' inválido. Use: Nomina, Servicios u OtrosGastos.");
            if (string.IsNullOrWhiteSpace(c(2)))
                errores.Add($"Egresos fila {fila}: 'A Nombre De' es obligatorio.");
            if (!DateTime.TryParse(c(3), out DateTime fecha))
                errores.Add($"Egresos fila {fila}: Fecha inválida '{c(3)}'.");
            if (!decimal.TryParse(c(4), out decimal monto) || monto <= 0)
                errores.Add($"Egresos fila {fila}: Monto inválido '{c(4)}'.");

            if (errores.Any()) { r.Errores++; r.MensajesError.AddRange(errores); }
            else
            {
                r.Exitosas++;
                r.VistaEgresos.Add(new EgresoMigracion
                {
                    TipoEgreso = c(1),
                    ANombreDe  = c(2),
                    Fecha      = fecha,
                    Monto      = monto,
                    Nota       = c(5),
                });
            }
            fila++;
        }
    }

    private static void ValidarIngresosCapital(XLWorkbook wb, ResultadoMigracion r)
    {
        var ws = wb.Worksheet("IngresosCapital");
        if (ws == null) return;

        int fila = 2;
        while (!ws.Row(fila).IsEmpty())
        {
            r.TotalFilas++;
            string c(int col) => ws.Cell(fila, col).GetString().Trim();
            var errores = new List<string>();

            if (string.IsNullOrWhiteSpace(c(1)))
                errores.Add($"IngresosCapital fila {fila}: Nombre es obligatorio.");
            if (!DateTime.TryParse(c(2), out DateTime fecha))
                errores.Add($"IngresosCapital fila {fila}: Fecha inválida '{c(2)}'.");
            if (!decimal.TryParse(c(3), out decimal monto) || monto <= 0)
                errores.Add($"IngresosCapital fila {fila}: Monto inválido '{c(3)}'.");

            if (errores.Any()) { r.Errores++; r.MensajesError.AddRange(errores); }
            else
            {
                r.Exitosas++;
                r.VistaIngresosCapital.Add(new IngresoCapitalMigracion
                {
                    Nombre        = c(1),
                    Fecha         = fecha,
                    Monto         = monto,
                    Observaciones = c(4),
                });
            }
            fila++;
        }
    }

    // ─── IMPORTACIÓN BATCH (una llamada por hoja, mucho más rápido) ──────────

    public async Task<ResultadoImportacion> ImportarAsync(Stream archivoExcel, string usuario)
    {
        var resultado = Validar(archivoExcel, usuario);
        var conteo    = new ResultadoImportacion();

        // ── 1. Compañías: insertar solo las nuevas en batch ──────────────────
        var companiasExistentes = await _companias.ObtenerTodasAsync();
        var filasCompanias      = new List<IList<object>>();
        // Determinar secuencia ADCOM máxima actual para asignar en batch
        int maxAdcom = companiasExistentes
            .Where(c => c.Id.StartsWith("ADCOM", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(c.Id[5..], out _))
            .Select(c => int.Parse(c.Id[5..]))
            .DefaultIfEmpty(0).Max();

        foreach (var comp in resultado.VistaCompanias)
        {
            bool yaExiste = companiasExistentes.Any(c =>
                c.Nombre.Equals(comp.Nombre, StringComparison.OrdinalIgnoreCase));
            if (!yaExiste)
            {
                maxAdcom++;
                comp.Id = $"ADCOM{maxAdcom:D3}";
                filasCompanias.Add(new object[]
                {
                    comp.Id, comp.Nombre, comp.Rnc, comp.Telefono,
                    comp.Direccion, comp.Correo, comp.CondicionPagoDias,
                    comp.Activo ? "TRUE" : "FALSE"
                });
                // Agregar al listado local para dedup dentro del mismo archivo
                companiasExistentes.Add(comp);
                conteo.Companias++;
            }
        }
        if (filasCompanias.Any())
            await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_COMPANIAS, filasCompanias);

        // ── 2. Clientes: batch ───────────────────────────────────────────────
        var clientesExistentes = await _clientes.ObtenerTodosAsync();
        var filasClientes       = new List<IList<object>>();
        // Determinar secuencia ADCTE máxima actual
        int maxAdcte = clientesExistentes
            .Where(c => c.Id.StartsWith("ADCTE", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(c.Id[5..], out _))
            .Select(c => int.Parse(c.Id[5..]))
            .DefaultIfEmpty(0).Max();

        foreach (var cli in resultado.VistaClientes)
        {
            bool yaExiste = clientesExistentes.Any(c =>
                c.NombreCompleto.Equals(cli.NombreCompleto, StringComparison.OrdinalIgnoreCase));
            if (!yaExiste)
            {
                maxAdcte++;
                cli.Id = $"ADCTE{maxAdcte:D3}";
                filasClientes.Add(new object[]
                {
                    cli.Id, cli.NombreCompleto, cli.Cedula,
                    cli.Telefono, cli.Correo, "TRUE"
                });
                clientesExistentes.Add(cli);
                conteo.Clientes++;
            }
        }
        if (filasClientes.Any())
            await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_CLIENTES, filasClientes);

        // ── 3. Facturas: batch ───────────────────────────────────────────────
        // Obtener el ticket más alto actual para continuar la secuencia
        var facturasActuales = await _facturas.ObtenerTodasAsync();
        int ticketBase  = facturasActuales.Count > 0
            ? facturasActuales.Max(f => f.NumTicket) : 0;

        // Construir todas las filas con transacción única
        var filasFacturas      = new List<IList<object>>();
        // Diccionario ticket → numTransaccion para vincular pagos después
        var mapaTicketTrans    = new Dictionary<int, string>();

        var now = DateTime.Now;
        foreach (var f in resultado.VistaFacturas)
        {
            // Si el ticket del Excel ya existe en el sistema, lo respetamos;
            // si es 0 o no viene, asignamos secuencial
            if (f.NumTicket == 0) { ticketBase++; f.NumTicket = ticketBase; }

            // Generar transacción única usando índice para evitar duplicados en batch
            var idx   = filasFacturas.Count;
            var trans = $"F{now:yyyyMMddHHmmss}{idx:D4}";
            f.NumTransaccion = trans;
            mapaTicketTrans[f.NumTicket] = trans;

            filasFacturas.Add(new object[]
            {
                trans,
                f.NumTicket,
                f.EmpresaNombre,
                f.ClienteNombre,
                f.Monto.ToString("F2"),
                f.FechaFactura.ToString("yyyy-MM-dd"),
                f.Estado.ToString(),
                f.PagadoALaFecha.ToString("F2"),
                f.Notas,
                string.Empty,           // ValorAnterior
                OrigenDato.Migracion.ToString(),
                f.FechaMigracion?.ToString("yyyy-MM-dd") ?? now.ToString("yyyy-MM-dd"),
                f.UsuarioMigracion
            });
            conteo.Facturas++;
        }

        // Una sola llamada para todas las facturas
        if (filasFacturas.Any())
        {
            await _gs.CrearHojaSiNoExisteAsync(FacturaService.HOJA_FACTURAS);
            // Agregar encabezado si la hoja está vacía
            var hojaDatos = await _gs.LeerAsync(FacturaService.HOJA_FACTURAS, "A1:A1");
            if (hojaDatos.Count == 0)
            {
                await _gs.AgregarFilaAsync(FacturaService.HOJA_FACTURAS, new object[]
                {
                    "NumTransaccion","NumTicket","Empresa","Cliente","Monto",
                    "FechaFactura","Estado","PagadoALaFecha","Notas",
                    "ValorAnterior","Origen","FechaMigracion","UsuarioMigracion"
                });
            }
            await _gs.AgregarMultiplesFilasAsync(FacturaService.HOJA_FACTURAS, filasFacturas);
        }

        // ── 4. Pagos: batch ──────────────────────────────────────────────────
        var filasPagos = new List<IList<object>>();
        int idxPago    = 0;

        foreach (var pago in resultado.VistaPagos)
        {
            // Buscar primero en las facturas recién importadas
            if (!mapaTicketTrans.TryGetValue(pago.NumTicket, out var trans))
            {
                // Si no está en el batch, buscar en las que ya existían
                var facExistente = facturasActuales
                    .FirstOrDefault(f => f.NumTicket == pago.NumTicket);
                if (facExistente == null) { conteo.PagosOmitidos++; continue; }
                trans = facExistente.NumTransaccion;
            }

            pago.NumTransaccionFactura = trans;
            pago.NumTransaccionPago    = $"P{now:yyyyMMddHHmmss}{idxPago:D4}";
            pago.FechaRegistro         = now;
            idxPago++;

            filasPagos.Add(SerializarPago(pago));
            conteo.Pagos++;
        }

        // Una sola llamada para todos los pagos
        if (filasPagos.Any())
            await _gs.AgregarMultiplesFilasAsync(GoogleSheetsService.HOJA_PAGOS, filasPagos);

        // ── 5. Egresos: batch (históricos, sin SeqCuadre/SeqMovCaja) ─────────
        var filasEgresos = new List<IList<object>>();
        int idxEgr = 0;
        foreach (var eg in resultado.VistaEgresos)
        {
            var seq = $"EGR-{eg.Fecha:yyyyMMdd}-{idxEgr:D4}";
            filasEgresos.Add(new object[]
            {
                seq,
                "",                         // SeqCuadre — vacío en migración
                eg.TipoEgreso,
                eg.ANombreDe,
                eg.Fecha.ToString("yyyy-MM-dd"),
                eg.Monto.ToString("F2"),
                eg.Nota,
                usuario,
                now.ToString("o"),          // FechaCreacion
                "",                         // SeqMovCaja — vacío en migración
            });
            idxEgr++;
            conteo.Egresos++;
        }
        if (filasEgresos.Any())
            await _gs.AgregarMultiplesFilasAsync(EgresosService.HOJA, filasEgresos);

        // ── 6. IngresosCapital: batch (históricos) ────────────────────────────
        var filasIngresos = new List<IList<object>>();
        int idxIng = 0;
        foreach (var ing in resultado.VistaIngresosCapital)
        {
            var seq = $"ING-{ing.Fecha:yyyyMMdd}-{idxIng:D4}";
            filasIngresos.Add(new object[]
            {
                seq,
                "",                         // SeqCuadre — vacío en migración
                ing.Nombre,
                ing.Fecha.ToString("yyyy-MM-dd"),
                ing.Monto.ToString("F2"),
                ing.Observaciones,
                usuario,
                now.ToString("o"),          // FechaCreacion
                "",                         // SeqMovCaja — vacío en migración
            });
            idxIng++;
            conteo.IngresosCapital++;
        }
        if (filasIngresos.Any())
            await _gs.AgregarMultiplesFilasAsync(IngresosCapitalService.HOJA, filasIngresos);

        return conteo;
    }

    private static IList<object> SerializarPago(Pago p) => new object[]
    {
        p.NumTransaccionPago, p.NumTransaccionFactura, p.NumTicket,
        p.FechaPago.ToString("yyyy-MM-dd"),
        p.MontoPagado.ToString("F2"),
        p.FormaPago.ToString(),
        p.NumChequeReferencia,
        p.Notas,
        p.UsuarioRegistro,
        p.FechaRegistro.ToString("yyyy-MM-dd HH:mm:ss")
    };
}

public class ResultadoImportacion
{
    public int Facturas       { get; set; }
    public int Pagos          { get; set; }
    public int PagosOmitidos  { get; set; }
    public int Companias      { get; set; }
    public int Clientes       { get; set; }
    public int Egresos        { get; set; }
    public int IngresosCapital { get; set; }

    public string Resumen =>
        $"Facturas: {Facturas} | Pagos: {Pagos}" +
        (PagosOmitidos > 0 ? $" ({PagosOmitidos} omitidos)" : "") +
        $" | Compañías: {Companias} | Clientes: {Clientes}" +
        (Egresos > 0        ? $" | Egresos: {Egresos}"           : "") +
        (IngresosCapital > 0 ? $" | Ing. Capital: {IngresosCapital}" : "");
}
