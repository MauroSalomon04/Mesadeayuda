using System.Text.RegularExpressions;

namespace HelpDesk.Api.Datos;

/// <summary>
/// Aplica, en orden, los scripts de /database que todavía no se ejecutaron.
/// Lleva el control en la tabla VersionEsquema.
/// Con Docker, el contenedor de PostgreSQL ya ejecuta esos scripts al crear la base
/// (y los registra en VersionEsquema), así que aquí se omiten.
/// </summary>
public sealed partial class Migrador(BaseDatos db, IOptions<OpcionesHelpDesk> opciones, ILogger<Migrador> log)
{
    [GeneratedRegex(@"^Scripts\.(\d+)_(.+)\.sql$", RegexOptions.IgnoreCase)]
    private static partial Regex NombreScript();

    [GeneratedRegex(@"/\*.*?\*/|--[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex Comentarios();

    // Clave fija para que dos instancias no apliquen scripts al mismo tiempo.
    private const long ClaveBloqueo = 4_815_162_342;

    private sealed record ScriptBd(int Version, string Nombre, string Contenido);

    public async Task EjecutarAsync(CancellationToken ct = default)
    {
        if (opciones.Value.CrearBaseSiNoExiste)
        {
            await CrearBaseSiNoExisteAsync(ct);
        }

        await using var cn = await db.AbrirAsync(ct);
        await cn.ExecuteAsync("SELECT pg_advisory_lock(@clave)", new { clave = ClaveBloqueo });
        try
        {
            await cn.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS VersionEsquema (
                    Version    INTEGER      NOT NULL CONSTRAINT PK_VersionEsquema PRIMARY KEY,
                    Nombre     VARCHAR(200) NOT NULL,
                    AplicadaEn TIMESTAMP(0) NOT NULL
                );
                """);

            var aplicadas = (await cn.QueryAsync<int>("SELECT Version FROM VersionEsquema")).ToHashSet();

            foreach (var script in LeerScripts())
            {
                if (aplicadas.Contains(script.Version)) continue;
                if (string.IsNullOrWhiteSpace(Comentarios().Replace(script.Contenido, ""))) continue;

                log.LogInformation("Aplicando script de base de datos {Version}: {Nombre}", script.Version, script.Nombre);
                await using var tx = await cn.BeginTransactionAsync(ct);
                await cn.ExecuteAsync(new CommandDefinition(script.Contenido, transaction: tx, commandTimeout: 300, cancellationToken: ct));
                await cn.ExecuteAsync(
                    """
                    INSERT INTO VersionEsquema (Version, Nombre, AplicadaEn)
                    VALUES (@Version, @Nombre, LOCALTIMESTAMP(0))
                    ON CONFLICT (Version) DO NOTHING
                    """,
                    new { script.Version, script.Nombre },
                    tx);
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            await cn.ExecuteAsync("SELECT pg_advisory_unlock(@clave)", new { clave = ClaveBloqueo });
        }
    }

    private static List<ScriptBd> LeerScripts()
    {
        var ensamblado = typeof(Migrador).Assembly;
        var scripts = new List<ScriptBd>();
        foreach (var recurso in ensamblado.GetManifestResourceNames())
        {
            var coincidencia = NombreScript().Match(recurso);
            if (!coincidencia.Success) continue;

            using var flujo = ensamblado.GetManifestResourceStream(recurso)
                ?? throw new InvalidOperationException($"No se pudo leer el recurso {recurso}.");
            using var lector = new StreamReader(flujo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            scripts.Add(new ScriptBd(
                int.Parse(coincidencia.Groups[1].Value, CultureInfo.InvariantCulture),
                coincidencia.Groups[2].Value,
                lector.ReadToEnd()));
        }
        return scripts.OrderBy(s => s.Version).ToList();
    }

    /// <summary>Crea la base si no existe, conectándose a la base de mantenimiento "postgres".</summary>
    private async Task CrearBaseSiNoExisteAsync(CancellationToken ct)
    {
        var constructor = new NpgsqlConnectionStringBuilder(db.CadenaConexion);
        var nombreBase = constructor.Database;
        if (string.IsNullOrWhiteSpace(nombreBase)) return;

        constructor.Database = "postgres";
        constructor.Pooling = false;
        await using var cn = new NpgsqlConnection(constructor.ConnectionString);
        await cn.OpenAsync(ct);

        var existe = await cn.ExecuteScalarAsync<int?>("SELECT 1 FROM pg_database WHERE datname = @nombre", new { nombre = nombreBase });
        if (existe is not null) return;

        log.LogInformation("Creando la base de datos {Base}", nombreBase);
        var identificador = "\"" + nombreBase.Replace("\"", "\"\"") + "\"";
        await cn.ExecuteAsync($"CREATE DATABASE {identificador} ENCODING 'UTF8' TEMPLATE template0");
    }
}
