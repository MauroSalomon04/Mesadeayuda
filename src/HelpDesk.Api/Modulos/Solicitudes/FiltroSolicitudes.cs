namespace HelpDesk.Api.Modulos.Solicitudes;

/// <summary>
/// Lista de Ids recibida en la URL ("1,4,sin"). "sin" significa "sin valor" (NULL).
/// </summary>
public sealed record ListaIds(IReadOnlyList<int> Ids, bool IncluyeVacio)
{
    public static readonly ListaIds Vacia = new([], false);

    public bool EstaVacia => Ids.Count == 0 && !IncluyeVacio;

    public static ListaIds Parsear(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return Vacia;
        var ids = new List<int>();
        var vacio = false;
        foreach (var parte in valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (parte.Equals("sin", StringComparison.OrdinalIgnoreCase)) vacio = true;
            else if (int.TryParse(parte, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) ids.Add(id);
        }
        return new ListaIds(ids, vacio);
    }
}

/// <summary>Filtros de la tabla de solicitudes. Los usan la tabla, la exportación y los reportes.</summary>
public sealed class FiltroSolicitudes
{
    public static readonly string[] FiltrosRapidos = ["todos", "pendientes", "resueltos", "mias", "hoy", "sinestado"];

    public string? Texto { get; init; }
    public string Rapido { get; init; } = "todos";
    public ListaIds Estados { get; init; } = ListaIds.Vacia;
    public ListaIds Responsables { get; init; } = ListaIds.Vacia;
    public ListaIds Prioridades { get; init; } = ListaIds.Vacia;
    public ListaIds Tipos { get; init; } = ListaIds.Vacia;
    public ListaIds Medios { get; init; } = ListaIds.Vacia;
    public ListaIds Oficinas { get; init; } = ListaIds.Vacia;
    /// <summary>Parte del nombre de la oficina ("RAP" encuentra todas las RAP).</summary>
    public string? OficinaTexto { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public string Orden { get; init; } = "id";
    public bool Descendente { get; init; } = true;
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 100;

    public static FiltroSolicitudes DesdeQuery(IQueryCollection q)
    {
        var rapido = ((string?)q["rapido"] ?? "todos").ToLowerInvariant();
        if (!FiltrosRapidos.Contains(rapido)) rapido = "todos";

        return new FiltroSolicitudes
        {
            Texto = HelpDesk.Api.Infraestructura.Texto.Limpiar(q["q"]),
            Rapido = rapido,
            Estados = ListaIds.Parsear(q["estado"]),
            Responsables = ListaIds.Parsear(q["responsable"]),
            Prioridades = ListaIds.Parsear(q["prioridad"]),
            Tipos = ListaIds.Parsear(q["tipo"]),
            Medios = ListaIds.Parsear(q["medio"]),
            Oficinas = ListaIds.Parsear(q["oficina"]),
            OficinaTexto = HelpDesk.Api.Infraestructura.Texto.LimpiarLinea(q["ofi"]),
            Desde = ParsearFecha(q["desde"]),
            Hasta = ParsearFecha(q["hasta"]),
            Orden = ((string?)q["orden"] ?? "id").ToLowerInvariant(),
            Descendente = !string.Equals((string?)q["dir"], "asc", StringComparison.OrdinalIgnoreCase),
            Pagina = Math.Max(1, ParsearEntero(q["pagina"]) ?? 1),
            Tamano = Math.Clamp(ParsearEntero(q["tamano"]) ?? 100, 1, 1000),
        };
    }

    private static DateTime? ParsearFecha(string? valor) =>
        DateTime.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : null;

    private static int? ParsearEntero(string? valor) =>
        int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>Construye el SQL de consulta de solicitudes a partir de los filtros.</summary>
public static class ConsultaSolicitudes
{
    public const string Columnas = """
        SELECT s.Id, s.Numero, s.FechaIngreso, s.DiaSemana, s.NombreFuncionario,
               s.OficinaId, o.Nombre AS Oficina,
               s.MedioContactoId, m.Nombre AS MedioContacto,
               s.TipoSolicitudId, t.Codigo AS TipoSolicitud,
               s.Descripcion, s.Observaciones,
               s.ResponsableId, r.Nombre AS Responsable,
               s.DuracionEstimadaMin,
               s.EstadoId, e.Nombre AS Estado, e.EsResuelto AS EstadoEsResuelto, e.Color AS EstadoColor,
               s.PrioridadId, p.Nombre AS Prioridad, p.Nivel AS PrioridadNivel,
               s.FechaResolucion, s.MinutosResolucion, s.Origen, s.Version
        """;

    public const string Desde = """
        FROM Solicitud s
        LEFT JOIN Oficina o       ON o.Id = s.OficinaId
        LEFT JOIN MedioContacto m ON m.Id = s.MedioContactoId
        LEFT JOIN TipoSolicitud t ON t.Id = s.TipoSolicitudId
        LEFT JOIN Responsable r   ON r.Id = s.ResponsableId
        LEFT JOIN Estado e        ON e.Id = s.EstadoId
        LEFT JOIN Prioridad p     ON p.Id = s.PrioridadId
        """;

    private static readonly Dictionary<string, string> ColumnasOrden = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "s.Numero",
        ["fecha"] = "s.FechaIngreso",
        ["funcionario"] = "s.NombreFuncionario",
        ["oficina"] = "o.Nombre",
        ["medio"] = "m.Nombre",
        ["tipo"] = "t.Codigo",
        ["descripcion"] = "s.Descripcion",
        ["observaciones"] = "LEFT(s.Observaciones, 400)",
        ["responsable"] = "r.Nombre",
        ["duracion"] = "s.DuracionEstimadaMin",
        ["estado"] = "e.Nombre",
        ["prioridad"] = "p.Nivel",
    };

    /// <summary>Arma la cláusula WHERE y carga los parámetros.</summary>
    public static string Where(FiltroSolicitudes f, UsuarioActual usuario, DateTime hoy, DynamicParameters p)
    {
        var w = new StringBuilder("WHERE s.EliminadoEn IS NULL");

        switch (f.Rapido)
        {
            case "pendientes":
                w.Append(" AND e.EsResuelto = FALSE");
                break;
            case "resueltos":
                w.Append(" AND e.EsResuelto = TRUE");
                break;
            case "sinestado":
                w.Append(" AND s.EstadoId IS NULL");
                break;
            case "hoy":
                w.Append(" AND s.FechaIngreso >= @hoyInicio AND s.FechaIngreso < @hoyFin");
                p.Add("hoyInicio", hoy);
                p.Add("hoyFin", hoy.AddDays(1));
                break;
            case "mias":
                if (usuario.ResponsableId is int miResponsable)
                {
                    w.Append(" AND s.ResponsableId = @miResponsable");
                    p.Add("miResponsable", miResponsable);
                }
                else
                {
                    w.Append(" AND s.CreadoPorId = @miUsuario");
                    p.Add("miUsuario", usuario.Id);
                }
                break;
        }

        AgregarLista(w, p, "s.EstadoId", "fEstado", f.Estados);
        AgregarLista(w, p, "s.ResponsableId", "fResponsable", f.Responsables);
        AgregarLista(w, p, "s.PrioridadId", "fPrioridad", f.Prioridades);
        AgregarLista(w, p, "s.TipoSolicitudId", "fTipo", f.Tipos);
        AgregarLista(w, p, "s.MedioContactoId", "fMedio", f.Medios);
        AgregarLista(w, p, "s.OficinaId", "fOficina", f.Oficinas);
        if (f.OficinaTexto is { } oficinaTexto)
        {
            w.Append(" AND normalizar(o.Nombre) LIKE normalizar(@fOficinaTexto)");
            p.Add("fOficinaTexto", "%" + Infraestructura.Texto.EscaparLike(oficinaTexto) + "%");
        }

        if (f.Desde is DateTime desde)
        {
            w.Append(" AND s.FechaIngreso >= @fDesde");
            p.Add("fDesde", desde.Date);
        }
        if (f.Hasta is DateTime hasta)
        {
            w.Append(" AND s.FechaIngreso < @fHasta");
            p.Add("fHasta", hasta.Date.AddDays(1));
        }

        var texto = f.Texto;
        if (!string.IsNullOrEmpty(texto))
        {
            if (texto.StartsWith('#') && int.TryParse(texto[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idExacto))
            {
                w.Append(" AND s.Numero = @idExacto");
                p.Add("idExacto", idExacto);
            }
            else
            {
                var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8).ToList();
                for (var i = 0; i < palabras.Count; i++)
                {
                    var nombre = $"q{i}";
                    p.Add(nombre, "%" + Infraestructura.Texto.EscaparLike(palabras[i]) + "%");
                    w.Append($"""
                         AND (CAST(s.Numero AS TEXT) LIKE @{nombre}
                          OR normalizar(s.NombreFuncionario) LIKE normalizar(@{nombre})
                          OR normalizar(o.Nombre) LIKE normalizar(@{nombre})
                          OR normalizar(s.Descripcion) LIKE normalizar(@{nombre})
                          OR normalizar(s.Observaciones) LIKE normalizar(@{nombre})
                          OR normalizar(r.Nombre) LIKE normalizar(@{nombre}))
                        """);
                }
            }
        }

        return w.ToString();
    }

    /// <summary>ORDER BY seguro (solo columnas de la lista blanca).</summary>
    public static string OrderBy(FiltroSolicitudes f, DynamicParameters p)
    {
        var columna = ColumnasOrden.GetValueOrDefault(f.Orden, "s.Numero");
        var direccion = f.Descendente ? "DESC" : "ASC";
        var prefijo = "";
        // Si se busca un número, la solicitud con ese número aparece primero.
        if (f.Texto is { } texto && int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idBuscado))
        {
            prefijo = "CASE WHEN s.Numero = @idBuscado THEN 0 ELSE 1 END, ";
            p.Add("idBuscado", idBuscado);
        }
        // NULL al final en ambos sentidos.
        var nulos = columna == "s.Numero" ? "" : $"CASE WHEN {columna} IS NULL THEN 1 ELSE 0 END, ";
        // Desempate estable por la clave interna (orden de alta).
        var desempate = ", s.Id DESC";

        return $" ORDER BY {prefijo}{nulos}{columna} {direccion}{desempate}";
    }

    private static void AgregarLista(StringBuilder w, DynamicParameters p, string columna, string parametro, ListaIds lista)
    {
        if (lista.EstaVacia) return;
        var condiciones = new List<string>();
        if (lista.Ids.Count > 0)
        {
            // Con PostgreSQL, Dapper envía la lista como un arreglo: se usa "= ANY(...)" en lugar de "IN".
            condiciones.Add($"{columna} = ANY(@{parametro})");
            p.Add(parametro, lista.Ids.ToArray());
        }
        if (lista.IncluyeVacio) condiciones.Add($"{columna} IS NULL");
        w.Append(" AND (").Append(string.Join(" OR ", condiciones)).Append(')');
    }
}
