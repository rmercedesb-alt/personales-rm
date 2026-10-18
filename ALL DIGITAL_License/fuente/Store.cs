using System.Text.Json;
using ClosedXML.Excel;

namespace MfSoftLicense;

// Base de clientes (un JSON legible en datos\clientes.json). Cada escritura deja antes una
// copia en clientes.bak.json. Se puede exportar a Excel desde la pantalla.
public class Store
{
    private readonly object _lk = new();
    private readonly string _json;
    private List<Cliente> _lista = new();
    public string DatosDir { get; }
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Store(string datosDir)
    {
        DatosDir = datosDir;
        _json = Path.Combine(datosDir, "clientes.json");
        if (File.Exists(_json))
            try { _lista = JsonSerializer.Deserialize<List<Cliente>>(File.ReadAllText(_json), Opt) ?? new(); } catch { _lista = new(); }
    }

    private void Guardar()
    {
        if (File.Exists(_json)) File.Copy(_json, Path.Combine(DatosDir, "clientes.bak.json"), true);
        var tmp = _json + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_lista, Opt));
        File.Move(tmp, _json, true);
    }

    public Cliente? Get(string id) { lock (_lk) return _lista.FirstOrDefault(c => c.Id == id); }

    public Cliente Guardar(string? id, string nombre, string contacto, string notas, string? huella, Emision em)
    {
        lock (_lk)
        {
            var c = id is null ? null : _lista.FirstOrDefault(x => x.Id == id);
            if (c is null) { c = new Cliente(); _lista.Add(c); }
            c.Nombre = nombre; c.Contacto = contacto; c.Notas = notas; c.Huella = huella;
            c.Emisiones.Add(em);
            Guardar();
            return c;
        }
    }

    public bool Eliminar(string id)
    {
        lock (_lk)
        {
            var n = _lista.RemoveAll(c => c.Id == id);
            if (n > 0) Guardar();
            return n > 0;
        }
    }

    public static (string estado, int? dias, DateOnly? expira) Estado(Cliente c)
    {
        var ult = c.Emisiones.LastOrDefault();
        if (ult is null || !DateOnly.TryParseExact(ult.Expira, "yyyy-MM-dd", out var exp)) return ("Sin licencia", null, null);
        var dias = exp.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        return (dias < 0 ? "Vencida" : dias <= 30 ? "Por vencer" : "Activa", dias, exp);
    }

    public object Dto(Cliente c)
    {
        var ult = c.Emisiones.LastOrDefault();
        var (estado, dias, exp) = Estado(c);
        return new
        {
            id = c.Id, nombre = c.Nombre, contacto = c.Contacto, notas = c.Notas, huella = c.Huella, creado = c.Creado,
            estado, diasRestantes = dias, expira = exp?.ToString("yyyy-MM-dd"),
            modulos = ult?.Modulos ?? new List<string>(),
            ultimaEmision = ult?.Fecha,
            emisiones = c.Emisiones.Select((e, i) => new { n = i, e.Fecha, e.Expira, e.Modulos }).Reverse().ToList()
        };
    }

    public List<object> Todos()
    {
        lock (_lk) return _lista.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(Dto).ToList();
    }

    public byte[] ExportarExcel()
    {
        lock (_lk)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Clientes");
            var cab = new List<string> { "Cliente", "Contacto", "Vence", "Días restantes", "Estado", "Módulos activos", "Última emisión" };
            cab.AddRange(Mod.Todos.Select(m => m.Nombre)); cab.Add("Notas"); cab.Add("Huella del servidor");
            for (int i = 0; i < cab.Count; i++) ws.Cell(1, i + 1).Value = cab[i];
            int r = 2;
            foreach (var c in _lista.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
            {
                var ult = c.Emisiones.LastOrDefault();
                var (estado, dias, exp) = Estado(c);
                ws.Cell(r, 1).Value = c.Nombre; ws.Cell(r, 2).Value = c.Contacto;
                if (exp is not null) ws.Cell(r, 3).Value = exp.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 3).Style.DateFormat.Format = "dd/MM/yyyy";
                if (dias is not null) ws.Cell(r, 4).Value = dias.Value;
                ws.Cell(r, 5).Value = estado;
                ws.Cell(r, 6).Value = ult?.Modulos.Count ?? 0;
                ws.Cell(r, 7).Value = ult?.Fecha ?? "";
                for (int m = 0; m < Mod.Todos.Length; m++) ws.Cell(r, 8 + m).Value = (ult?.Modulos.Contains(Mod.Todos[m].Clave) ?? false) ? "Sí" : "No";
                ws.Cell(r, 8 + Mod.Todos.Length).Value = c.Notas;
                ws.Cell(r, 9 + Mod.Todos.Length).Value = string.IsNullOrEmpty(c.Huella) ? "Sin atar (cualquier servidor)" : c.Huella;
                var color = estado == "Vencida" ? XLColor.LightPink : estado == "Por vencer" ? XLColor.LightYellow : estado == "Activa" ? XLColor.LightGreen : XLColor.NoColor;
                ws.Cell(r, 5).Style.Fill.BackgroundColor = color;
                r++;
            }
            var h = ws.Range(1, 1, 1, cab.Count); h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#283078"); h.Style.Font.FontColor = XLColor.White;
            ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents();

            var hs = wb.Worksheets.Add("Historial");
            string[] ch = ["Cliente", "Fecha de emisión", "Vence", "Módulos"];
            for (int i = 0; i < ch.Length; i++) hs.Cell(1, i + 1).Value = ch[i];
            int rr = 2;
            foreach (var c in _lista.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
                foreach (var e in c.Emisiones)
                {
                    hs.Cell(rr, 1).Value = c.Nombre; hs.Cell(rr, 2).Value = e.Fecha; hs.Cell(rr, 3).Value = e.Expira;
                    hs.Cell(rr, 4).Value = string.Join(", ", e.Modulos.Select(k => Mod.Todos.FirstOrDefault(m => m.Clave == k).Nombre ?? k));
                    rr++;
                }
            var hh = hs.Range(1, 1, 1, ch.Length); hh.Style.Font.Bold = true; hh.Style.Fill.BackgroundColor = XLColor.FromHtml("#283078"); hh.Style.Font.FontColor = XLColor.White;
            hs.SheetView.FreezeRows(1); hs.Columns().AdjustToContents();

            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }
    }
}
