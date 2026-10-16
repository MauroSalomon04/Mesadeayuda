namespace HelpDesk.Api.Modulos.Solicitudes;

public static class SolicitudesEndpoints
{
    public static void MapearSolicitudes(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/solicitudes");
        g.MapGet("", Listar);
        g.MapGet("/contadores", ObtenerContadores);
        g.MapGet("/proximo-id", ProximoId);
        g.MapGet("/{id:int}", Obtener);
        g.MapPost("", Crear);
        g.MapPatch("/{id:int}", Actualizar);
        g.MapPost("/{id:int}/resolver", Resolver);
        g.MapDelete("/{id:int}", Eliminar).RequireAuthorization(Politicas.Admin);
        g.MapPut("/{id:int}/fecha-ingreso", CorregirFecha).RequireAuthorization(Politicas.Admin);
    }

    private static async Task<IResult> Listar(HttpContext http, SolicitudesServicio servicio, CancellationToken ct)
    {
        var filtro = FiltroSolicitudes.DesdeQuery(http.Request.Query);
        return Results.Ok(await servicio.ListarAsync(filtro, http.Usuario(), ct));
    }

    private static async Task<IResult> ObtenerContadores(HttpContext http, SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.ContadoresAsync(http.Usuario(), ct));

    private static async Task<IResult> ProximoId(SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(new { id = await servicio.ProximoIdAsync(ct) });

    private static async Task<IResult> Obtener(int id, SolicitudesServicio servicio, CancellationToken ct)
    {
        var detalle = await servicio.ObtenerDetalleAsync(id, ct);
        return detalle is null
            ? Results.Json(new { mensaje = "La solicitud no existe o fue eliminada." }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(detalle);
    }

    private static async Task<IResult> Crear(SolicitudEntrada entrada, HttpContext http, SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.CrearAsync(entrada, http.Usuario(), ct));

    private static async Task<IResult> Actualizar(int id, ActualizacionEntrada entrada, HttpContext http,
        SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.ActualizarAsync(id, entrada, http.Usuario(), ct));

    private static async Task<IResult> Resolver(int id, ResolverEntrada? entrada, HttpContext http,
        SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.ResolverAsync(id, entrada ?? new ResolverEntrada(), http.Usuario(), ct));

    private static async Task<IResult> Eliminar(int id, HttpContext http, SolicitudesServicio servicio, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, http.Usuario(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CorregirFecha(int id, CorreccionFechaEntrada entrada, HttpContext http,
        SolicitudesServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.CorregirFechaIngresoAsync(id, entrada, http.Usuario(), ct));
}
