namespace HelpDesk.Api.Modulos.Conocimiento;

public sealed class CasoSimilar
{
    public int Id { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string? NombreFuncionario { get; set; }
    public string? Oficina { get; set; }
    public string? Descripcion { get; set; }
    public string? Observaciones { get; set; }
    public string? Responsable { get; set; }
    public string? Estado { get; set; }
    public bool? EstadoEsResuelto { get; set; }
    public string? TipoSolicitud { get; set; }
    public double Puntaje { get; set; }
}

public sealed record ResultadoSimilares(int Total, int ConSolucion, List<CasoSimilar> Casos, List<string> Palabras);

public sealed class SugerenciaFuncionario
{
    public string Nombre { get; set; } = "";
    public string? Oficina { get; set; }
    public int Cantidad { get; set; }
    public DateTime UltimaFecha { get; set; }
}

public sealed class SugerenciaTexto
{
    public string Texto { get; set; } = "";
    public int Cantidad { get; set; }
}

/// <summary>
/// Base de conocimiento sin IA: busca solicitudes anteriores con palabras en común y las
/// ordena dando más peso a las palabras poco frecuentes (por ejemplo "etoken" pesa más que "error").
/// </summary>
public sealed class ConocimientoServicio(BaseDatos db)
{
    private const int MaximoCandidatos = 4000;
    private const double UmbralSimilitud = 0.5;

    public async Task<ResultadoSimilares> BuscarSimilaresAsync(string? texto, int? excluirId, int limite, CancellationToken ct)
    {
        var palabras = Texto.PalabrasClave(texto);
        if (palabras.Count == 0) return new ResultadoSimilares(0, 0, [], []);

        var p = new DynamicParameters();
        var condiciones = new List<string>();
        for (var i = 0; i < palabras.Count; i++)
        {
            p.Add($"k{i}", "%" + Texto.EscaparLike(palabras[i]) + "%");
            condiciones.Add($"normalizar(s.Descripcion) LIKE normalizar(@k{i}) OR normalizar(s.Observaciones) LIKE normalizar(@k{i})");
        }
        p.Add("excluir", excluirId);
        p.Add("maximo", MaximoCandidatos);

        var sql = $"""
            SELECT COUNT(*) FROM Solicitud WHERE EliminadoEn IS NULL;

            SELECT s.Id, s.FechaIngreso, s.NombreFuncionario, o.Nombre AS Oficina, s.Descripcion, s.Observaciones,
                   r.Nombre AS Responsable, e.Nombre AS Estado, e.EsResuelto AS EstadoEsResuelto, t.Codigo AS TipoSolicitud
            FROM Solicitud s
            LEFT JOIN Oficina o ON o.Id = s.OficinaId
            LEFT JOIN Responsable r ON r.Id = s.ResponsableId
            LEFT JOIN Estado e ON e.Id = s.EstadoId
            LEFT JOIN TipoSolicitud t ON t.Id = s.TipoSolicitudId
            WHERE s.EliminadoEn IS NULL
              AND (@excluir IS NULL OR s.Id <> @excluir)
              AND ({string.Join(" OR ", condiciones)})
            ORDER BY s.Id DESC
            LIMIT @maximo;
            """;

        await using var cn = await db.AbrirAsync(ct);
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        var totalSolicitudes = Math.Max(1, await multi.ReadSingleAsync<int>());
        var candidatos = (await multi.ReadAsync<CasoSimilar>()).ToList();

        // Frecuencia de cada palabra entre los candidatos (todos los que contienen alguna palabra).
        var normalizados = candidatos
            .Select(c => (Caso: c, Desc: Texto.Normalizar(c.Descripcion), Obs: Texto.Normalizar(c.Observaciones)))
            .ToList();
        var pesos = palabras.ToDictionary(
            w => w,
            w =>
            {
                var frecuencia = normalizados.Count(n => n.Desc.Contains(w, StringComparison.Ordinal) || n.Obs.Contains(w, StringComparison.Ordinal));
                return Math.Log(1.0 + (double)totalSolicitudes / Math.Max(1, frecuencia));
            });
        var pesoTotal = pesos.Values.Sum();
        var frase = string.Join(' ', palabras);

        var similares = new List<CasoSimilar>();
        foreach (var (caso, desc, obs) in normalizados)
        {
            double puntos = 0;
            foreach (var palabra in palabras)
            {
                if (desc.Contains(palabra, StringComparison.Ordinal)) puntos += pesos[palabra];
                else if (obs.Contains(palabra, StringComparison.Ordinal)) puntos += pesos[palabra] * 0.5;
            }
            var puntaje = pesoTotal > 0 ? puntos / pesoTotal : 0;
            if (palabras.Count > 1 && desc.Contains(frase, StringComparison.Ordinal)) puntaje += 0.15;
            if (puntaje < UmbralSimilitud) continue;
            caso.Puntaje = Math.Round(Math.Min(puntaje, 1.0), 3);
            similares.Add(caso);
        }

        var ordenados = similares
            .OrderByDescending(c => c.Puntaje)
            .ThenByDescending(c => c.EstadoEsResuelto == true && !string.IsNullOrWhiteSpace(c.Observaciones))
            .ThenByDescending(c => c.FechaIngreso)
            .ToList();

        var conSolucion = ordenados.Count(c => c.EstadoEsResuelto == true && !string.IsNullOrWhiteSpace(c.Observaciones));
        return new ResultadoSimilares(ordenados.Count, conSolucion, ordenados.Take(Math.Clamp(limite, 1, 100)).ToList(), palabras);
    }

