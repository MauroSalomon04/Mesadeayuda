using HelpDesk.Api.Modulos.Catalogos;
using HelpDesk.Api.Modulos.Conocimiento;
using HelpDesk.Api.Modulos.Estadisticas;
using HelpDesk.Api.Modulos.Exportacion;
using HelpDesk.Api.Modulos.Importacion;
using HelpDesk.Api.Modulos.Solicitudes;
using HelpDesk.Api.Modulos.TareasExtra;
using HelpDesk.Api.Modulos.Usuarios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Como servicio de Windows la carpeta de trabajo es System32: se usa la del ejecutable.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});

builder.Host.UseWindowsService(o => o.ServiceName = "HelpDeskAsse");

var opciones = builder.Configuration.GetSection(OpcionesHelpDesk.Seccion).Get<OpcionesHelpDesk>() ?? new OpcionesHelpDesk();
builder.Services.Configure<OpcionesHelpDesk>(builder.Configuration.GetSection(OpcionesHelpDesk.Seccion));

// Dapper: DATETIME2 en lugar de DATETIME para los parámetros de fecha.
SqlMapper.AddTypeMap(typeof(DateTime), DbType.DateTime2);
SqlMapper.AddTypeMap(typeof(DateTime?), DbType.DateTime2);

// ---------------------------------------------------------------- servicios
builder.Services.AddSingleton<BaseDatos>();
builder.Services.AddSingleton<Reloj>();
builder.Services.AddSingleton<Migrador>();
builder.Services.AddSingleton<LimitadorLogin>();
builder.Services.AddSingleton<CatalogosRepositorio>();
builder.Services.AddSingleton<OficinasRepositorio>();
builder.Services.AddSingleton<UsuariosRepositorio>();
builder.Services.AddSingleton<SolicitudesServicio>();
builder.Services.AddSingleton<ConocimientoServicio>();
builder.Services.AddSingleton<DashboardServicio>();
builder.Services.AddSingleton<ReportesServicio>();
builder.Services.AddSingleton<TareasExtraRepositorio>();
builder.Services.AddSingleton<ImportadorExcel>();
builder.Services.AddMemoryCache();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = (long)(opciones.TamanoMaximoImportacionMb + 5) * 1024 * 1024;
});

// Claves que cifran la cookie de sesión: se guardan en disco para que las sesiones
// sobrevivan a un reinicio del servicio.
var carpetaClaves = Path.IsPathRooted(opciones.CarpetaClaves)
    ? opciones.CarpetaClaves
    : Path.Combine(builder.Environment.ContentRootPath, opciones.CarpetaClaves);
var proteccion = builder.Services.AddDataProtection()
    .SetApplicationName("HelpDeskAsse")
    .PersistKeysToFileSystem(new DirectoryInfo(carpetaClaves));
