using HelpDesk.Api.Modulos.Solicitudes;

namespace HelpDesk.Api.Modulos.Estadisticas;

public sealed class FilaReporte
{
    public string Clave { get; set; } = "";
    public string Etiqueta { get; set; } = "";
    public int Cantidad { get; set; }
    public long Minutos { get; set; }
    public double? PromedioMin { get; set; }
    public List<int> CeldasCantidad { get; set; } = [];
    public List<long> CeldasMinutos { get; set; } = [];
}

public sealed class ReporteDto
{
    public string Dimension { get; set; } = "";
    public string DimensionEtiqueta { get; set; } = "";
    public string? Dimension2 { get; set; }
    public string? Dimension2Etiqueta { get; set; }
    public List<string> Columnas { get; set; } = [];
    public List<FilaReporte> Filas { get; set; } = [];
    public FilaReporte Totales { get; set; } = new();
}

/// <summary>Reportes agrupados por una o dos dimensiones, con los mismos filtros que la tabla.</summary>
public sealed class ReportesServicio(BaseDatos db, Reloj reloj)
{
    private sealed record Dimension(string Expresion, string Etiqueta, bool OrdenNatural);

    private sealed class FilaBruta
    {
        public string Clave1 { get; set; } = "";
        public string Clave2 { get; set; } = "";
        public int Cantidad { get; set; }
        public long Minutos { get; set; }
    }

    private const int MaximoColumnas = 40;

    private static readonly Dictionary<string, Dimension> Dimensiones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mes"] = new("to_char(s.FechaIngreso, 'YYYY-MM')", "Mes", true),
        ["dia"] = new("CAST(s.DiaSemana AS TEXT)", "Día de la semana", true),
        ["hora"] = new("to_char(s.FechaIngreso, 'HH24')", "Hora de ingreso", true),
        ["tipo"] = new("COALESCE(t.Codigo, '(sin tipo)')", "Tipo", false),
        ["responsable"] = new("COALESCE(r.Nombre, '(sin responsable)')", "Responsable", false),
        ["oficina"] = new("COALESCE(o.Nombre, '(sin oficina)')", "Oficina", false),
        ["medio"] = new("COALESCE(m.Nombre, '(sin medio)')", "Medio de contacto", false),
        ["prioridad"] = new("COALESCE(p.Nombre, '(sin prioridad)')", "Prioridad", false),
        ["estado"] = new("COALESCE(e.Nombre, '(sin estado)')", "Estado", false),
    };

    public static IEnumerable<object> DimensionesDisponibles() =>
        Dimensiones.Select(d => new { clave = d.Key, etiqueta = d.Value.Etiqueta });

    public async Task<ReporteDto> GenerarAsync(string? dimension, string? dimension2, FiltroSolicitudes filtro,
        UsuarioActual usuario, CancellationToken ct)
    {
        var claveDim = dimension is not null && Dimensiones.ContainsKey(dimension) ? dimension.ToLowerInvariant() : "mes";
        var d1 = Dimensiones[claveDim];
        var claveDim2 = dimension2 is not null && Dimensiones.ContainsKey(dimension2) && !dimension2.Equals(claveDim, StringComparison.OrdinalIgnoreCase)
            ? dimension2.ToLowerInvariant()
            : null;
        var d2 = claveDim2 is null ? null : Dimensiones[claveDim2];

        var p = new DynamicParameters();
        var where = ConsultaSolicitudes.Where(filtro, usuario, reloj.Hoy(), p);
        var expr2 = d2?.Expresion ?? "CAST('' AS TEXT)";
        var sql = $"""
            SELECT {d1.Expresion} AS Clave1, {expr2} AS Clave2, COUNT(*) AS Cantidad,
                   COALESCE(SUM(s.DuracionEstimadaMin), 0) AS Minutos
            {ConsultaSolicitudes.Desde}
            {where}
            GROUP BY {d1.Expresion}{(d2 is null ? "" : ", " + d2.Expresion)}
            """;

        await using var cn = await db.AbrirAsync(ct);
        var filas = (await cn.QueryAsync<FilaBruta>(new CommandDefinition(sql, p, cancellationToken: ct, commandTimeout: 120))).ToList();

        // Columnas (segunda dimensión)
        var columnas = new List<string>();
        if (d2 is not null)
        {
            var porColumna = filas.GroupBy(f => f.Clave2).Select(g => (Clave: g.Key, Total: g.Sum(x => x.Cantidad)));
            columnas = (d2.OrdenNatural
                    ? porColumna.OrderBy(c => c.Clave, StringComparer.Ordinal)
                    : porColumna.OrderByDescending(c => c.Total).ThenBy(c => c.Clave, StringComparer.Ordinal))
                .Take(MaximoColumnas)
                .Select(c => c.Clave)
                .ToList();
        }
        var indiceColumna = columnas.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);

        var resultado = filas
            .GroupBy(f => f.Clave1)
            .Select(g =>
            {
                var fila = new FilaReporte
                {
                    Clave = g.Key,
                    Etiqueta = Etiquetar(claveDim, g.Key),
                    Cantidad = g.Sum(x => x.Cantidad),
                    Minutos = g.Sum(x => x.Minutos),
                    CeldasCantidad = Enumerable.Repeat(0, columnas.Count).ToList(),
                    CeldasMinutos = Enumerable.Repeat(0L, columnas.Count).ToList(),
                };
                foreach (var x in g)
                {
                    if (indiceColumna.TryGetValue(x.Clave2, out var i))
                    {
                        fila.CeldasCantidad[i] += x.Cantidad;
                        fila.CeldasMinutos[i] += x.Minutos;
                    }
                }
                return fila;
            })
            .ToList();

        resultado = d1.OrdenNatural
            ? resultado.OrderBy(f => f.Clave, StringComparer.Ordinal).ToList()
            : resultado.OrderByDescending(f => f.Cantidad).ThenBy(f => f.Etiqueta, StringComparer.CurrentCulture).ToList();

        var totales = new FilaReporte
        {
            Clave = "total",
            Etiqueta = "Total",
            Cantidad = resultado.Sum(f => f.Cantidad),
            Minutos = resultado.Sum(f => f.Minutos),
            CeldasCantidad = Enumerable.Range(0, columnas.Count).Select(i => resultado.Sum(f => f.CeldasCantidad[i])).ToList(),
            CeldasMinutos = Enumerable.Range(0, columnas.Count).Select(i => resultado.Sum(f => f.CeldasMinutos[i])).ToList(),
        };
        foreach (var fila in resultado.Append(totales))
        {
            fila.PromedioMin = fila.Cantidad == 0 ? null : Math.Round((double)fila.Minutos / fila.Cantidad, 1);
        }

        return new ReporteDto
        {
            Dimension = claveDim,
            DimensionEtiqueta = d1.Etiqueta,
            Dimension2 = claveDim2,
            Dimension2Etiqueta = d2?.Etiqueta,
            Columnas = columnas.Select(c => Etiquetar(claveDim2!, c)).ToList(),
            Filas = resultado,
            Totales = totales,
        };
    }

    private static string Etiquetar(string dimension, string clave) => dimension switch
    {
        "dia" when int.TryParse(clave, out var dia) => Reloj.NombreDia(dia),
        "hora" => $"{clave}:00",
        "mes" when DateTime.TryParseExact(clave + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mes)
            => mes.ToString("MMM yyyy", new CultureInfo("es-UY")),
        _ => clave,
    };
}
