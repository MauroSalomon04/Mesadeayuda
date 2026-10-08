using HelpDesk.Api.Excel;
using HelpDesk.Api.Modulos.Estadisticas;
using HelpDesk.Api.Modulos.Solicitudes;
using HelpDesk.Api.Modulos.TareasExtra;

namespace HelpDesk.Api.Modulos.Exportacion;

/// <summary>Exportación a Excel y CSV respetando los filtros activos de la pantalla.</summary>
public static class ExportacionEndpoints
{
    private const int MaximoFilas = 200_000;

    public static void MapearExportacion(this RouteGroupBuilder api)
    {
        api.MapGet("/solicitudes/exportar", ExportarSolicitudes);
        api.MapGet("/tareas-extra/exportar", ExportarTareas);
        api.MapGet("/reportes/exportar", ExportarReporte).RequireAuthorization(Politicas.Admin);
    }

    private static readonly List<ColumnaXlsx> ColumnasSolicitudes =
    [
        // Mismas columnas y orden que el Excel original (se puede volver a importar).
        new("ID Solicitud", 11, FormatoColumna.Entero),
        new("Fecha y hora de ingreso", 18, FormatoColumna.FechaHora),
        new("Nombre funcionario", 26),
        new("Oficina", 30),
        new("Medio de contacto", 15),
        new("Tipo solicitud", 12),
        new("Descripción solicitud", 40),
        new("Observaciones", 60),
        new("Responsable", 14),
        new("Duración estimada (min)", 12, FormatoColumna.Entero),
        new("Estado", 20),
        new("Prioridad", 10),
        // Columnas adicionales calculadas por el sistema
        new("Día de la semana", 12),
        new("Fecha de resolución", 18, FormatoColumna.FechaHora),
        new("Tiempo de resolución (min)", 14, FormatoColumna.Entero),
    ];

    private static async Task<IResult> ExportarSolicitudes(HttpContext http, SolicitudesServicio servicio, Reloj reloj, CancellationToken ct)
    {
        var filtro = FiltroSolicitudes.DesdeQuery(http.Request.Query);
        var formato = ((string?)http.Request.Query["formato"] ?? "xlsx").ToLowerInvariant();
        var filas = await servicio.ListarParaExportarAsync(filtro, http.Usuario(), MaximoFilas, ct);
        var marca = reloj.Ahora().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);

        var valores = filas.Select(s => new object?[]
        {
            s.Numero,
            s.FechaIngreso,
            s.NombreFuncionario,
            s.Oficina,
            s.MedioContacto,
            s.TipoSolicitud,
            s.Descripcion,
            s.Observaciones,
            s.Responsable,
            s.DuracionEstimadaMin,
            s.Estado,
            s.Prioridad,
            Reloj.NombreDia(s.DiaSemana),
            s.FechaResolucion,
            s.MinutosResolucion,
        }).ToList();

        if (formato == "csv")
        {
            var csv = GenerarCsv(ColumnasSolicitudes.Select(c => c.Titulo), valores);
            return Results.File(csv, "text/csv; charset=utf-8", $"solicitudes_{marca}.csv");
        }

