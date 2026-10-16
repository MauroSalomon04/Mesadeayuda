namespace HelpDesk.Api.Datos;

/// <summary>Fábrica de conexiones a PostgreSQL (con pool de conexiones de Npgsql).</summary>
public sealed class BaseDatos : IDisposable
{
    private readonly NpgsqlDataSource _fuente;

    public BaseDatos(IConfiguration configuracion)
    {
        CadenaConexion = configuracion.GetConnectionString("HelpDesk")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:HelpDesk' (appsettings.json o variable de entorno ConnectionStrings__HelpDesk).");
        _fuente = NpgsqlDataSource.Create(CadenaConexion);
    }

    public string CadenaConexion { get; }

    public async Task<NpgsqlConnection> AbrirAsync(CancellationToken ct = default) =>
        await _fuente.OpenConnectionAsync(ct);

    public void Dispose() => _fuente.Dispose();
}
