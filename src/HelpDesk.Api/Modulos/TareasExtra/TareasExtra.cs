using HelpDesk.Api.Modulos.TiempoReal;

namespace HelpDesk.Api.Modulos.TareasExtra;

public sealed class TareaExtraFila
{
    public int Id { get; set; }
    public int? AreaAsseId { get; set; }
    public string? Area { get; set; }
    public string Tarea { get; set; } = "";
    public string? Impacto { get; set; }
    public string? CargaTrabajo { get; set; }
    public string Origen { get; set; } = "APP";
    public DateTime CreadoEn { get; set; }
    public string? CreadoPor { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public string? ActualizadoPor { get; set; }
    public List<ImplicadoItem> Implicados { get; set; } = [];
}

public sealed class ImplicadoItem
{
    public int TareaExtraId { get; set; }
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
}

public sealed class TareaExtraEntrada
{
    public int? AreaAsseId { get; set; }
    public string? Tarea { get; set; }
    public string? Impacto { get; set; }
    public string? CargaTrabajo { get; set; }
    public List<int>? ImplicadosIds { get; set; }
}

/// <summary>Tareas extra: colaboraciones de la mesa de ayuda con otras áreas de ASSE.</summary>
public sealed class TareasExtraRepositorio(BaseDatos db, Reloj reloj, Notificador notificador)
{
    private const string Select = """
        SELECT te.Id, te.AreaAsseId, a.Nombre AS Area, te.Tarea, te.Impacto, te.CargaTrabajo, te.Origen,
               te.CreadoEn, uc.NombreCompleto AS CreadoPor, te.ActualizadoEn, ua.NombreCompleto AS ActualizadoPor
        FROM TareaExtra te
        LEFT JOIN AreaAsse a ON a.Id = te.AreaAsseId
        LEFT JOIN Usuario uc ON uc.Id = te.CreadoPorId
        LEFT JOIN Usuario ua ON ua.Id = te.ActualizadoPorId
        """;

    public async Task<List<TareaExtraFila>> ListarAsync(string? texto, int? areaId, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = new StringBuilder(" WHERE te.EliminadoEn IS NULL");
        if (areaId is int area)
        {
            where.Append(" AND te.AreaAsseId = @area");
            p.Add("area", area);
        }
        var limpio = Texto.LimpiarLinea(texto);
        if (limpio is not null)
        {
            where.Append("""
                 AND (normalizar(te.Tarea) LIKE normalizar(@texto)
                  OR normalizar(te.Impacto) LIKE normalizar(@texto)
                  OR normalizar(te.CargaTrabajo) LIKE normalizar(@texto)
                  OR normalizar(a.Nombre) LIKE normalizar(@texto))
                """);
            p.Add("texto", "%" + Texto.EscaparLike(limpio) + "%");
        }

        await using var cn = await db.AbrirAsync(ct);
        var tareas = (await cn.QueryAsync<TareaExtraFila>(Select + where + " ORDER BY te.Id DESC", p)).ToList();
        await CargarImplicadosAsync(cn, null, tareas);
        return tareas;
    }

    public async Task<TareaExtraFila?> ObtenerAsync(int id, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        var tarea = await cn.QuerySingleOrDefaultAsync<TareaExtraFila>(Select + " WHERE te.Id = @id AND te.EliminadoEn IS NULL", new { id });
        if (tarea is not null) await CargarImplicadosAsync(cn, null, [tarea]);
        return tarea;
    }