        var archivo = EscritorXlsx.Generar(new HojaNueva
        {
            Nombre = "Solicitudes",
            Columnas = ColumnasSolicitudes,
            Filas = valores,
        });
        return Results.File(archivo, EscritorXlsx.TipoContenido, $"solicitudes_{marca}.xlsx");
    }

    private static async Task<IResult> ExportarTareas(string? q, int? area, TareasExtraRepositorio repo, Reloj reloj, CancellationToken ct)
    {
        var tareas = await repo.ListarAsync(q, area, ct);
        var archivo = EscritorXlsx.Generar(new HojaNueva
        {
            Nombre = "Detalle tareas extra",
            Columnas =
            [
                new("Área de ASSE con quién se colabora", 28),
                new("Tarea", 50),
                new("Impacto", 60),
                new("Carga de trabajo", 35),
                new("Implicados de mesa de ayuda", 30),
            ],
            Filas = tareas.Select(t => new object?[]
            {
                t.Area,
                t.Tarea,
                t.Impacto,
                t.CargaTrabajo,
                string.Join(" y ", t.Implicados.Select(i => i.Nombre)),
            }).ToList(),
        });
        var marca = reloj.Ahora().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        return Results.File(archivo, EscritorXlsx.TipoContenido, $"tareas_extra_{marca}.xlsx");
    }

    private static async Task<IResult> ExportarReporte(HttpContext http, ReportesServicio servicio, Reloj reloj, CancellationToken ct)
    {
        var q = http.Request.Query;
        var filtro = FiltroSolicitudes.DesdeQuery(q);
        var reporte = await servicio.GenerarAsync(q["dimension"], q["dimension2"], filtro, http.Usuario(), ct);
        var metrica = ((string?)q["metrica"] ?? "cantidad").ToLowerInvariant();
        var usarMinutos = metrica == "minutos";

        var columnas = new List<ColumnaXlsx> { new(reporte.DimensionEtiqueta, 30) };
        var filas = new List<object?[]>();
        if (reporte.Columnas.Count > 0)
        {
            columnas.AddRange(reporte.Columnas.Select(c => new ColumnaXlsx(c, 12, FormatoColumna.Entero)));
            columnas.Add(new ColumnaXlsx("Total", 12, FormatoColumna.Entero));
            foreach (var fila in reporte.Filas.Append(reporte.Totales))
            {
                var celdas = new List<object?> { fila.Etiqueta };
                celdas.AddRange(usarMinutos ? fila.CeldasMinutos.Cast<object?>() : fila.CeldasCantidad.Cast<object?>());
                celdas.Add(usarMinutos ? fila.Minutos : fila.Cantidad);
                filas.Add(celdas.ToArray());
            }
        }
        else
        {
            columnas.Add(new ColumnaXlsx("Solicitudes", 13, FormatoColumna.Entero));
            columnas.Add(new ColumnaXlsx("% del total", 11, FormatoColumna.Decimal));
            columnas.Add(new ColumnaXlsx("Minutos estimados", 16, FormatoColumna.Entero));
            columnas.Add(new ColumnaXlsx("Promedio (min)", 14, FormatoColumna.Decimal));
            var total = Math.Max(1, reporte.Totales.Cantidad);
            foreach (var fila in reporte.Filas.Append(reporte.Totales))
            {
                filas.Add([fila.Etiqueta, fila.Cantidad, Math.Round(100.0 * fila.Cantidad / total, 1), fila.Minutos, fila.PromedioMin]);
            }
        }

        var archivo = EscritorXlsx.Generar(new HojaNueva
        {
            Nombre = "Reporte",
            Columnas = columnas,
            Filas = filas,
            UltimaFilaEsTotal = true,
        });
        var marca = reloj.Ahora().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        return Results.File(archivo, EscritorXlsx.TipoContenido, $"reporte_{reporte.Dimension}_{marca}.xlsx");
    }

    /// <summary>CSV con separador ";" y BOM UTF-8, para que Excel en español lo abra con tildes y columnas correctas.</summary>
    public static byte[] GenerarCsv(IEnumerable<string> encabezados, IEnumerable<object?[]> filas)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', encabezados.Select(Campo)).Append("\r\n");
        foreach (var fila in filas)
        {
            sb.AppendJoin(';', fila.Select(v => Campo(v switch
            {
                null => "",
                DateTime fecha => fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                IFormattable numero => numero.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString() ?? "",
            }))).Append("\r\n");
        }
        var preambulo = Encoding.UTF8.GetPreamble();
        var cuerpo = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. preambulo, .. cuerpo];
    }

    private static string Campo(string valor)
    {
        // Evita que Excel interprete el contenido como fórmula.
        if (valor.Length > 0 && "=+-@".Contains(valor[0]) && !double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            valor = "'" + valor;
        }
        return valor.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? "\"" + valor.Replace("\"", "\"\"") + "\""
            : valor;
    }
}
