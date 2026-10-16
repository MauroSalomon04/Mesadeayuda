namespace HelpDesk.Api.Infraestructura;

public static class Roles
{
    public const string Admin = "ADMIN";
    public const string Soporte = "SOPORTE";

    public static bool EsValido(string? rol) => rol is Admin or Soporte;
}

/// <summary>Usuario autenticado de la solicitud en curso (se carga desde la base en cada request).</summary>
public sealed record UsuarioActual(
    int Id,
    string NombreUsuario,
    string Nombre,
    string Rol,
    int? ResponsableId,
    bool DebeCambiarPassword)
{
    public bool EsAdmin => Rol == Roles.Admin;
}

public static class ExtensionesUsuario
{
    public const string ClaveItems = "HelpDesk.UsuarioActual";

    /// <summary>Usuario autenticado. Lanza 401 si no hay sesión válida.</summary>
    public static UsuarioActual Usuario(this HttpContext contexto) =>
        contexto.Items[ClaveItems] as UsuarioActual
        ?? throw new ErrorApi(StatusCodes.Status401Unauthorized, "La sesión expiró. Volvé a ingresar.");

    public static UsuarioActual? UsuarioOpcional(this HttpContext contexto) =>
        contexto.Items[ClaveItems] as UsuarioActual;
}