    public async Task<TareaExtraFila> GuardarAsync(int? id, TareaExtraEntrada entrada, UsuarioActual usuario, CancellationToken ct)
    {
        var tarea = Texto.Limpiar(entrada.Tarea) ?? throw ErrorApi.Validacion("Describí la tarea.");
        if (tarea.Length > 500) throw ErrorApi.Validacion("La tarea no puede superar 500 caracteres.");
        var impacto = Texto.Limpiar(entrada.Impacto);
        var carga = Texto.Recortar(Texto.Limpiar(entrada.CargaTrabajo), 1000);
        var implicados = (entrada.ImplicadosIds ?? new List<int>()).Distinct().ToList();
        var ahora = reloj.Ahora();

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();

        if (entrada.AreaAsseId is int areaId &&
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AreaAsse WHERE Id = @areaId", new { areaId }, tx) == 0)
        {
            throw ErrorApi.Validacion("El área indicada no existe.");
        }
        if (implicados.Count > 0)
        {
            var existentes = await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Responsable WHERE Id = ANY(@implicados)", new { implicados = implicados.ToArray() }, tx);
            if (existentes != implicados.Count) throw ErrorApi.Validacion("Alguno de los implicados no existe.");
        }

        int idFinal;
        if (id is int existente)
        {
            var filas = await cn.ExecuteAsync(
                """
                UPDATE TareaExtra
                SET AreaAsseId = @area, Tarea = @tarea, Impacto = @impacto, CargaTrabajo = @carga,
                    ActualizadoEn = @ahora, ActualizadoPorId = @usuarioId
                WHERE Id = @id AND EliminadoEn IS NULL
                """,
                new { id = existente, area = entrada.AreaAsseId, tarea, impacto, carga, ahora, usuarioId = usuario.Id }, tx);
            if (filas == 0) throw ErrorApi.NoEncontrado("La tarea no existe.");
            await cn.ExecuteAsync("DELETE FROM TareaExtraImplicado WHERE TareaExtraId = @id", new { id = existente }, tx);
            idFinal = existente;
        }
        else
        {
            idFinal = await cn.ExecuteScalarAsync<int>(
                """
                INSERT INTO TareaExtra (AreaAsseId, Tarea, Impacto, CargaTrabajo, Origen, CreadoPorId, CreadoEn, ActualizadoPorId, ActualizadoEn)
                VALUES (@area, @tarea, @impacto, @carga, 'APP', @usuarioId, @ahora, @usuarioId, @ahora)
                RETURNING Id
                """,
                new { area = entrada.AreaAsseId, tarea, impacto, carga, ahora, usuarioId = usuario.Id }, tx);
        }

        if (implicados.Count > 0)
        {
            await cn.ExecuteAsync(
                "INSERT INTO TareaExtraImplicado (TareaExtraId, ResponsableId) VALUES (@TareaExtraId, @ResponsableId)",
                implicados.Select(r => new { TareaExtraId = idFinal, ResponsableId = r }).ToList(), tx);
        }

        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, "TareaExtra", idFinal.ToString(CultureInfo.InvariantCulture),
            id is null ? "CREADO" : "MODIFICADO", new { entrada.AreaAsseId, tarea, impacto, carga, implicados });
        await notificador.PublicarAsync(cn, tx, [TiposCambio.Tareas], id is null ? "creada" : "modificada", idFinal);
        tx.Commit();

        return await ObtenerAsync(idFinal, ct) ?? throw ErrorApi.NoEncontrado();
    }

    public async Task EliminarAsync(int id, UsuarioActual usuario, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var ahora = reloj.Ahora();
        var filas = await cn.ExecuteAsync(
            "UPDATE TareaExtra SET EliminadoEn = @ahora, EliminadoPorId = @usuarioId WHERE Id = @id AND EliminadoEn IS NULL",
            new { id, ahora, usuarioId = usuario.Id }, tx);
        if (filas == 0) throw ErrorApi.NoEncontrado("La tarea no existe.");
        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, "TareaExtra", id.ToString(CultureInfo.InvariantCulture), "ELIMINADO");
        await notificador.PublicarAsync(cn, tx, [TiposCambio.Tareas], "eliminada", id);
        tx.Commit();
    }

    private static async Task CargarImplicadosAsync(IDbConnection cn, IDbTransaction? tx, List<TareaExtraFila> tareas)
    {
        if (tareas.Count == 0) return;
        var ids = tareas.Select(t => t.Id).ToList();
        var implicados = await cn.QueryAsync<ImplicadoItem>(
            """
            SELECT ti.TareaExtraId, r.Id, r.Nombre, r.Activo
            FROM TareaExtraImplicado ti
            JOIN Responsable r ON r.Id = ti.ResponsableId
            WHERE ti.TareaExtraId = ANY(@ids)
            ORDER BY r.Orden, r.Nombre
            """,
            new { ids = ids.ToArray() }, tx);
        var porTarea = implicados.ToLookup(i => i.TareaExtraId);
        foreach (var tarea in tareas)
        {
            tarea.Implicados = porTarea[tarea.Id].ToList();
        }
    }
}

public static class TareasExtraEndpoints
{
    public static void MapearTareasExtra(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tareas-extra");
        g.MapGet("", Listar);
        g.MapGet("/{id:int}", Obtener);
        g.MapPost("", Crear);
        g.MapPut("/{id:int}", Actualizar);
        g.MapDelete("/{id:int}", Eliminar);
    }

    private static async Task<IResult> Listar(string? q, int? area, TareasExtraRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.ListarAsync(q, area, ct));

    private static async Task<IResult> Obtener(int id, TareasExtraRepositorio repo, CancellationToken ct)
    {
        var tarea = await repo.ObtenerAsync(id, ct);
        return tarea is null ? Results.Json(new { mensaje = "La tarea no existe." }, statusCode: 404) : Results.Ok(tarea);
    }

    private static async Task<IResult> Crear(TareaExtraEntrada entrada, HttpContext http, TareasExtraRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.GuardarAsync(null, entrada, http.Usuario(), ct));

    private static async Task<IResult> Actualizar(int id, TareaExtraEntrada entrada, HttpContext http, TareasExtraRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.GuardarAsync(id, entrada, http.Usuario(), ct));

    private static async Task<IResult> Eliminar(int id, HttpContext http, TareasExtraRepositorio repo, CancellationToken ct)
    {
        await repo.EliminarAsync(id, http.Usuario(), ct);
        return Results.NoContent();
    }
}
