namespace HelpDesk.Api.Datos;

/// <summary>Fábrica de conexiones a SQL Server.</summary>
public sealed class BaseDatos
{
    public BaseDatos(IConfiguration configuracion)
    {
        CadenaConexion = configuracion.GetConnectionString("HelpDesk")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:HelpDesk' en appsettings.json.");
    }

    public string CadenaConexion { get; }

    public async Task<SqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync(ct);
        return conexion;
    }
}
