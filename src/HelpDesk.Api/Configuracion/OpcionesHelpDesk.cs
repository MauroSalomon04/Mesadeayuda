namespace HelpDesk.Api.Configuracion;

/// <summary>Opciones de la sección "HelpDesk" de appsettings.json.</summary>
public sealed class OpcionesHelpDesk
{
    public const string Seccion = "HelpDesk";

    /// <summary>Zona horaria en la que se registran las fechas (IANA o Windows).</summary>
    public string ZonaHoraria { get; set; } = "America/Montevideo";

    /// <summary>Aplica los scripts de /database pendientes al iniciar.</summary>
    public bool AplicarMigracionesAlIniciar { get; set; } = true;

    /// <summary>Crea la base de datos si no existe (requiere permiso CREATEDB en PostgreSQL).</summary>
    public bool CrearBaseSiNoExiste { get; set; }

    /// <summary>Horas de inactividad tras las cuales se cierra la sesión.</summary>
    public int HorasSesion { get; set; } = 12;

    /// <summary>
    /// Contraseña del usuario "admin" que se crea la primera vez.
    /// Si se deja vacía se genera una al azar y se muestra en el log.
    /// </summary>
    public string? PasswordAdminInicial { get; set; }

    /// <summary>Tamaño máximo del Excel a importar, en MB.</summary>
    public int TamanoMaximoImportacionMb { get; set; } = 30;

    /// <summary>Carpeta donde se guardan las claves de cifrado de la cookie de sesión.</summary>
    public string CarpetaClaves { get; set; } = "claves";

    /// <summary>
    /// Tomar la IP del usuario del encabezado X-Forwarded-For (solo cuando la aplicación
    /// está detrás de un proxy de confianza, como el contenedor "frontend" de Docker).
    /// </summary>
    public bool ConfiarEnProxy { get; set; }
}
