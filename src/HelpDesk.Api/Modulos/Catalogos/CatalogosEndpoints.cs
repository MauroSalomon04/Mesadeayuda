namespace HelpDesk.Api.Modulos.Catalogos;

public static class CatalogosEndpoints
{
    public static void MapearCatalogos(this RouteGroupBuilder api)
    {
        // Lectura para formularios y filtros (cualquier usuario autenticado).
        api.MapGet("/catalogos", ObtenerTodos);

        var admin = api.MapGroup("/admin").RequireAuthorization(Politicas.Admin);
        admin.MapGet("/catalogos/{clave}", Listar);
        admin.MapPost("/catalogos/{clave}", Crear);
        admin.MapPut("/catalogos/{clave}/{id:int}", Actualizar);

        admin.MapGet("/oficinas", ListarOficinas);
        admin.MapPut("/oficinas/{id:int}", ActualizarOficina);
        admin.MapPost("/oficinas/{id:int}/unificar", UnificarOficina);
    }

    private static async Task<IResult> ObtenerTodos(CatalogosRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.ObtenerTodosAsync(ct));

    private static async Task<IResult> Listar(string clave, CatalogosRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.ListarAsync(clave, ct));

    private static async Task<IResult> Crear(string clave, ItemCatalogoEntrada entrada, HttpContext http,
        CatalogosRepositorio repo, Reloj reloj, CancellationToken ct) =>
        Results.Ok(await repo.GuardarAsync(clave, null, entrada, http.Usuario(), reloj.Ahora(), ct));

    private static async Task<IResult> Actualizar(string clave, int id, ItemCatalogoEntrada entrada, HttpContext http,
        CatalogosRepositorio repo, Reloj reloj, CancellationToken ct) =>
        Results.Ok(await repo.GuardarAsync(clave, id, entrada, http.Usuario(), reloj.Ahora(), ct));

    private static async Task<IResult> ListarOficinas(string? q, OficinasRepositorio repo, CancellationToken ct) =>
        Results.Ok(await repo.ListarAsync(q, ct));

    private static async Task<IResult> ActualizarOficina(int id, OficinaEntrada entrada, HttpContext http,
        OficinasRepositorio repo, Reloj reloj, CancellationToken ct) =>
        Results.Ok(await repo.ActualizarAsync(id, entrada, http.Usuario(), reloj.Ahora(), ct));

    private static async Task<IResult> UnificarOficina(int id, UnificarOficinaEntrada entrada, HttpContext http,
        OficinasRepositorio repo, Reloj reloj, CancellationToken ct)
    {
        var movidas = await repo.UnificarAsync(id, entrada.DestinoId, http.Usuario(), reloj.Ahora(), ct);
        return Results.Ok(new { solicitudesMovidas = movidas });
    }
}
