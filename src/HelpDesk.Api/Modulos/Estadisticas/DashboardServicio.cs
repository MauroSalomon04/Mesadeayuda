namespace HelpDesk.Api.Modulos.Estadisticas;

public sealed class Conteo
{
    public string Etiqueta { get; set; } = "";
    public int Cantidad { get; set; }
}

public sealed class ConteoMes
{
    public string Mes { get; set; } = "";
    public int Cantidad { get; set; }
    public int Pendientes { get; set; }
}

public sealed class IndicadoresFijos
{
    public int Hoy { get; set; }
    public int Semana { get; set; }
    public int Mes { get; set; }
    public int Pendientes { get; set; }
    public int SinEstado { get; set; }
}

public sealed class IndicadoresPeriodo
{
    public int Total { get; set; }
    public int Resueltas { get; set; }
    public double? PromedioResolucionMin { get; set; }
    public int ResueltasConTiempo { get; set; }
    public int ResueltasAlRegistrar { get; set; }
    public double? DuracionPromedioMin { get; set; }
    public long MinutosEstimados { get; set; }
}

public sealed class DashboardDto
{
    public string Periodo { get; set; } = "todo";
    public DateTime? Desde { get; set; }
    public IndicadoresFijos Indicadores { get; set; } = new();
    public IndicadoresPeriodo EnPeriodo { get; set; } = new();
    public List<Conteo> PorTipo { get; set; } = [];
    public List<Conteo> PorResponsable { get; set; } = [];
    public List<Conteo> PorOficina { get; set; } = [];
    public List<Conteo> PorMedio { get; set; } = [];
    public List<Conteo> PorPrioridad { get; set; } = [];
    public List<Conteo> PorEstado { get; set; } = [];
    public List<ConteoMes> PorMes { get; set; } = [];
    public List<Conteo> TopProblemas { get; set; } = [];
}

/// <summary>Indicadores y gráficos del inicio.</summary>
public sealed class DashboardServicio(BaseDatos db, Reloj reloj)
{
    public static readonly string[] Periodos = ["todo", "anio", "12m", "90d", "mes", "semana"];

    public async Task<DashboardDto> ObtenerAsync(string? periodo, CancellationToken ct)
    {
        periodo = Periodos.Contains(periodo) ? periodo! : "todo";
        var hoy = reloj.Hoy();
        DateTime? desde = periodo switch
        {
            "anio" => new DateTime(hoy.Year, 1, 1),
            "12m" => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-11),
            "90d" => hoy.AddDays(-89),
            "mes" => reloj.InicioMes(),
            "semana" => reloj.InicioSemana(),
            _ => null,
        };