if (OperatingSystem.IsWindows())
{
    proteccion.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "HelpDeskAsse.Sesion";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(Math.Max(1, opciones.HorasSesion));
        o.SlidingExpiration = true;
        // Es una API: en lugar de redirigir a una página de login, responde 401/403.
        o.Events.OnRedirectToLogin = contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // En cada request se verifica que el usuario siga activo y que su contraseña/rol no hayan cambiado.
        o.Events.OnValidatePrincipal = async contexto =>
        {
            var idTexto = contexto.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var sello = contexto.Principal?.FindFirst("sello")?.Value;
            if (!int.TryParse(idTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || sello is null)
            {
                contexto.RejectPrincipal();
                return;
            }

            var repositorio = contexto.HttpContext.RequestServices.GetRequiredService<UsuariosRepositorio>();
            var usuario = await repositorio.ObtenerAsync(id);
            if (usuario is null || !usuario.Activo ||
                !string.Equals(usuario.SelloSeguridad.ToString(), sello, StringComparison.OrdinalIgnoreCase))
            {
                contexto.RejectPrincipal();
                await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            contexto.HttpContext.Items[ExtensionesUsuario.ClaveItems] = new UsuarioActual(
                usuario.Id, usuario.NombreUsuario, usuario.NombreCompleto, usuario.Rol,
                usuario.ResponsableId, usuario.DebeCambiarPassword);
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Politicas.Admin, politica => politica.RequireAuthenticatedUser().RequireRole(Roles.Admin));
});

var app = builder.Build();

// ---------------------------------------------------------------- base de datos
using (var alcance = app.Services.CreateScope())
{
    var log = alcance.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Inicio");
    try
    {
        if (opciones.AplicarMigracionesAlIniciar)
        {
            await alcance.ServiceProvider.GetRequiredService<Migrador>().EjecutarAsync();
        }
        await alcance.ServiceProvider.GetRequiredService<UsuariosRepositorio>().AsegurarAdministradorInicialAsync(
            opciones, alcance.ServiceProvider.GetRequiredService<Reloj>(), log, app.Environment.ContentRootPath);
    }
    catch (SqlException ex)
    {
        log.LogCritical(ex,
            "No se pudo conectar o preparar la base de datos SQL Server. Revisá 'ConnectionStrings:HelpDesk' en appsettings.json. Detalle: {Mensaje}",
            ex.Message);
        throw;
    }
}

// ---------------------------------------------------------------- pipeline
app.UseMiddleware<ManejoErroresMiddleware>();

app.Use(async (contexto, siguiente) =>
{
    var encabezados = contexto.Response.Headers;
    encabezados["X-Content-Type-Options"] = "nosniff";
    encabezados["X-Frame-Options"] = "DENY";
    encabezados["Referrer-Policy"] = "no-referrer";
    encabezados["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    encabezados["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; " +
        "connect-src 'self'; font-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (contexto.Request.Path.StartsWithSegments("/api"))
    {
        encabezados["Cache-Control"] = "no-store";
    }
    await siguiente();
});

var archivosEstaticos = new StaticFileOptions
{
    ContentTypeProvider = new FileExtensionContentTypeProvider(),
    OnPrepareResponse = contexto =>
    {
        var ruta = contexto.Context.Request.Path.Value ?? "";
        contexto.Context.Response.Headers["Cache-Control"] =
            ruta.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(archivosEstaticos);

app.UseAuthentication();

// Protección CSRF (además de la cookie SameSite=Strict) y contraseña temporal obligatoria.
app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.StartsWithSegments("/api"))
    {
        var metodo = contexto.Request.Method;
        var esLectura = HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo);
        if (!esLectura && contexto.Request.Headers["X-HelpDesk"] != "1")
        {
            contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
            await contexto.Response.WriteAsJsonAsync(new { mensaje = "Solicitud rechazada (falta el encabezado de seguridad)." });
            return;
        }

        if (contexto.UsuarioOpcional() is { DebeCambiarPassword: true } &&
            !contexto.Request.Path.StartsWithSegments("/api/auth"))
        {
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            await contexto.Response.WriteAsJsonAsync(new
            {
                mensaje = "Tenés que cambiar tu contraseña antes de continuar.",
                datos = new { codigo = "debe_cambiar_password" },
            });
            return;
        }
    }
    await siguiente();
});

app.UseAuthorization();

// ---------------------------------------------------------------- endpoints
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/salud", () => Results.Ok(new { estado = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }))
    .AllowAnonymous();
api.MapearAuth();
api.MapearCatalogos();
api.MapearSolicitudes();
api.MapearConocimiento();
api.MapearEstadisticas();
api.MapearTareasExtra();
api.MapearImportacion();
api.MapearExportacion();
// Cualquier otra ruta /api/... inexistente devuelve 404 (y no la página de la aplicación).
api.MapFallback(() => Results.Json(new { mensaje = "Recurso no encontrado." }, statusCode: StatusCodes.Status404NotFound))
    .AllowAnonymous();

// La aplicación web (React) para todo lo demás.
app.MapFallbackToFile("index.html", archivosEstaticos);

app.Run();
