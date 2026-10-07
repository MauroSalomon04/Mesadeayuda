namespace HelpDesk.Api.Modulos.Catalogos;

/// <summary>Elemento de cualquier catálogo (responsables, medios, tipos, estados, prioridades, áreas).</summary>
public sealed class ItemCatalogo
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
    public int Orden { get; set; }
    public bool Predeterminado { get; set; }
    public bool? EsResuelto { get; set; }
    public string? Color { get; set; }
    public int? Nivel { get; set; }
    public string? Descripcion { get; set; }
    public int? Usos { get; set; }
}

public sealed class ItemCatalogoEntrada
{
    public string? Nombre { get; set; }
    public bool? Activo { get; set; }
    public int? Orden { get; set; }
    public bool? Predeterminado { get; set; }
    public bool? EsResuelto { get; set; }
    public string? Color { get; set; }
    public int? Nivel { get; set; }
    public string? Descripcion { get; set; }
}

public sealed class CatalogosCompletos
{
    public List<ItemCatalogo> Responsables { get; set; } = [];
    public List<ItemCatalogo> Medios { get; set; } = [];
    public List<ItemCatalogo> Tipos { get; set; } = [];
    public List<ItemCatalogo> Estados { get; set; } = [];
    public List<ItemCatalogo> Prioridades { get; set; } = [];
    public List<ItemCatalogo> Areas { get; set; } = [];
}

/// <summary>Catálogos indexados por Id, para validar y armar el historial.</summary>
public sealed class MapasCatalogo
{
    public Dictionary<int, ItemCatalogo> Responsables { get; init; } = new();
    public Dictionary<int, ItemCatalogo> Medios { get; init; } = new();
    public Dictionary<int, ItemCatalogo> Tipos { get; init; } = new();
    public Dictionary<int, ItemCatalogo> Estados { get; init; } = new();
    public Dictionary<int, ItemCatalogo> Prioridades { get; init; } = new();

    public static string? Nombre(Dictionary<int, ItemCatalogo> mapa, int? id) =>
        id is int valor && mapa.TryGetValue(valor, out var item) ? item.Nombre : null;

    public bool EsResuelto(int? estadoId) =>
        estadoId is int id && Estados.TryGetValue(id, out var estado) && estado.EsResuelto == true;
}

/// <summary>Describe la tabla física de cada catálogo (lista blanca: nunca viene del usuario).</summary>
public sealed record DefinicionCatalogo(
    string Clave,
    string Tabla,
    string ColumnaNombre,
    int LargoNombre,
    string Etiqueta,
    bool TienePredeterminado,
    bool TieneEsResuelto,
    bool TieneColor,
    bool TieneNivel,
    bool TieneDescripcion,
    string ConsultaUsos)
{
    public static readonly IReadOnlyDictionary<string, DefinicionCatalogo> Todas =
        new Dictionary<string, DefinicionCatalogo>(StringComparer.OrdinalIgnoreCase)
        {
            ["responsables"] = new("responsables", "Responsable", "Nombre", 100, "Responsable", false, false, false, false, false,
                "(SELECT COUNT(*) FROM dbo.Solicitud x WHERE x.ResponsableId = c.Id)"),
            ["medios"] = new("medios", "MedioContacto", "Nombre", 60, "Medio de contacto", true, false, false, false, false,
                "(SELECT COUNT(*) FROM dbo.Solicitud x WHERE x.MedioContactoId = c.Id)"),
            ["tipos"] = new("tipos", "TipoSolicitud", "Codigo", 30, "Tipo de solicitud", false, false, false, false, true,
                "(SELECT COUNT(*) FROM dbo.Solicitud x WHERE x.TipoSolicitudId = c.Id)"),
            ["estados"] = new("estados", "Estado", "Nombre", 60, "Estado", true, true, true, false, false,
                "(SELECT COUNT(*) FROM dbo.Solicitud x WHERE x.EstadoId = c.Id)"),
            ["prioridades"] = new("prioridades", "Prioridad", "Nombre", 30, "Prioridad", true, false, false, true, false,
                "(SELECT COUNT(*) FROM dbo.Solicitud x WHERE x.PrioridadId = c.Id)"),
            ["areas"] = new("areas", "AreaAsse", "Nombre", 150, "Área de ASSE", false, false, false, false, false,
                "(SELECT COUNT(*) FROM dbo.TareaExtra x WHERE x.AreaAsseId = c.Id AND x.EliminadoEn IS NULL)"),
        };

    public string SqlSelect(bool incluirUsos) =>
        $"""
        SELECT c.Id,
               c.{ColumnaNombre} AS Nombre,
               c.Activo,
               c.Orden,
               {(TienePredeterminado ? "c.Predeterminado" : "CAST(0 AS BIT) AS Predeterminado")},
               {(TieneEsResuelto ? "c.EsResuelto" : "CAST(NULL AS BIT) AS EsResuelto")},
               {(TieneColor ? "c.Color" : "CAST(NULL AS NVARCHAR(20)) AS Color")},
               {(TieneNivel ? "c.Nivel" : "CAST(NULL AS INT) AS Nivel")},
               {(TieneDescripcion ? "c.Descripcion" : "CAST(NULL AS NVARCHAR(200)) AS Descripcion")},
               {(incluirUsos ? ConsultaUsos : "CAST(NULL AS INT)")} AS Usos
        FROM dbo.{Tabla} c
        """;

    public string SqlOrden => $" ORDER BY c.Orden, c.{ColumnaNombre}";
}

