using Microsoft.Extensions.Caching.Memory;

namespace HelpDesk.Api.Modulos.Importacion;

public sealed class ConfirmacionEntrada
{
    public string? Token { get; set; }
}

public sealed class ImportacionHistorica
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }
    public string? Usuario { get; set; }
    public string NombreArchivo { get; set; } = "";
    public int FilasLeidas { get; set; }
    public int SolicitudesNuevas { get; set; }
    public int SolicitudesExistentes { get; set; }
    public int FilasConError { get; set; }
    public int FilasConAdvertencia { get; set; }
    public int TareasExtraNuevas { get; set; }
}

public static class ImportacionEndpoints
{
    private const int MaximoIncidenciasEnPantalla = 1000;
    private static readonly TimeSpan VigenciaPrevisualizacion = TimeSpan.FromMinutes(30);

    private sealed record PrevisualizacionGuardada(int UsuarioId, AnalisisImportacion Analisis);

    public static void MapearImportacion(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/importacion").RequireAuthorization(Politicas.Admin);
        g.MapPost("/previsualizar", Previsualizar);
        g.MapPost("/confirmar", Confirmar);
        g.MapGet("", Historial);
    }

    /// <summary>
    /// Recibe el archivo como cuerpo binario (application/octet-stream) y el nombre en ?nombre=.
    /// Devuelve el resumen sin guardar nada.
    /// </summary>
    private static async Task<IResult> Previsualizar(HttpContext http, ImportadorExcel importador, IMemoryCache cache,
        IOptions<OpcionesHelpDesk> opciones, CancellationToken ct)
    {
        var usuario = http.Usuario();
        var maximo = (long)opciones.Value.TamanoMaximoImportacionMb * 1024 * 1024;
        if (http.Request.ContentLength is long largo && largo > maximo)
            throw ErrorApi.Validacion($"El archivo supera el máximo de {opciones.Value.TamanoMaximoImportacionMb} MB.");

        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            memoria.Write(buffer, 0, leidos);
            if (memoria.Length > maximo)
                throw ErrorApi.Validacion($"El archivo supera el máximo de {opciones.Value.TamanoMaximoImportacionMb} MB.");
        }
        if (memoria.Length == 0) throw ErrorApi.Validacion("No se recibió ningún archivo.");

        var nombre = Path.GetFileName((string?)http.Request.Query["nombre"] ?? "archivo.xlsx");
        var analisis = await importador.AnalizarAsync(memoria.ToArray(), nombre, ct);

        var token = Guid.NewGuid().ToString("N");
        cache.Set($"importacion:{token}", new PrevisualizacionGuardada(usuario.Id, analisis), VigenciaPrevisualizacion);

        var nuevas = analisis.Solicitudes.Where(s => !s.Existente).ToList();
        var ids = analisis.Solicitudes.Select(s => s.Id).ToList();
        var filasConError = analisis.Incidencias.Where(i => i.Tipo == "error").Select(i => (i.Hoja, i.Fila)).Distinct().Count();

        return Results.Ok(new
        {
            token,
            nombreArchivo = analisis.NombreArchivo,
            hojaSolicitudes = analisis.HojaSolicitudes,
            hojaTareas = analisis.HojaTareas,
            solicitudes = new
            {
                filasLeidas = analisis.FilasLeidas,
                validas = analisis.Solicitudes.Count,
                nuevas = nuevas.Count,
                existentes = analisis.Solicitudes.Count - nuevas.Count,
                conError = filasConError,
                conAdvertencia = analisis.Solicitudes.Count(s => s.Advertencias.Count > 0),
                idMinimo = ids.Count > 0 ? ids.Min() : (int?)null,
                idMaximo = ids.Count > 0 ? ids.Max() : (int?)null,
                pendientes = nuevas.Count(s => s.Estado is not null && !Texto.Normalizar(s.Estado).StartsWith("resuelt", StringComparison.Ordinal)),
            },
            tareasExtra = new
            {
                leidas = analisis.Tareas.Count,
                nuevas = analisis.Tareas.Count(t => !t.Existente),
                existentes = analisis.Tareas.Count(t => t.Existente),
            },
            catalogosNuevos = analisis.CatalogosNuevos,
            incidencias = analisis.Incidencias.Take(MaximoIncidenciasEnPantalla),
            totalIncidencias = analisis.Incidencias.Count,
            muestra = nuevas.OrderByDescending(s => s.Id).Take(5).Select(s => new
            {
                s.Id,
                s.FechaIngreso,
                s.NombreFuncionario,
                s.Oficina,
                s.Descripcion,
                s.Responsable,
                s.Estado,
            }),
        });
    }

    private static async Task<IResult> Confirmar(ConfirmacionEntrada entrada, HttpContext http, ImportadorExcel importador,
        IMemoryCache cache, CancellationToken ct)
    {
        var usuario = http.Usuario();
        var clave = $"importacion:{entrada.Token}";
        if (string.IsNullOrWhiteSpace(entrada.Token) ||
            !cache.TryGetValue(clave, out PrevisualizacionGuardada? guardada) ||
            guardada is null || guardada.UsuarioId != usuario.Id)
        {
            throw ErrorApi.Validacion("La previsualización expiró o no es válida. Volvé a cargar el archivo.");
        }

        cache.Remove(clave);
        var resultado = await importador.ConfirmarAsync(guardada.Analisis, usuario, ct);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> Historial(BaseDatos db, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        var filas = await cn.QueryAsync<ImportacionHistorica>(
            """
            SELECT TOP (50) i.Id, i.FechaHora, u.NombreCompleto AS Usuario, i.NombreArchivo, i.FilasLeidas, i.SolicitudesNuevas,
                   i.SolicitudesExistentes, i.FilasConError, i.FilasConAdvertencia, i.TareasExtraNuevas
            FROM dbo.Importacion i
            LEFT JOIN dbo.Usuario u ON u.Id = i.UsuarioId
            ORDER BY i.Id DESC
            """);
        return Results.Ok(filas);
    }
}
