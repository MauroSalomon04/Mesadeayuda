namespace HelpDesk.Api.Infraestructura;

/// <summary>
/// Error controlado que se devuelve al navegador como JSON { mensaje, datos }.
/// </summary>
public sealed class ErrorApi(int estado, string mensaje, object? datos = null) : Exception(mensaje)
{
    public int Estado { get; } = estado;
    public object? Datos { get; } = datos;

    public static ErrorApi Validacion(string mensaje) => new(StatusCodes.Status400BadRequest, mensaje);
    public static ErrorApi NoEncontrado(string mensaje = "No se encontró el registro.") => new(StatusCodes.Status404NotFound, mensaje);
    public static ErrorApi Conflicto(string mensaje, object? datos = null) => new(StatusCodes.Status409Conflict, mensaje, datos);
    public static ErrorApi Prohibido(string mensaje = "No tenés permiso para realizar esta acción.") => new(StatusCodes.Status403Forbidden, mensaje);
}

/// <summary>Convierte excepciones en respuestas JSON legibles.</summary>
public sealed class ManejoErroresMiddleware(RequestDelegate siguiente, ILogger<ManejoErroresMiddleware> log)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await siguiente(contexto);
        }
        catch (ErrorApi ex)
        {
            await EscribirAsync(contexto, ex.Estado, ex.Message, ex.Datos);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await EscribirAsync(contexto, StatusCodes.Status409Conflict, "Ya existe un registro con ese nombre.", null);
        }
        catch (BadHttpRequestException ex)
        {
            await EscribirAsync(contexto, ex.StatusCode, "La solicitud enviada no es válida.", null);
        }
        catch (JsonException)
        {
            await EscribirAsync(contexto, StatusCodes.Status400BadRequest, "El formato de los datos enviados no es válido.", null);
        }
        catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
        {
            // El navegador canceló la solicitud: no hay nada que responder.
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error no controlado en {Metodo} {Ruta}", contexto.Request.Method, contexto.Request.Path);
            await EscribirAsync(contexto, StatusCodes.Status500InternalServerError,
                "Ocurrió un error inesperado. Si se repite, avisá al administrador.", null);
        }
    }

    private static async Task EscribirAsync(HttpContext contexto, int estado, string mensaje, object? datos)
    {
        if (contexto.Response.HasStarted) return;
        contexto.Response.Clear();
        contexto.Response.StatusCode = estado;
        await contexto.Response.WriteAsJsonAsync(new { mensaje, datos });
    }
}