public sealed class CatalogosRepositorio(BaseDatos db)
{
    public static readonly string[] ColoresValidos = ["verde", "naranja", "ambar", "rojo", "azul", "violeta", "gris"];

    private static DefinicionCatalogo Def(string clave) => DefinicionCatalogo.Todas[clave];

    public async Task<CatalogosCompletos> ObtenerTodosAsync(CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);
        var sql = string.Join(";\n", new[] { "responsables", "medios", "tipos", "estados", "prioridades", "areas" }
            .Select(c => Def(c).SqlSelect(false) + Def(c).SqlOrden));
        using var multi = await cn.QueryMultipleAsync(sql);
        return new CatalogosCompletos
        {
            Responsables = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
            Medios = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
            Tipos = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
            Estados = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
            Prioridades = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
            Areas = (await multi.ReadAsync<ItemCatalogo>()).ToList(),
        };
    }

    /// <summary>Carga los catálogos de solicitudes dentro de una transacción existente.</summary>
    public static async Task<MapasCatalogo> CargarMapasAsync(IDbConnection cn, IDbTransaction? tx)
    {
        var sql = string.Join(";\n", new[] { "responsables", "medios", "tipos", "estados", "prioridades" }
            .Select(c => Def(c).SqlSelect(false)));
        using var multi = await cn.QueryMultipleAsync(sql, transaction: tx);
        return new MapasCatalogo
        {
            Responsables = (await multi.ReadAsync<ItemCatalogo>()).ToDictionary(i => i.Id),
            Medios = (await multi.ReadAsync<ItemCatalogo>()).ToDictionary(i => i.Id),
            Tipos = (await multi.ReadAsync<ItemCatalogo>()).ToDictionary(i => i.Id),
            Estados = (await multi.ReadAsync<ItemCatalogo>()).ToDictionary(i => i.Id),
            Prioridades = (await multi.ReadAsync<ItemCatalogo>()).ToDictionary(i => i.Id),
        };
    }

    public async Task<List<ItemCatalogo>> ListarAsync(string clave, CancellationToken ct)
    {
        var def = ObtenerDefinicion(clave);
        await using var cn = await db.AbrirAsync(ct);
        return (await cn.QueryAsync<ItemCatalogo>(def.SqlSelect(true) + def.SqlOrden)).ToList();
    }

    public async Task<ItemCatalogo> GuardarAsync(string clave, int? id, ItemCatalogoEntrada entrada, UsuarioActual usuario, DateTime ahora, CancellationToken ct)
    {
        var def = ObtenerDefinicion(clave);
        var nombre = Texto.LimpiarLinea(entrada.Nombre)
            ?? throw ErrorApi.Validacion("El nombre es obligatorio.");
        if (nombre.Length > def.LargoNombre)
            throw ErrorApi.Validacion($"El nombre no puede superar {def.LargoNombre} caracteres.");

        var color = Texto.LimpiarLinea(entrada.Color)?.ToLowerInvariant();
        if (def.TieneColor && color is not null && !ColoresValidos.Contains(color))
            throw ErrorApi.Validacion("Color no válido.");

        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();

        ItemCatalogo? anterior = null;
        if (id is int idExistente)
        {
            anterior = await cn.QuerySingleOrDefaultAsync<ItemCatalogo>(
                def.SqlSelect(false) + " WHERE c.Id = @id", new { id = idExistente }, tx)
                ?? throw ErrorApi.NoEncontrado();
        }

        var activo = entrada.Activo ?? anterior?.Activo ?? true;
        var orden = entrada.Orden ?? anterior?.Orden
            ?? await cn.ExecuteScalarAsync<int>($"SELECT ISNULL(MAX(Orden), 0) + 1 FROM dbo.{def.Tabla}", transaction: tx);
        var predeterminado = def.TienePredeterminado && (entrada.Predeterminado ?? anterior?.Predeterminado ?? false);
        var esResuelto = def.TieneEsResuelto && (entrada.EsResuelto ?? anterior?.EsResuelto ?? false);
        var nivel = def.TieneNivel
            ? entrada.Nivel ?? anterior?.Nivel ?? await cn.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(Nivel), 0) + 1 FROM dbo.Prioridad", transaction: tx)
            : (int?)null;
        var descripcion = def.TieneDescripcion ? Texto.Recortar(Texto.LimpiarLinea(entrada.Descripcion), 200) : null;

        if (predeterminado && !activo)
            throw ErrorApi.Validacion("Un valor predeterminado debe estar activo.");

        var parametros = new DynamicParameters();
        parametros.Add("Nombre", nombre);
        parametros.Add("Activo", activo);
        parametros.Add("Orden", orden);
        var columnas = new List<string> { def.ColumnaNombre, "Activo", "Orden" };
        var valores = new List<string> { "@Nombre", "@Activo", "@Orden" };
        if (def.TienePredeterminado) { columnas.Add("Predeterminado"); valores.Add("@Predeterminado"); parametros.Add("Predeterminado", predeterminado); }
        if (def.TieneEsResuelto) { columnas.Add("EsResuelto"); valores.Add("@EsResuelto"); parametros.Add("EsResuelto", esResuelto); }
        if (def.TieneColor) { columnas.Add("Color"); valores.Add("@Color"); parametros.Add("Color", color); }
        if (def.TieneNivel) { columnas.Add("Nivel"); valores.Add("@Nivel"); parametros.Add("Nivel", nivel); }
        if (def.TieneDescripcion) { columnas.Add("Descripcion"); valores.Add("@Descripcion"); parametros.Add("Descripcion", descripcion); }

        int idFinal;
        if (anterior is null)
        {
            idFinal = await cn.ExecuteScalarAsync<int>(
                $"INSERT INTO dbo.{def.Tabla} ({string.Join(", ", columnas)}) OUTPUT inserted.Id VALUES ({string.Join(", ", valores)})",
                parametros, tx);
        }
        else
        {
            idFinal = anterior.Id;
            parametros.Add("Id", idFinal);
            var asignaciones = columnas.Zip(valores, (c, v) => $"{c} = {v}");
            await cn.ExecuteAsync($"UPDATE dbo.{def.Tabla} SET {string.Join(", ", asignaciones)} WHERE Id = @Id", parametros, tx);
        }

        if (predeterminado)
        {
            await cn.ExecuteAsync($"UPDATE dbo.{def.Tabla} SET Predeterminado = 0 WHERE Id <> @id", new { id = idFinal }, tx);
        }

        if (def.TieneEsResuelto)
        {
            var resueltosActivos = await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Estado WHERE EsResuelto = 1 AND Activo = 1", transaction: tx);
            if (resueltosActivos == 0)
                throw ErrorApi.Validacion("Tiene que quedar al menos un estado activo marcado como \"resuelto\".");
        }

        var guardado = await cn.QuerySingleAsync<ItemCatalogo>(def.SqlSelect(true) + " WHERE c.Id = @id", new { id = idFinal }, tx);
        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, def.Tabla, idFinal.ToString(CultureInfo.InvariantCulture),
            anterior is null ? "CREADO" : "MODIFICADO", new { anterior, nuevo = guardado });
        tx.Commit();
        return guardado;
    }

    public static DefinicionCatalogo ObtenerDefinicion(string clave) =>
        DefinicionCatalogo.Todas.TryGetValue(clave, out var def)
            ? def
            : throw ErrorApi.NoEncontrado("Catálogo desconocido.");
}
