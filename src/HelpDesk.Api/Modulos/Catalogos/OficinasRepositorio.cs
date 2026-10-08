using HelpDesk.Api.Modulos.TiempoReal;

namespace HelpDesk.Api.Modulos.Catalogos;

public sealed class OficinaItem
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
    public int Solicitudes { get; set; }
    public DateTime? UltimaSolicitud { get; set; }
}

public sealed class OficinaEntrada
{
    public string? Nombre { get; set; }
    public bool? Activo { get; set; }
}

public sealed class UnificarOficinaEntrada
{
    public int DestinoId { get; set; }
}

/// <summary>Oficinas: se crean solas al registrar solicitudes y el administrador puede renombrarlas o unificarlas.</summary>
public sealed class OficinasRepositorio(BaseDatos db, Notificador notificador)
{
    public const int LargoNombre = 150;

    /// <summary>
    /// Devuelve el Id de la oficina con ese nombre (sin distinguir mayúsculas ni tildes).
    /// Si no existe, la crea.
    /// </summary>
    public static async Task<int?> ObtenerOCrearAsync(IDbConnection cn, IDbTransaction tx, string? nombre, DateTime ahora)
    {
        var limpio = Texto.Recortar(Texto.LimpiarLinea(nombre), LargoNombre);
        if (limpio is null) return null;

        // Bloqueo hasta el fin de la transacción para que dos usuarios no creen la misma oficina
        // a la vez (equivale al UPDLOCK, HOLDLOCK de la versión SQL Server).
        await cn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('HelpDesk.Oficina'))", transaction: tx);

        var existente = await cn.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT Id FROM Oficina
            WHERE normalizar(Nombre) = normalizar(@nombre)
            ORDER BY Activo DESC, Id
            LIMIT 1
            """,
            new { nombre = limpio }, tx);
        if (existente is int id) return id;

        return await cn.ExecuteScalarAsync<int>(
            "INSERT INTO Oficina (Nombre, Activo, CreadoEn) VALUES (@nombre, TRUE, @ahora) RETURNING Id",
            new { nombre = limpio, ahora }, tx);
    }

    public async Task<List<OficinaItem>> ListarAsync(string? texto, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        var filtro = Texto.LimpiarLinea(texto);
        return (await cn.QueryAsync<OficinaItem>(
            """
            SELECT o.Id, o.Nombre, o.Activo,
                   CAST(COUNT(s.Id) AS INTEGER) AS Solicitudes,
                   MAX(s.FechaIngreso) AS UltimaSolicitud
            FROM Oficina o
            LEFT JOIN Solicitud s ON s.OficinaId = o.Id AND s.EliminadoEn IS NULL
            WHERE @filtro IS NULL OR normalizar(o.Nombre) LIKE normalizar(@patron)
            GROUP BY o.Id, o.Nombre, o.Activo
            ORDER BY o.Nombre
            """,
            new { filtro, patron = "%" + Texto.EscaparLike(filtro ?? "") + "%" })).ToList();
    }

    public async Task<OficinaItem> ActualizarAsync(int id, OficinaEntrada entrada, UsuarioActual usuario, DateTime ahora, CancellationToken ct)
    {
        var nombre = Texto.LimpiarLinea(entrada.Nombre) ?? throw ErrorApi.Validacion("El nombre es obligatorio.");
        if (nombre.Length > LargoNombre) throw ErrorApi.Validacion($"El nombre no puede superar {LargoNombre} caracteres.");

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var anterior = await cn.QuerySingleOrDefaultAsync<OficinaItem>(
            "SELECT Id, Nombre, Activo FROM Oficina WHERE Id = @id", new { id }, tx)
            ?? throw ErrorApi.NoEncontrado("La oficina no existe.");

        var duplicada = await cn.ExecuteScalarAsync<int?>(
            "SELECT Id FROM Oficina WHERE Id <> @id AND normalizar(Nombre) = normalizar(@nombre) LIMIT 1",
            new { id, nombre }, tx);
        if (duplicada is not null)
            throw ErrorApi.Conflicto("Ya existe otra oficina con ese nombre. Usá \"Unificar\" para juntarlas.");

        await cn.ExecuteAsync("UPDATE Oficina SET Nombre = @nombre, Activo = @activo WHERE Id = @id",
            new { id, nombre, activo = entrada.Activo ?? anterior.Activo }, tx);

        if (!string.Equals(anterior.Nombre, nombre, StringComparison.Ordinal))
        {
            await cn.ExecuteAsync(
                """
                INSERT INTO SolicitudHistorial (SolicitudId, FechaHora, UsuarioId, Accion, Campo, ValorAnterior, ValorNuevo, Detalle)
                SELECT s.Id, @ahora, @usuarioId, 'MODIFICADA', 'Oficina', @anterior, @nuevo, 'Oficina renombrada desde Configuración'
                FROM Solicitud s WHERE s.OficinaId = @id
                """,
                new { ahora, usuarioId = usuario.Id, anterior = anterior.Nombre, nuevo = nombre, id }, tx);
        }

        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, "Oficina", id.ToString(CultureInfo.InvariantCulture),
            "MODIFICADO", new { anterior = anterior.Nombre, nuevo = nombre, activo = entrada.Activo });
        await notificador.PublicarAsync(cn, tx, [TiposCambio.Catalogos, TiposCambio.Solicitudes], "oficina");
        tx.Commit();

        anterior.Nombre = nombre;
        anterior.Activo = entrada.Activo ?? anterior.Activo;
        return anterior;
    }

    /// <summary>Mueve todas las solicitudes de una oficina a otra y elimina la de origen.</summary>
    public async Task<int> UnificarAsync(int origenId, int destinoId, UsuarioActual usuario, DateTime ahora, CancellationToken ct)
    {
        if (origenId == destinoId) throw ErrorApi.Validacion("Elegí una oficina distinta.");

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var origen = await cn.QuerySingleOrDefaultAsync<string>("SELECT Nombre FROM Oficina WHERE Id = @origenId", new { origenId }, tx)
            ?? throw ErrorApi.NoEncontrado("La oficina de origen no existe.");
        var destino = await cn.QuerySingleOrDefaultAsync<string>("SELECT Nombre FROM Oficina WHERE Id = @destinoId", new { destinoId }, tx)
            ?? throw ErrorApi.NoEncontrado("La oficina de destino no existe.");

        await cn.ExecuteAsync(
            """
            INSERT INTO SolicitudHistorial (SolicitudId, FechaHora, UsuarioId, Accion, Campo, ValorAnterior, ValorNuevo, Detalle)
            SELECT s.Id, @ahora, @usuarioId, 'MODIFICADA', 'Oficina', @origen, @destino, 'Unificación de oficinas'
            FROM Solicitud s WHERE s.OficinaId = @origenId
            """,
            new { ahora, usuarioId = usuario.Id, origen, destino, origenId }, tx);

        var movidas = await cn.ExecuteAsync(
            "UPDATE Solicitud SET OficinaId = @destinoId, Version = Version + 1 WHERE OficinaId = @origenId",
            new { origenId, destinoId }, tx);
        await cn.ExecuteAsync("DELETE FROM Oficina WHERE Id = @origenId", new { origenId }, tx);

        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, "Oficina", origenId.ToString(CultureInfo.InvariantCulture),
            "UNIFICADA", new { origen, destino, destinoId, solicitudesMovidas = movidas });
        await notificador.PublicarAsync(cn, tx, [TiposCambio.Catalogos, TiposCambio.Solicitudes], "oficina");
        tx.Commit();
        return movidas;
    }
}
