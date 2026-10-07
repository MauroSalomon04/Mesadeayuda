namespace HelpDesk.Api.Modulos.Usuarios;

/// <summary>Fila de dbo.Usuario.</summary>
public sealed class UsuarioRegistro
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Rol { get; set; } = Roles.Soporte;
    public int? ResponsableId { get; set; }
    public bool Activo { get; set; }
    public bool DebeCambiarPassword { get; set; }
    public Guid SelloSeguridad { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}

/// <summary>Usuario tal como se muestra en la pantalla de administración.</summary>
public sealed class UsuarioVista
{
    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public int? ResponsableId { get; set; }
    public string? Responsable { get; set; }
    public bool Activo { get; set; }
    public bool DebeCambiarPassword { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}

public sealed class UsuarioEntrada
{
    public string? Usuario { get; set; }
    public string? Nombre { get; set; }
    public string? Rol { get; set; }
    public int? ResponsableId { get; set; }
    public bool? Activo { get; set; }
    public string? Password { get; set; }
}

public sealed class UsuariosRepositorio(BaseDatos db)
{
    private const string SelectVista = """
        SELECT u.Id, u.NombreUsuario AS Usuario, u.NombreCompleto AS Nombre, u.Rol, u.ResponsableId,
               r.Nombre AS Responsable, u.Activo, u.DebeCambiarPassword, u.CreadoEn, u.UltimoAcceso
        FROM dbo.Usuario u
        LEFT JOIN dbo.Responsable r ON r.Id = u.ResponsableId
        """;

    public async Task<UsuarioRegistro?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<UsuarioRegistro>("SELECT * FROM dbo.Usuario WHERE Id = @id", new { id });
    }

