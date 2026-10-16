using HelpDesk.Api.Modulos.Solicitudes;

namespace HelpDesk.Api.Modulos.Estadisticas;

public static class EstadisticasEndpoints
{
    public static void MapearEstadisticas(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", Dashboard);

        var reportes = api.MapGroup("/reportes").RequireAuthorization(Politicas.Admin);
        reportes.MapGet("/dimensiones", () => Results.Ok(ReportesServicio.DimensionesDisponibles()));
        reportes.MapGet("", Reporte);
    }

    private static async Task<IResult> Dashboard(string? periodo, DashboardServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.ObtenerAsync(periodo, ct));

    private static async Task<IResult> Reporte(HttpContext http, ReportesServicio servicio, CancellationToken ct)
    {
        var q = http.Request.Query;
        var filtro = FiltroSolicitudes.DesdeQuery(q);
        return Results.Ok(await servicio.GenerarAsync(q["dimension"], q["dimension2"], filtro, http.Usuario(), ct));
    }
}
