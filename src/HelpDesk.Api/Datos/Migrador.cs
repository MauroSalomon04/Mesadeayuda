using System.Text.RegularExpressions;

namespace HelpDesk.Api.Datos;

/// <summary>
/// Aplica, en orden, los scripts de /database que todavía no se ejecutaron.
/// Lleva el control en la tabla dbo.VersionEsquema.
/// </summary>
public sealed partial class Migrador(BaseDatos db, IOptions<OpcionesHelpDesk> opciones, ILogger<Migrador> log)
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    [GeneratedRegex(@"^Scripts\.(\d+)_(.+)\.sql$", RegexOptions.IgnoreCase)]
    private static partial Regex NombreScript();

    [GeneratedRegex(@"/\*.*?\*/|--[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex Comentarios();

    private sealed record ScriptBd(int Version, string Nombre, string Contenido);

    public async Task EjecutarAsync(CancellationToken ct = default)
    {
        if (opciones.Value.CrearBaseSiNoExiste)
        {
            await CrearBaseSiNoExisteAsync(ct);
        }

        await using var cn = await db.AbrirAsync(ct);
        await cn.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL
                CREATE TABLE dbo.VersionEsquema (
                    Version    INT           NOT NULL CONSTRAINT PK_VersionEsquema PRIMARY KEY,
                    Nombre     NVARCHAR(200) NOT NULL,
                    AplicadaEn DATETIME2(0)  NOT NULL
                );
            """);

        var aplicadas = (await cn.QueryAsync<int>("SELECT Version FROM dbo.VersionEsquema")).ToHashSet();

        foreach (var script in LeerScripts())
        {
            if (aplicadas.Contains(script.Version)) continue;

            log.LogInformation("Aplicando script de base de datos {Version}: {Nombre}", script.Version, script.Nombre);
            using var tx = cn.BeginTransaction();
            foreach (var lote in SeparadorGo().Split(script.Contenido))
            {
                if (string.IsNullOrWhiteSpace(Comentarios().Replace(lote, ""))) continue;
                await cn.ExecuteAsync(new CommandDefinition(lote, transaction: tx, commandTimeout: 300, cancellationToken: ct));
            }
            await cn.ExecuteAsync(
                "INSERT INTO dbo.VersionEsquema (Version, Nombre, AplicadaEn) VALUES (@Version, @Nombre, SYSDATETIME())",
                new { script.Version, script.Nombre },
                tx);
            tx.Commit();
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

    private async Task CrearBaseSiNoExisteAsync(CancellationToken ct)
    {
        var constructor = new SqlConnectionStringBuilder(db.CadenaConexion);
        var nombreBase = constructor.InitialCatalog;
        if (string.IsNullOrWhiteSpace(nombreBase)) return;

        constructor.InitialCatalog = "master";
        await using var cn = new SqlConnection(constructor.ConnectionString);
        await cn.OpenAsync(ct);

        var existe = await cn.ExecuteScalarAsync<int?>("SELECT DB_ID(@nombre)", new { nombre = nombreBase });
        if (existe is not null) return;

        log.LogInformation("Creando la base de datos {Base}", nombreBase);
        await cn.ExecuteAsync(
            "DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@nombre) + N' COLLATE Modern_Spanish_CI_AS'; EXEC (@sql);",
            new { nombre = nombreBase });
    }
}