    public async Task<UsuarioRegistro?> ObtenerPorNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<UsuarioRegistro>(
            "SELECT * FROM dbo.Usuario WHERE NombreUsuario = @nombreUsuario", new { nombreUsuario });
    }

    public async Task RegistrarAccesoAsync(int id, DateTime ahora)
    {
        await using var cn = await db.AbrirAsync();
        await cn.ExecuteAsync("UPDATE dbo.Usuario SET UltimoAcceso = @ahora WHERE Id = @id", new { id, ahora });
    }

    public async Task<List<UsuarioVista>> ListarAsync(CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        return (await cn.QueryAsync<UsuarioVista>(SelectVista + " ORDER BY u.Activo DESC, u.NombreCompleto")).ToList();
    }

    public async Task<UsuarioVista> CrearAsync(UsuarioEntrada entrada, UsuarioActual admin, DateTime ahora, CancellationToken ct)
    {
        var nombreUsuario = Texto.LimpiarLinea(entrada.Usuario)?.ToLowerInvariant()
            ?? throw ErrorApi.Validacion("El nombre de usuario es obligatorio.");
        if (nombreUsuario.Length > 50 || nombreUsuario.Contains(' '))
            throw ErrorApi.Validacion("El nombre de usuario no puede tener espacios ni superar 50 caracteres.");
        var nombre = Texto.LimpiarLinea(entrada.Nombre) ?? throw ErrorApi.Validacion("El nombre completo es obligatorio.");
        var rol = (entrada.Rol ?? Roles.Soporte).ToUpperInvariant();
        if (!Roles.EsValido(rol)) throw ErrorApi.Validacion("Rol no válido.");
        var errorPassword = Contrasenas.Validar(entrada.Password);
        if (errorPassword is not null) throw ErrorApi.Validacion(errorPassword);

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        await ValidarResponsableAsync(cn, tx, entrada.ResponsableId);
        var id = await cn.ExecuteScalarAsync<int>(
            """
            INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, PasswordHash, Rol, ResponsableId, Activo, DebeCambiarPassword, CreadoEn)
            OUTPUT inserted.Id
            VALUES (@nombreUsuario, @nombre, @hash, @rol, @responsableId, 1, 1, @ahora)
            """,
            new { nombreUsuario, nombre, hash = Contrasenas.Hashear(entrada.Password!), rol, responsableId = entrada.ResponsableId, ahora },
            tx);
        await RegistroAuditoria.RegistrarAsync(cn, tx, admin.Id, ahora, "Usuario", id.ToString(CultureInfo.InvariantCulture), "CREADO",
            new { nombreUsuario, nombre, rol, entrada.ResponsableId });
        tx.Commit();
        return (await ObtenerVistaAsync(id, ct))!;
    }

    public async Task<UsuarioVista> ActualizarAsync(int id, UsuarioEntrada entrada, UsuarioActual admin, DateTime ahora, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var actual = await cn.QuerySingleOrDefaultAsync<UsuarioRegistro>(
            "SELECT * FROM dbo.Usuario WITH (UPDLOCK) WHERE Id = @id", new { id }, tx)
            ?? throw ErrorApi.NoEncontrado("El usuario no existe.");

        var nombre = Texto.LimpiarLinea(entrada.Nombre) ?? actual.NombreCompleto;
        var rol = (entrada.Rol ?? actual.Rol).ToUpperInvariant();
        if (!Roles.EsValido(rol)) throw ErrorApi.Validacion("Rol no válido.");
        var activo = entrada.Activo ?? actual.Activo;
        await ValidarResponsableAsync(cn, tx, entrada.ResponsableId);

        if (id == admin.Id && (!activo || rol != Roles.Admin))
            throw ErrorApi.Validacion("No podés quitarte el rol de administrador ni desactivar tu propio usuario.");

        var cambiaSeguridad = rol != actual.Rol || activo != actual.Activo;
        await cn.ExecuteAsync(
            $"""
            UPDATE dbo.Usuario
            SET NombreCompleto = @nombre, Rol = @rol, ResponsableId = @responsableId, Activo = @activo
                {(cambiaSeguridad ? ", SelloSeguridad = NEWID()" : "")}
            WHERE Id = @id
            """,
            new { id, nombre, rol, responsableId = entrada.ResponsableId, activo }, tx);

        var adminsActivos = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Usuario WHERE Rol = N'ADMIN' AND Activo = 1", transaction: tx);
        if (adminsActivos == 0) throw ErrorApi.Validacion("Tiene que quedar al menos un administrador activo.");

        await RegistroAuditoria.RegistrarAsync(cn, tx, admin.Id, ahora, "Usuario", id.ToString(CultureInfo.InvariantCulture), "MODIFICADO",
            new
            {
                anterior = new { actual.NombreCompleto, actual.Rol, actual.ResponsableId, actual.Activo },
                nuevo = new { NombreCompleto = nombre, Rol = rol, entrada.ResponsableId, Activo = activo }
            });
        tx.Commit();
        return (await ObtenerVistaAsync(id, ct))!;
    }

    /// <summary>El administrador asigna una contraseña temporal; el usuario debe cambiarla al ingresar.</summary>
    public async Task RestablecerPasswordAsync(int id, string? password, UsuarioActual admin, DateTime ahora, CancellationToken ct)
    {
        var error = Contrasenas.Validar(password);
        if (error is not null) throw ErrorApi.Validacion(error);

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var filas = await cn.ExecuteAsync(
            "UPDATE dbo.Usuario SET PasswordHash = @hash, DebeCambiarPassword = 1, SelloSeguridad = NEWID() WHERE Id = @id",
            new { id, hash = Contrasenas.Hashear(password!) }, tx);
        if (filas == 0) throw ErrorApi.NoEncontrado("El usuario no existe.");
        await RegistroAuditoria.RegistrarAsync(cn, tx, admin.Id, ahora, "Usuario", id.ToString(CultureInfo.InvariantCulture), "PASSWORD_RESTABLECIDA");
        tx.Commit();
    }

    /// <summary>El propio usuario cambia su contraseña. Devuelve el nuevo sello de seguridad.</summary>
    public async Task<UsuarioRegistro> CambiarPasswordPropiaAsync(int id, string? actual, string? nueva, DateTime ahora, CancellationToken ct)
    {
        var error = Contrasenas.Validar(nueva);
        if (error is not null) throw ErrorApi.Validacion(error);

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();
        var usuario = await cn.QuerySingleOrDefaultAsync<UsuarioRegistro>(
            "SELECT * FROM dbo.Usuario WITH (UPDLOCK) WHERE Id = @id", new { id }, tx)
            ?? throw ErrorApi.NoEncontrado("El usuario no existe.");
        if (!Contrasenas.Verificar(actual ?? "", usuario.PasswordHash))
            throw ErrorApi.Validacion("La contraseña actual no es correcta.");
        if (actual == nueva)
            throw ErrorApi.Validacion("La nueva contraseña tiene que ser distinta de la actual.");

        var sello = Guid.NewGuid();
        await cn.ExecuteAsync(
            "UPDATE dbo.Usuario SET PasswordHash = @hash, DebeCambiarPassword = 0, SelloSeguridad = @sello WHERE Id = @id",
            new { id, hash = Contrasenas.Hashear(nueva!), sello }, tx);
        await RegistroAuditoria.RegistrarAsync(cn, tx, id, ahora, "Usuario", id.ToString(CultureInfo.InvariantCulture), "PASSWORD_CAMBIADA");
        tx.Commit();

        usuario.SelloSeguridad = sello;
        usuario.DebeCambiarPassword = false;
        return usuario;
    }

    /// <summary>Crea el usuario "admin" la primera vez que arranca el sistema.</summary>
    public async Task AsegurarAdministradorInicialAsync(OpcionesHelpDesk opciones, Reloj reloj, ILogger log, string carpetaContenido)
    {
        await using var cn = await db.AbrirAsync();
        var hayUsuarios = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Usuario");
        if (hayUsuarios > 0) return;

        var password = string.IsNullOrWhiteSpace(opciones.PasswordAdminInicial)
            ? Contrasenas.Generar()
            : opciones.PasswordAdminInicial!;
        await cn.ExecuteAsync(
            """
            INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, PasswordHash, Rol, Activo, DebeCambiarPassword, CreadoEn)
            VALUES (N'admin', N'Administrador', @hash, N'ADMIN', 1, 1, @ahora)
            """,
            new { hash = Contrasenas.Hashear(password), ahora = reloj.Ahora() });

        var archivo = Path.Combine(carpetaContenido, "admin-password-inicial.txt");
        try
        {
            await File.WriteAllTextAsync(archivo,
                $"Usuario: admin{Environment.NewLine}Contraseña inicial: {password}{Environment.NewLine}" +
                $"El sistema pedirá cambiarla en el primer ingreso. Borrá este archivo después.{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo escribir {Archivo}", archivo);
        }

        log.LogWarning("Se creó el usuario 'admin' con la contraseña inicial: {Password} (también guardada en {Archivo})", password, archivo);
    }

    private async Task<UsuarioVista?> ObtenerVistaAsync(int id, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<UsuarioVista>(SelectVista + " WHERE u.Id = @id", new { id });
    }

    private static async Task ValidarResponsableAsync(IDbConnection cn, IDbTransaction tx, int? responsableId)
    {
        if (responsableId is not int rid) return;
        var existe = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Responsable WHERE Id = @rid", new { rid }, tx);
        if (existe == 0) throw ErrorApi.Validacion("El responsable indicado no existe.");
    }
}
