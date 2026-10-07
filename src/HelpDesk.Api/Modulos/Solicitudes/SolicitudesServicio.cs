using HelpDesk.Api.Modulos.Catalogos;

namespace HelpDesk.Api.Modulos.Solicitudes;

/// <summary>Reglas de negocio de las solicitudes: alta, edición, resolución e historial.</summary>
public sealed class SolicitudesServicio(BaseDatos db, Reloj reloj)
{
    public const int LargoFuncionario = 150;
    public const int LargoDescripcion = 500;
    public const int DuracionMaxima = 100_000;

    /// <summary>Campos editables: clave JSON → etiqueta para el historial.</summary>
    private static readonly Dictionary<string, string> CamposEditables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nombreFuncionario"] = "Funcionario",
        ["oficina"] = "Oficina",
        ["medioContactoId"] = "Medio de contacto",
        ["tipoSolicitudId"] = "Tipo",
        ["descripcion"] = "Descripción",
        ["observaciones"] = "Observaciones",
        ["responsableId"] = "Responsable",
        ["duracionEstimadaMin"] = "Duración estimada (min)",
        ["estadoId"] = "Estado",
        ["prioridadId"] = "Prioridad",
    };

    public sealed record ConflictoCampo(
        string Campo, string Etiqueta, string? ValorActual, string? ActualizadoPor, DateTime ActualizadoEn);

    // ------------------------------------------------------------------ consultas

    public async Task<PaginaResultado<SolicitudFila>> ListarAsync(FiltroSolicitudes f, UsuarioActual usuario, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = ConsultaSolicitudes.Where(f, usuario, reloj.Hoy(), p);
        var orden = ConsultaSolicitudes.OrderBy(f, p);
        p.Add("offset", (f.Pagina - 1) * f.Tamano);
        p.Add("tamano", f.Tamano);

        var sql = $"""
            SELECT COUNT(*) {ConsultaSolicitudes.Desde} {where};
            {ConsultaSolicitudes.Columnas} {ConsultaSolicitudes.Desde} {where} {orden}
            LIMIT @tamano OFFSET @offset;
            """;

        await using var cn = await db.AbrirAsync(ct);
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<SolicitudFila>()).ToList();
        return new PaginaResultado<SolicitudFila> { Items = items, Total = total, Pagina = f.Pagina, Tamano = f.Tamano };
    }

    /// <summary>Todas las solicitudes que cumplen el filtro (para exportar).</summary>
    public async Task<List<SolicitudFila>> ListarParaExportarAsync(FiltroSolicitudes f, UsuarioActual usuario, int maximo, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = ConsultaSolicitudes.Where(f, usuario, reloj.Hoy(), p);
        var orden = ConsultaSolicitudes.OrderBy(f, p);
        p.Add("maximo", maximo);
        var sql = $"""
            {ConsultaSolicitudes.Columnas} {ConsultaSolicitudes.Desde} {where} {orden}
            LIMIT @maximo;
            """;
        await using var cn = await db.AbrirAsync(ct);
        return (await cn.QueryAsync<SolicitudFila>(new CommandDefinition(sql, p, cancellationToken: ct, commandTimeout: 120))).ToList();
    }

    public async Task<SolicitudDetalle?> ObtenerDetalleAsync(int id, CancellationToken ct = default)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await ObtenerDetalleAsync(cn, null, id);
    }

    private static async Task<SolicitudDetalle?> ObtenerDetalleAsync(IDbConnection cn, IDbTransaction? tx, int id)
    {
        var sql = $"""
            {ConsultaSolicitudes.Columnas},
                   s.CreadoEn, s.ActualizadoEn, s.FilaExcel, s.ImportacionId,
                   uc.NombreCompleto AS CreadoPor, ua.NombreCompleto AS ActualizadoPor
            {ConsultaSolicitudes.Desde}
            LEFT JOIN Usuario uc ON uc.Id = s.CreadoPorId
            LEFT JOIN Usuario ua ON ua.Id = s.ActualizadoPorId
            WHERE s.Id = @id AND s.EliminadoEn IS NULL;

            SELECT h.Id, h.FechaHora, u.NombreCompleto AS Usuario, h.Accion, h.Campo, h.ValorAnterior, h.ValorNuevo, h.Detalle
            FROM SolicitudHistorial h
            LEFT JOIN Usuario u ON u.Id = h.UsuarioId
            WHERE h.SolicitudId = @id
            ORDER BY h.Id;
            """;
        using var multi = await cn.QueryMultipleAsync(sql, new { id }, tx);
        var detalle = await multi.ReadSingleOrDefaultAsync<SolicitudDetalle>();
        if (detalle is null) return null;
        detalle.Historial = (await multi.ReadAsync<HistorialItem>()).ToList();
        return detalle;
    }

    public async Task<Contadores> ContadoresAsync(UsuarioActual usuario, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await cn.QuerySingleAsync<Contadores>(
            """
            SELECT
                COALESCE(SUM(CASE WHEN e.EsResuelto = FALSE THEN 1 ELSE 0 END), 0) AS Pendientes,
                COALESCE(SUM(CASE WHEN e.EsResuelto = FALSE AND s.ResponsableId = @rid THEN 1 ELSE 0 END), 0) AS MisPendientes,
                COALESCE(SUM(CASE WHEN s.EstadoId IS NULL THEN 1 ELSE 0 END), 0) AS SinEstado,
                COALESCE(SUM(CASE WHEN s.FechaIngreso >= @hoy THEN 1 ELSE 0 END), 0) AS Hoy
            FROM Solicitud s
            LEFT JOIN Estado e ON e.Id = s.EstadoId
            WHERE s.EliminadoEn IS NULL
            """,
            new { rid = usuario.ResponsableId, hoy = reloj.Hoy() });
    }

    /// <summary>ID que recibiría una solicitud creada ahora (orientativo).</summary>
    public async Task<int> ProximoIdAsync(CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN COALESCE(sq.UltimoValor, 0) > COALESCE(m.MaxId, 0) THEN COALESCE(sq.UltimoValor, 0) ELSE COALESCE(m.MaxId, 0) END + 1
            FROM (SELECT MAX(Id) AS MaxId FROM Solicitud) m
            LEFT JOIN Secuencia sq ON sq.Nombre = 'Solicitud'
            """);
    }

    // ------------------------------------------------------------------ alta

    public async Task<SolicitudDetalle> CrearAsync(SolicitudEntrada e, UsuarioActual usuario, CancellationToken ct)
    {
        var funcionario = Texto.Recortar(Texto.LimpiarLinea(e.NombreFuncionario), LargoFuncionario);
        var descripcion = Texto.LimpiarLinea(e.Descripcion) ?? throw ErrorApi.Validacion("La descripción es obligatoria.");
        if (descripcion.Length > LargoDescripcion)
            throw ErrorApi.Validacion($"La descripción no puede superar {LargoDescripcion} caracteres.");
        var observaciones = Texto.Limpiar(e.Observaciones);
        if (e.DuracionEstimadaMin is < 0 or > DuracionMaxima)
            throw ErrorApi.Validacion("La duración estimada no es válida.");

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();

        var mapas = await CatalogosRepositorio.CargarMapasAsync(cn, tx);
        ValidarReferencia(mapas.Medios, e.MedioContactoId, "el medio de contacto", obligatorio: true);
        ValidarReferencia(mapas.Tipos, e.TipoSolicitudId, "el tipo de solicitud", obligatorio: false);
        ValidarReferencia(mapas.Responsables, e.ResponsableId, "el responsable", obligatorio: true);
        ValidarReferencia(mapas.Estados, e.EstadoId, "el estado", obligatorio: true);
        ValidarReferencia(mapas.Prioridades, e.PrioridadId, "la prioridad", obligatorio: true);

        var ahora = reloj.Ahora();
        var oficinaId = await OficinasRepositorio.ObtenerOCrearAsync(cn, tx, e.Oficina, ahora);
        var id = await SiguienteIdAsync(cn, tx);
        var resuelta = mapas.EsResuelto(e.EstadoId);

        await cn.ExecuteAsync(
            """
            INSERT INTO Solicitud
                (Id, FechaIngreso, DiaSemana, NombreFuncionario, OficinaId, MedioContactoId, TipoSolicitudId,
                 Descripcion, Observaciones, ResponsableId, DuracionEstimadaMin, EstadoId, PrioridadId,
                 FechaResolucion, MinutosResolucion, Origen, Version, CreadoPorId, CreadoEn, ActualizadoPorId, ActualizadoEn)
            VALUES
                (@Id, @FechaIngreso, @DiaSemana, @NombreFuncionario, @OficinaId, @MedioContactoId, @TipoSolicitudId,
                 @Descripcion, @Observaciones, @ResponsableId, @DuracionEstimadaMin, @EstadoId, @PrioridadId,
                 @FechaResolucion, @MinutosResolucion, 'APP', 1, @UsuarioId, @FechaIngreso, @UsuarioId, @FechaIngreso)
            """,
            new
            {
                Id = id,
                FechaIngreso = ahora,
                DiaSemana = Reloj.DiaSemana(ahora),
                NombreFuncionario = funcionario,
                OficinaId = oficinaId,
                e.MedioContactoId,
                e.TipoSolicitudId,
                Descripcion = descripcion,
                Observaciones = observaciones,
                e.ResponsableId,
                e.DuracionEstimadaMin,
                e.EstadoId,
                e.PrioridadId,
                FechaResolucion = resuelta ? ahora : (DateTime?)null,
                MinutosResolucion = resuelta ? 0 : (int?)null,
                UsuarioId = usuario.Id,
            },
            tx);

        var estado = MapasCatalogo.Nombre(mapas.Estados, e.EstadoId);
        var responsable = MapasCatalogo.Nombre(mapas.Responsables, e.ResponsableId);
        await InsertarHistorialAsync(cn, tx, id, ahora, usuario.Id,
        [
            new CambioHistorial("CREADA", null, null, null,
                $"Estado inicial: {estado}. Responsable: {responsable}.")
        ]);

        tx.Commit();
        return await ObtenerDetalleAsync(cn, null, id)
            ?? throw new InvalidOperationException("No se pudo leer la solicitud recién creada.");
    }

    // ------------------------------------------------------------------ edición

    public async Task<SolicitudDetalle> ActualizarAsync(int id, ActualizacionEntrada entrada, UsuarioActual usuario, CancellationToken ct)
    {
        var cambios = entrada.Cambios;
        if (cambios is null || cambios.Count == 0) throw ErrorApi.Validacion("No se indicaron cambios.");

        await using var cn = await db.AbrirAsync(ct);
        List<ConflictoCampo> conflictos;
        using (var tx = cn.BeginTransaction())
        {
            conflictos = await AplicarCambiosAsync(cn, tx, id, cambios, entrada.Base, usuario);
            if (conflictos.Count > 0) tx.Rollback();
            else tx.Commit();
        }

        if (conflictos.Count > 0)
        {
            var actual = await ObtenerDetalleAsync(cn, null, id);
            throw ErrorApi.Conflicto(
                "Otra persona modificó esta solicitud mientras la editabas. Revisá los cambios antes de guardar.",
                new { conflictos, actual });
        }

        return await ObtenerDetalleAsync(cn, null, id) ?? throw ErrorApi.NoEncontrado("La solicitud no existe.");
    }

    /// <summary>Pasa la solicitud al estado "resuelto" predeterminado (botón Resolver).</summary>
    public async Task<SolicitudDetalle> ResolverAsync(int id, ResolverEntrada entrada, UsuarioActual usuario, CancellationToken ct)
    {
        int estadoResuelto;
        await using (var cn = await db.AbrirAsync(ct))
        {
            estadoResuelto = await cn.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM Estado WHERE EsResuelto = TRUE AND Activo = TRUE ORDER BY Predeterminado DESC, Orden, Id LIMIT 1")
                ?? throw ErrorApi.Validacion("No hay ningún estado activo marcado como resuelto. Revisá Configuración → Estados.");
        }

        var cambios = new Dictionary<string, JsonElement>
        {
            ["estadoId"] = JsonSerializer.SerializeToElement(estadoResuelto),
        };
        if (entrada.Observaciones is not null)
            cambios["observaciones"] = JsonSerializer.SerializeToElement(entrada.Observaciones);
        if (entrada.DuracionEstimadaMin is not null)
            cambios["duracionEstimadaMin"] = JsonSerializer.SerializeToElement(entrada.DuracionEstimadaMin.Value);

        return await ActualizarAsync(id, new ActualizacionEntrada { Cambios = cambios }, usuario, ct);
    }

    public async Task EliminarAsync(int id, UsuarioActual usuario, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var ahora = reloj.Ahora();
        var filas = await cn.ExecuteAsync(
            """
            UPDATE Solicitud
            SET EliminadoEn = @ahora, EliminadoPorId = @usuarioId, Version = Version + 1
            WHERE Id = @id AND EliminadoEn IS NULL
            """,
            new { id, ahora, usuarioId = usuario.Id }, tx);
        if (filas == 0) throw ErrorApi.NoEncontrado("La solicitud no existe o ya fue eliminada.");
        await InsertarHistorialAsync(cn, tx, id, ahora, usuario.Id,
            [new CambioHistorial("ELIMINADA", null, null, null, $"Eliminada por {usuario.Nombre}. El número {id} no se volverá a usar.")]);
        tx.Commit();
    }

    /// <summary>Corrección administrativa de la fecha de ingreso (queda en el historial).</summary>
    public async Task<SolicitudDetalle> CorregirFechaIngresoAsync(int id, CorreccionFechaEntrada entrada, UsuarioActual usuario, CancellationToken ct)
    {
        if (entrada.FechaIngreso is not DateTime fechaRecibida) throw ErrorApi.Validacion("Indicá la nueva fecha de ingreso.");
        var motivo = Texto.LimpiarLinea(entrada.Motivo) ?? throw ErrorApi.Validacion("Indicá el motivo de la corrección.");
        var fecha = new DateTime(fechaRecibida.Year, fechaRecibida.Month, fechaRecibida.Day,
            fechaRecibida.Hour, fechaRecibida.Minute, 0, DateTimeKind.Unspecified);
        var ahora = reloj.Ahora();
        if (fecha > ahora.AddDays(1) || fecha.Year < 2000) throw ErrorApi.Validacion("La fecha indicada no es válida.");

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var actual = await cn.QuerySingleOrDefaultAsync<SolicitudRegistro>(
            "SELECT * FROM Solicitud WHERE Id = @id AND EliminadoEn IS NULL FOR UPDATE", new { id }, tx)
            ?? throw ErrorApi.NoEncontrado("La solicitud no existe.");

        int? minutos = actual.FechaResolucion is DateTime resolucion ? Reloj.MinutosEntre(fecha, resolucion) : null;
        await cn.ExecuteAsync(
            """
            UPDATE Solicitud
            SET FechaIngreso = @fecha, DiaSemana = @dia, MinutosResolucion = @minutos,
                Version = Version + 1, ActualizadoEn = @ahora, ActualizadoPorId = @usuarioId
            WHERE Id = @id
            """,
            new { id, fecha, dia = Reloj.DiaSemana(fecha), minutos, ahora, usuarioId = usuario.Id }, tx);
        await InsertarHistorialAsync(cn, tx, id, ahora, usuario.Id,
            [new CambioHistorial("FECHA_CORREGIDA", "Fecha de ingreso", Reloj.Formatear(actual.FechaIngreso), Reloj.Formatear(fecha), motivo)]);
        tx.Commit();
        return await ObtenerDetalleAsync(cn, null, id) ?? throw ErrorApi.NoEncontrado();
    }

    // ------------------------------------------------------------------ internos

    private async Task<List<ConflictoCampo>> AplicarCambiosAsync(
        NpgsqlConnection cn,
        NpgsqlTransaction tx,
        int id,
        Dictionary<string, JsonElement> cambios,
        Dictionary<string, JsonElement>? baseCliente,
        UsuarioActual usuario)
    {
        var actual = await cn.QuerySingleOrDefaultAsync<SolicitudRegistro>(
            "SELECT * FROM Solicitud WHERE Id = @id AND EliminadoEn IS NULL FOR UPDATE", new { id }, tx)
            ?? throw ErrorApi.NoEncontrado("La solicitud no existe o fue eliminada.");
        var mapas = await CatalogosRepositorio.CargarMapasAsync(cn, tx);
        var oficinaActual = actual.OficinaId is int oficinaId
            ? await cn.QuerySingleOrDefaultAsync<string>("SELECT Nombre FROM Oficina WHERE Id = @oficinaId", new { oficinaId }, tx)
            : null;
        var ahora = reloj.Ahora();

        var sets = new List<string>();
        var p = new DynamicParameters();
        var historial = new List<CambioHistorial>();
        var conflictos = new List<ConflictoCampo>();
        string? ultimoEditor = null;

        foreach (var (claveRecibida, valor) in cambios)
        {
            if (!CamposEditables.TryGetValue(claveRecibida, out var etiqueta))
                throw ErrorApi.Validacion($"El campo '{claveRecibida}' no se puede modificar.");
            var campo = CamposEditables.Keys.First(k => k.Equals(claveRecibida, StringComparison.OrdinalIgnoreCase));

            // 1) ¿Otra persona cambió este campo desde que el usuario abrió la solicitud?
            if (baseCliente is not null && baseCliente.TryGetValue(claveRecibida, out var valorBase))
            {
                var comparableBase = ComparableDesdeJson(campo, valorBase);
                var comparableActual = ComparableActual(campo, actual, oficinaActual);
                if (!string.Equals(comparableBase, comparableActual, StringComparison.Ordinal))
                {
                    ultimoEditor ??= actual.ActualizadoPorId is int editorId
                        ? await cn.QuerySingleOrDefaultAsync<string>(
                            "SELECT NombreCompleto FROM Usuario WHERE Id = @editorId", new { editorId }, tx)
                        : "importación";
                    conflictos.Add(new ConflictoCampo(campo, etiqueta, TextoActual(campo, actual, oficinaActual, mapas),
                        ultimoEditor, actual.ActualizadoEn));
                    continue;
                }
            }

            // 2) Aplicar el cambio si el valor es distinto.
            switch (campo)
            {
                case "nombreFuncionario":
                {
                    var nuevo = Texto.Recortar(Texto.LimpiarLinea(LeerTexto(valor)), LargoFuncionario);
                    if (nuevo == actual.NombreFuncionario) break;
                    sets.Add("NombreFuncionario = @NombreFuncionario");
                    p.Add("NombreFuncionario", nuevo);
                    historial.Add(new CambioHistorial("MODIFICADA", etiqueta, actual.NombreFuncionario, nuevo, null));
                    break;
                }
                case "oficina":
                {
                    var nombre = Texto.LimpiarLinea(LeerTexto(valor));
                    if (Texto.Normalizar(nombre) == Texto.Normalizar(oficinaActual) && nombre == oficinaActual) break;
                    var nuevoId = await OficinasRepositorio.ObtenerOCrearAsync(cn, tx, nombre, ahora);
                    if (nuevoId == actual.OficinaId) break;
                    var nombreFinal = nuevoId is int idOficina
                        ? await cn.QuerySingleAsync<string>("SELECT Nombre FROM Oficina WHERE Id = @idOficina", new { idOficina }, tx)
                        : null;
                    sets.Add("OficinaId = @OficinaId");
                    p.Add("OficinaId", nuevoId);
                    historial.Add(new CambioHistorial("MODIFICADA", etiqueta, oficinaActual, nombreFinal, null));
                    break;
                }
                case "descripcion":
                {
                    var nuevo = Texto.LimpiarLinea(LeerTexto(valor)) ?? throw ErrorApi.Validacion("La descripción es obligatoria.");
                    if (nuevo.Length > LargoDescripcion)
                        throw ErrorApi.Validacion($"La descripción no puede superar {LargoDescripcion} caracteres.");
                    if (nuevo == actual.Descripcion) break;
                    sets.Add("Descripcion = @Descripcion");
                    p.Add("Descripcion", nuevo);
                    historial.Add(new CambioHistorial("MODIFICADA", etiqueta, actual.Descripcion, nuevo, null));
                    break;
                }
                case "observaciones":
                {
                    var nuevo = Texto.Limpiar(LeerTexto(valor));
                    if (UnificarSaltos(nuevo) == UnificarSaltos(Texto.Limpiar(actual.Observaciones))) break;
                    sets.Add("Observaciones = @Observaciones");
                    p.Add("Observaciones", nuevo);
                    var accion = string.IsNullOrEmpty(actual.Observaciones) ? "Observación agregada" : "Observación modificada";
                    historial.Add(new CambioHistorial("MODIFICADA", etiqueta, actual.Observaciones, nuevo, accion));
                    break;
                }
                case "duracionEstimadaMin":
                {
                    var nuevo = LeerEntero(valor);
                    if (nuevo is < 0 or > DuracionMaxima) throw ErrorApi.Validacion("La duración estimada no es válida.");
                    if (nuevo == actual.DuracionEstimadaMin) break;
                    sets.Add("DuracionEstimadaMin = @DuracionEstimadaMin");
                    p.Add("DuracionEstimadaMin", nuevo);
                    historial.Add(new CambioHistorial("MODIFICADA", etiqueta, Num(actual.DuracionEstimadaMin), Num(nuevo), null));
                    break;
                }
                case "medioContactoId":
                    CambiarReferencia("MedioContactoId", etiqueta, actual.MedioContactoId, valor, mapas.Medios, "el medio de contacto", obligatorio: true);
                    break;
                case "tipoSolicitudId":
                    CambiarReferencia("TipoSolicitudId", etiqueta, actual.TipoSolicitudId, valor, mapas.Tipos, "el tipo de solicitud", obligatorio: false);
                    break;
                case "responsableId":
                    CambiarReferencia("ResponsableId", etiqueta, actual.ResponsableId, valor, mapas.Responsables, "el responsable", obligatorio: true);
                    break;
                case "prioridadId":
                    CambiarReferencia("PrioridadId", etiqueta, actual.PrioridadId, valor, mapas.Prioridades, "la prioridad", obligatorio: true);
                    break;
                case "estadoId":
                {
                    var nuevo = LeerEntero(valor);
                    if (nuevo == actual.EstadoId) break;
                    ValidarReferencia(mapas.Estados, nuevo, "el estado", obligatorio: true);
                    sets.Add("EstadoId = @EstadoId");
                    p.Add("EstadoId", nuevo);

                    var nombreAnterior = MapasCatalogo.Nombre(mapas.Estados, actual.EstadoId);
                    var nombreNuevo = MapasCatalogo.Nombre(mapas.Estados, nuevo);
                    var eraResuelta = mapas.EsResuelto(actual.EstadoId);
                    var esResuelta = mapas.EsResuelto(nuevo);

                    if (!eraResuelta && esResuelta)
                    {
                        var minutos = Reloj.MinutosEntre(actual.FechaIngreso, ahora);
                        sets.Add("FechaResolucion = @FechaResolucion");
                        sets.Add("MinutosResolucion = @MinutosResolucion");
                        p.Add("FechaResolucion", ahora);
                        p.Add("MinutosResolucion", minutos);
                        historial.Add(new CambioHistorial("RESUELTA", "Estado", nombreAnterior, nombreNuevo,
                            $"Tiempo total desde el ingreso: {Reloj.FormatearDuracion(minutos)}"));
                    }
                    else if (eraResuelta && !esResuelta)
                    {
                        sets.Add("FechaResolucion = NULL");
                        sets.Add("MinutosResolucion = NULL");
                        var detalle = actual.FechaResolucion is DateTime resuelta
                            ? $"Había sido resuelta el {Reloj.Formatear(resuelta)}"
                            : null;
                        historial.Add(new CambioHistorial("REABIERTA", "Estado", nombreAnterior, nombreNuevo, detalle));
                    }
                    else
                    {
                        historial.Add(new CambioHistorial("ESTADO", "Estado", nombreAnterior, nombreNuevo, null));
                    }
                    break;
                }
            }
        }

        if (conflictos.Count > 0 || sets.Count == 0) return conflictos;

        sets.Add("Version = Version + 1");
        sets.Add("ActualizadoEn = @ActualizadoEn");
        sets.Add("ActualizadoPorId = @ActualizadoPorId");
        p.Add("ActualizadoEn", ahora);
        p.Add("ActualizadoPorId", usuario.Id);
        p.Add("IdSolicitud", id);
        await cn.ExecuteAsync($"UPDATE Solicitud SET {string.Join(", ", sets)} WHERE Id = @IdSolicitud", p, tx);
        await InsertarHistorialAsync(cn, tx, id, ahora, usuario.Id, historial);
        return conflictos;

        void CambiarReferencia(string columna, string etiquetaCampo, int? anterior, JsonElement valorJson,
            Dictionary<int, ItemCatalogo> mapa, string descripcionCampo, bool obligatorio)
        {
            var nuevo = LeerEntero(valorJson);
            if (nuevo == anterior) return;
            ValidarReferencia(mapa, nuevo, descripcionCampo, obligatorio);
            sets.Add($"{columna} = @{columna}");
            p.Add(columna, nuevo);
            historial.Add(new CambioHistorial("MODIFICADA", etiquetaCampo,
                MapasCatalogo.Nombre(mapa, anterior), MapasCatalogo.Nombre(mapa, nuevo), null));
        }
    }

    private static string? ComparableActual(string campo, SolicitudRegistro s, string? oficina) => campo switch
    {
        "nombreFuncionario" => Texto.LimpiarLinea(s.NombreFuncionario),
        "oficina" => VacioANull(Texto.Normalizar(oficina)),
        "descripcion" => Texto.LimpiarLinea(s.Descripcion),
        "observaciones" => UnificarSaltos(Texto.Limpiar(s.Observaciones)),
        "duracionEstimadaMin" => Num(s.DuracionEstimadaMin),
        "medioContactoId" => Num(s.MedioContactoId),
        "tipoSolicitudId" => Num(s.TipoSolicitudId),
        "responsableId" => Num(s.ResponsableId),
        "estadoId" => Num(s.EstadoId),
        "prioridadId" => Num(s.PrioridadId),
        _ => null,
    };

    private static string? ComparableDesdeJson(string campo, JsonElement valor) => campo switch
    {
        "nombreFuncionario" or "descripcion" => Texto.LimpiarLinea(LeerTexto(valor)),
        "oficina" => VacioANull(Texto.Normalizar(LeerTexto(valor))),
        "observaciones" => UnificarSaltos(Texto.Limpiar(LeerTexto(valor))),
        _ => Num(LeerEntero(valor)),
    };

    private static string? TextoActual(string campo, SolicitudRegistro s, string? oficina, MapasCatalogo mapas) => campo switch
    {
        "nombreFuncionario" => s.NombreFuncionario,
        "oficina" => oficina,
        "descripcion" => s.Descripcion,
        "observaciones" => s.Observaciones,
        "duracionEstimadaMin" => Num(s.DuracionEstimadaMin),
        "medioContactoId" => MapasCatalogo.Nombre(mapas.Medios, s.MedioContactoId),
        "tipoSolicitudId" => MapasCatalogo.Nombre(mapas.Tipos, s.TipoSolicitudId),
        "responsableId" => MapasCatalogo.Nombre(mapas.Responsables, s.ResponsableId),
        "estadoId" => MapasCatalogo.Nombre(mapas.Estados, s.EstadoId),
        "prioridadId" => MapasCatalogo.Nombre(mapas.Prioridades, s.PrioridadId),
        _ => null,
    };

    private static void ValidarReferencia(Dictionary<int, ItemCatalogo> mapa, int? id, string descripcion, bool obligatorio)
    {
        if (id is not int valor)
        {
            if (obligatorio) throw ErrorApi.Validacion($"Seleccioná {descripcion}.");
            return;
        }
        if (!mapa.TryGetValue(valor, out var item))
            throw ErrorApi.Validacion($"El valor elegido para {descripcion} no existe.");
        if (!item.Activo)
            throw ErrorApi.Validacion($"El valor elegido para {descripcion} está desactivado.");
    }

    private static string? LeerTexto(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => valor.GetString(),
        JsonValueKind.Number => valor.GetRawText(),
        _ => throw ErrorApi.Validacion("Se esperaba un texto."),
    };

    private static int? LeerEntero(JsonElement valor)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Number when valor.TryGetInt32(out var numero):
                return numero;
            case JsonValueKind.String:
                var texto = valor.GetString();
                if (string.IsNullOrWhiteSpace(texto)) return null;
                if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var desdeTexto)) return desdeTexto;
                break;
        }
        throw ErrorApi.Validacion("Se esperaba un número entero.");
    }

    private static string? Num(int? valor) => valor?.ToString(CultureInfo.InvariantCulture);

    private static string? VacioANull(string valor) => valor.Length == 0 ? null : valor;

    private static string? UnificarSaltos(string? valor) => valor?.Replace("\r\n", "\n");

    /// <summary>Reserva el próximo número de solicitud. Nunca reutiliza números.</summary>
    internal static async Task<int> SiguienteIdAsync(IDbConnection cn, IDbTransaction tx)
    {
        await AsegurarSecuenciaAsync(cn, tx);
        // El UPDATE bloquea la fila de Secuencia hasta el fin de la transacción:
        // dos altas simultáneas nunca reciben el mismo número.
        return await cn.ExecuteScalarAsync<int>(
            """
            UPDATE Secuencia
            SET UltimoValor = GREATEST(UltimoValor, (SELECT COALESCE(MAX(Id), 0) FROM Solicitud)) + 1
            WHERE Nombre = 'Solicitud'
            RETURNING UltimoValor
            """,
            transaction: tx);
    }

    /// <summary>Crea la fila de numeración de solicitudes si todavía no existe.</summary>
    internal static Task AsegurarSecuenciaAsync(IDbConnection cn, IDbTransaction tx) =>
        cn.ExecuteAsync(
            "INSERT INTO Secuencia (Nombre, UltimoValor) VALUES ('Solicitud', 0) ON CONFLICT (Nombre) DO NOTHING",
            transaction: tx);

    internal static Task InsertarHistorialAsync(IDbConnection cn, IDbTransaction tx, int solicitudId, DateTime fecha,
        int? usuarioId, IEnumerable<CambioHistorial> cambios)
    {
        var filas = cambios.Select(c => new
        {
            SolicitudId = solicitudId,
            FechaHora = fecha,
            UsuarioId = usuarioId,
            c.Accion,
            c.Campo,
            c.ValorAnterior,
            c.ValorNuevo,
            Detalle = Texto.Recortar(c.Detalle, 2000),
        }).ToList();
        if (filas.Count == 0) return Task.CompletedTask;
        return cn.ExecuteAsync(
            """
            INSERT INTO SolicitudHistorial (SolicitudId, FechaHora, UsuarioId, Accion, Campo, ValorAnterior, ValorNuevo, Detalle)
            VALUES (@SolicitudId, @FechaHora, @UsuarioId, @Accion, @Campo, @ValorAnterior, @ValorNuevo, @Detalle)
            """,
            filas, tx);
    }
}