    public async Task<List<SugerenciaFuncionario>> SugerirFuncionariosAsync(string? texto, CancellationToken ct)
    {
        var limpio = Texto.LimpiarLinea(texto);
        if (limpio is null || limpio.Length < 2) return [];
        await using var cn = await db.AbrirAsync(ct);
        var filas = await cn.QueryAsync<SugerenciaFuncionario>(
            """
            SELECT x.Nombre, o.Nombre AS Oficina, x.Cantidad, x.UltimaFecha
            FROM (
                -- Se agrupa sin distinguir mayúsculas (como la intercalación CI de SQL Server).
                SELECT MIN(s.NombreFuncionario) AS Nombre, CAST(COUNT(*) AS INTEGER) AS Cantidad,
                       MAX(s.FechaIngreso) AS UltimaFecha, MAX(s.Id) AS UltimoId
                FROM Solicitud s
                WHERE s.EliminadoEn IS NULL
                  AND s.NombreFuncionario IS NOT NULL
                  AND s.NombreFuncionario <> '?'
                  AND normalizar(s.NombreFuncionario) LIKE normalizar(@patron)
                GROUP BY lower(s.NombreFuncionario)
            ) x
            JOIN Solicitud ultima ON ultima.Id = x.UltimoId
            LEFT JOIN Oficina o ON o.Id = ultima.OficinaId
            ORDER BY CASE WHEN normalizar(x.Nombre) LIKE normalizar(@prefijo) THEN 0 ELSE 1 END,
                     x.Cantidad DESC, x.UltimaFecha DESC
            LIMIT 10
            """,
            new { patron = "%" + Texto.EscaparLike(limpio) + "%", prefijo = Texto.EscaparLike(limpio) + "%" });
        return filas.ToList();
    }

    public async Task<List<SugerenciaTexto>> SugerirOficinasAsync(string? texto, CancellationToken ct)
    {
        var limpio = Texto.LimpiarLinea(texto) ?? "";
        await using var cn = await db.AbrirAsync(ct);
        var filas = await cn.QueryAsync<SugerenciaTexto>(
            """
            SELECT o.Nombre AS Texto, CAST(COUNT(s.Id) AS INTEGER) AS Cantidad
            FROM Oficina o
            LEFT JOIN Solicitud s ON s.OficinaId = o.Id AND s.EliminadoEn IS NULL
            WHERE o.Activo = TRUE AND normalizar(o.Nombre) LIKE normalizar(@patron)
            GROUP BY o.Id, o.Nombre
            ORDER BY CASE WHEN normalizar(o.Nombre) LIKE normalizar(@prefijo) THEN 0 ELSE 1 END,
                     COUNT(s.Id) DESC, o.Nombre
            LIMIT 12
            """,
            new { patron = "%" + Texto.EscaparLike(limpio) + "%", prefijo = Texto.EscaparLike(limpio) + "%" });
        return filas.ToList();
    }

    public async Task<List<SugerenciaTexto>> SugerirDescripcionesAsync(string? texto, CancellationToken ct)
    {
        var limpio = Texto.LimpiarLinea(texto) ?? "";
        await using var cn = await db.AbrirAsync(ct);
        var filas = await cn.QueryAsync<SugerenciaTexto>(
            """
            SELECT x.Texto, x.Cantidad
            FROM (
                -- Se agrupa sin distinguir mayúsculas (como la intercalación CI de SQL Server).
                SELECT MIN(s.Descripcion) AS Texto, CAST(COUNT(*) AS INTEGER) AS Cantidad
                FROM Solicitud s
                WHERE s.EliminadoEn IS NULL
                  AND s.Descripcion IS NOT NULL
                  AND normalizar(s.Descripcion) LIKE normalizar(@patron)
                GROUP BY lower(s.Descripcion)
            ) x
            ORDER BY CASE WHEN normalizar(x.Texto) LIKE normalizar(@prefijo) THEN 0 ELSE 1 END,
                     x.Cantidad DESC, x.Texto
            LIMIT 12
            """,
            new { patron = "%" + Texto.EscaparLike(limpio) + "%", prefijo = Texto.EscaparLike(limpio) + "%" });
        return filas.ToList();
    }
}
