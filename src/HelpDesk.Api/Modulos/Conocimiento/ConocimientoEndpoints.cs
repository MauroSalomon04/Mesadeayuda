namespace HelpDesk.Api.Modulos.Conocimiento;

public static class ConocimientoEndpoints
{
    public static void MapearConocimiento(this RouteGroupBuilder api)
    {
        api.MapGet("/conocimiento/similares", Similares);
        api.MapGet("/sugerencias/funcionarios", Funcionarios);
        api.MapGet("/sugerencias/oficinas", Oficinas);
        api.MapGet("/sugerencias/descripciones", Descripciones);
    }

    private static async Task<IResult> Similares(string? texto, int? excluir, int? limite, ConocimientoServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.BuscarSimilaresAsync(texto, excluir, limite ?? 20, ct));

    private static async Task<IResult> Funcionarios(string? q, ConocimientoServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.SugerirFuncionariosAsync(q, ct));

    private static async Task<IResult> Oficinas(string? q, ConocimientoServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.SugerirOficinasAsync(q, ct));

    private static async Task<IResult> Descripciones(string? q, ConocimientoServicio servicio, CancellationToken ct) =>
        Results.Ok(await servicio.SugerirDescripcionesAsync(q, ct));
}
