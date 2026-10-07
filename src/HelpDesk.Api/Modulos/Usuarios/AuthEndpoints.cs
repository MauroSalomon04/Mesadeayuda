using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HelpDesk.Api.Modulos.Usuarios;

public sealed class LoginEntrada
{
    public string? Usuario { get; set; }
    public string? Password { get; set; }
}

public sealed class CambioPasswordEntrada
{
    public string? Actual { get; set; }
    public string? Nueva { get; set; }
}

public sealed class RestablecerPasswordEntrada
{
    public string? Password { get; set; }
}

/// <summary>Datos de la sesión que usa la interfaz.</summary>
public sealed record SesionDto(int Id, string Usuario, string Nombre, string Rol, int? ResponsableId, bool DebeCambiarPassword);

public static class AuthEndpoints
{
    public static void MapearAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", Login).AllowAnonymous();
        auth.MapPost("/logout", Logout).AllowAnonymous();
        auth.MapGet("/yo", Yo);
        auth.MapPost("/cambiar-password", CambiarPassword);

        var admin = api.MapGroup("/admin/usuarios").RequireAuthorization(Politicas.Admin);
        admin.MapGet("", ListarUsuarios);
        admin.MapPost("", CrearUsuario);
        admin.MapPut("/{id:int}", ActualizarUsuario);
        admin.MapPost("/{id:int}/restablecer-password", RestablecerPassword);
    }

    public static async Task IniciarSesionAsync(HttpContext http, UsuarioRegistro usuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, usuario.NombreUsuario),
            new(ClaimTypes.Role, usuario.Rol),
            new("sello", usuario.SelloSeguridad.ToString()),
        };
        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties { IsPersistent = false });
    }

    private static SesionDto ADto(UsuarioRegistro u) =>
        new(u.Id, u.NombreUsuario, u.NombreCompleto, u.Rol, u.ResponsableId, u.DebeCambiarPassword);

    private static async Task<IResult> Login(LoginEntrada entrada, HttpContext http, UsuariosRepositorio repo,
        LimitadorLogin limitador, Reloj reloj, CancellationToken ct)
    {
        var nombreUsuario = (entrada.Usuario ?? "").Trim().ToLowerInvariant();
        var clave = $"{nombreUsuario}|{http.Connection.RemoteIpAddress}";
        if (limitador.EstaBloqueado(clave, out var espera))
        {
            var minutos = Math.Max(1, (int)Math.Ceiling(espera.TotalMinutes));
            return Results.Json(new { mensaje = $"Demasiados intentos fallidos. Probá de nuevo en {minutos} min." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var usuario = nombreUsuario.Length == 0 ? null : await repo.ObtenerPorNombreUsuarioAsync(nombreUsuario, ct);
        var valido = Contrasenas.Verificar(entrada.Password ?? "", usuario?.PasswordHash);
        if (usuario is null || !valido || !usuario.Activo)
        {
            limitador.RegistrarFallo(clave);
            return Results.Json(new { mensaje = "Usuario o contraseña incorrectos." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        limitador.Limpiar(clave);
        await repo.RegistrarAccesoAsync(usuario.Id, reloj.Ahora());
        await IniciarSesionAsync(http, usuario);
        return Results.Ok(ADto(usuario));
    }

    private static async Task<IResult> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static IResult Yo(HttpContext http)
    {
        var u = http.Usuario();
        return Results.Ok(new SesionDto(u.Id, u.NombreUsuario, u.Nombre, u.Rol, u.ResponsableId, u.DebeCambiarPassword));
    }

    private static async Task<IResult> CambiarPassword(CambioPasswordEntrada entrada, HttpContext http,
        UsuariosRepositorio repo, Reloj reloj, CancellationToken ct)
    {
        var u = http.Usuario();
        var actualizado = await repo.CambiarPasswordPropiaAsync(u.Id, entrada.Actual, entrada.Nueva, reloj.Ahora(), ct);
        // El sello cambió: se vuelve a emitir la cookie para no cerrar la sesión actual.
        await IniciarSesionAsync(http, actualizado);
        return Results.Ok(ADto(actualizado));
    }

    private static async Task<IResult> ListarUsuarios(UsuariosRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.ListarAsync(ct));

    private static async Task<IResult> CrearUsuario(UsuarioEntrada entrada, HttpContext http, UsuariosRepositorio repo,
        Reloj reloj, CancellationToken ct) =>
        Results.Ok(await repo.CrearAsync(entrada, http.Usuario(), reloj.Ahora(), ct));

    private static async Task<IResult> ActualizarUsuario(int id, UsuarioEntrada entrada, HttpContext http,
        UsuariosRepositorio repo, Reloj reloj, CancellationToken ct) =>
        Results.Ok(await repo.ActualizarAsync(id, entrada, http.Usuario(), reloj.Ahora(), ct));

    private static async Task<IResult> RestablecerPassword(int id, RestablecerPasswordEntrada entrada, HttpContext http,
        UsuariosRepositorio repo, Reloj reloj, CancellationToken ct)
    {
        await repo.RestablecerPasswordAsync(id, entrada.Password, http.Usuario(), reloj.Ahora(), ct);
        return Results.NoContent();
    }
}