        var where = "WHERE s.EliminadoEn IS NULL" + (desde is null ? "" : " AND s.FechaIngreso >= @desde");
        var sql = $"""
            SELECT
                COALESCE(SUM(CASE WHEN s.FechaIngreso >= @hoy THEN 1 ELSE 0 END), 0) AS Hoy,
                COALESCE(SUM(CASE WHEN s.FechaIngreso >= @semana THEN 1 ELSE 0 END), 0) AS Semana,
                COALESCE(SUM(CASE WHEN s.FechaIngreso >= @mes THEN 1 ELSE 0 END), 0) AS Mes,
                COALESCE(SUM(CASE WHEN e.EsResuelto = FALSE THEN 1 ELSE 0 END), 0) AS Pendientes,
                COALESCE(SUM(CASE WHEN s.EstadoId IS NULL THEN 1 ELSE 0 END), 0) AS SinEstado
            FROM Solicitud s
            LEFT JOIN Estado e ON e.Id = s.EstadoId
            WHERE s.EliminadoEn IS NULL;

            SELECT
                COUNT(*) AS Total,
                COALESCE(SUM(CASE WHEN e.EsResuelto = TRUE THEN 1 ELSE 0 END), 0) AS Resueltas,
                AVG(CASE WHEN s.MinutosResolucion > 0 THEN CAST(s.MinutosResolucion AS DOUBLE PRECISION) END) AS PromedioResolucionMin,
                COALESCE(SUM(CASE WHEN s.MinutosResolucion > 0 THEN 1 ELSE 0 END), 0) AS ResueltasConTiempo,
                COALESCE(SUM(CASE WHEN s.MinutosResolucion = 0 THEN 1 ELSE 0 END), 0) AS ResueltasAlRegistrar,
                AVG(CAST(s.DuracionEstimadaMin AS DOUBLE PRECISION)) AS DuracionPromedioMin,
                COALESCE(SUM(s.DuracionEstimadaMin), 0) AS MinutosEstimados
            FROM Solicitud s
            LEFT JOIN Estado e ON e.Id = s.EstadoId
            {where};

            SELECT COALESCE(t.Codigo, '(sin tipo)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN TipoSolicitud t ON t.Id = s.TipoSolicitudId
            {where} GROUP BY t.Codigo ORDER BY COUNT(*) DESC;

            SELECT COALESCE(r.Nombre, '(sin responsable)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN Responsable r ON r.Id = s.ResponsableId
            {where} GROUP BY r.Nombre ORDER BY COUNT(*) DESC;

            SELECT COALESCE(o.Nombre, '(sin oficina)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN Oficina o ON o.Id = s.OficinaId
            {where} GROUP BY o.Nombre ORDER BY COUNT(*) DESC LIMIT 10;

            SELECT COALESCE(m.Nombre, '(sin medio)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN MedioContacto m ON m.Id = s.MedioContactoId
            {where} GROUP BY m.Nombre ORDER BY COUNT(*) DESC;

            SELECT COALESCE(p.Nombre, '(sin prioridad)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN Prioridad p ON p.Id = s.PrioridadId
            {where} GROUP BY p.Nombre, p.Nivel ORDER BY CASE WHEN p.Nivel IS NULL THEN 1 ELSE 0 END, p.Nivel;

            SELECT COALESCE(e.Nombre, '(sin estado)') AS Etiqueta, COUNT(*) AS Cantidad
            FROM Solicitud s LEFT JOIN Estado e ON e.Id = s.EstadoId
            {where} GROUP BY e.Nombre ORDER BY COUNT(*) DESC;

            SELECT to_char(s.FechaIngreso, 'YYYY-MM') AS Mes, COUNT(*) AS Cantidad,
                   COALESCE(SUM(CASE WHEN e.EsResuelto = FALSE THEN 1 ELSE 0 END), 0) AS Pendientes
            FROM Solicitud s LEFT JOIN Estado e ON e.Id = s.EstadoId
            {where} GROUP BY to_char(s.FechaIngreso, 'YYYY-MM') ORDER BY Mes;

            SELECT s.Descripcion FROM Solicitud s {where} AND s.Descripcion IS NOT NULL;
            """;

        await using var cn = await db.AbrirAsync(ct);
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            hoy,
            semana = reloj.InicioSemana(),
            mes = reloj.InicioMes(),
            desde,
        }, cancellationToken: ct));

        var dto = new DashboardDto
        {
            Periodo = periodo,
            Desde = desde,
            Indicadores = await multi.ReadSingleAsync<IndicadoresFijos>(),
            EnPeriodo = await multi.ReadSingleAsync<IndicadoresPeriodo>(),
            PorTipo = (await multi.ReadAsync<Conteo>()).ToList(),
            PorResponsable = (await multi.ReadAsync<Conteo>()).ToList(),
            PorOficina = (await multi.ReadAsync<Conteo>()).ToList(),
            PorMedio = (await multi.ReadAsync<Conteo>()).ToList(),
            PorPrioridad = (await multi.ReadAsync<Conteo>()).ToList(),
            PorEstado = (await multi.ReadAsync<Conteo>()).ToList(),
            PorMes = (await multi.ReadAsync<ConteoMes>()).ToList(),
        };
        var descripciones = (await multi.ReadAsync<string>()).ToList();
        dto.TopProblemas = AgruparProblemas(descripciones, 15);
        return dto;
    }

    /// <summary>Agrupa descripciones parecidas (sin tildes ni mayúsculas) y devuelve las más frecuentes.</summary>
    public static List<Conteo> AgruparProblemas(IEnumerable<string> descripciones, int cantidad) =>
        descripciones
            .Select(d => (Original: d.Trim(), Clave: Texto.ClaveAgrupacion(d)))
            .Where(x => x.Clave.Length > 0)
            .GroupBy(x => x.Clave)
            .Select(g => new Conteo
            {
                // Se muestra la forma de escribirlo más usada.
                Etiqueta = g.GroupBy(x => x.Original).OrderByDescending(v => v.Count()).First().Key,
                Cantidad = g.Count(),
            })
            .OrderByDescending(c => c.Cantidad)
            .ThenBy(c => c.Etiqueta)
            .Take(cantidad)
            .ToList();
}
