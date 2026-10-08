using System.Security.Cryptography;
using HelpDesk.Api.Excel;
using HelpDesk.Api.Modulos.Catalogos;
using HelpDesk.Api.Modulos.Solicitudes;
using HelpDesk.Api.Modulos.TiempoReal;

namespace HelpDesk.Api.Modulos.Importacion;

public sealed class FilaSolicitudImportada
{
    public int Fila { get; init; }
    public int Id { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string? NombreFuncionario { get; init; }
    public string? Oficina { get; init; }
    public string? Medio { get; init; }
    public string? Tipo { get; init; }
    public string? Descripcion { get; set; }
    public string? Observaciones { get; init; }
    public string? Responsable { get; init; }
    public int? Duracion { get; set; }
    public string? Estado { get; init; }
    public string? Prioridad { get; init; }
    public List<string> Advertencias { get; } = [];
    public bool Existente { get; set; }
}

public sealed class FilaTareaImportada
{
    public int Fila { get; init; }
    public string? Area { get; init; }
    public string Tarea { get; init; } = "";
    public string? Impacto { get; init; }
    public string? CargaTrabajo { get; init; }
    public List<string> Implicados { get; init; } = [];
    public bool Existente { get; set; }
}

public sealed record Incidencia(string Hoja, int Fila, int? Id, string Tipo, string Mensaje);

public sealed class CatalogosNuevos
{
    public List<string> Responsables { get; set; } = [];
    public List<string> ResponsablesInactivos { get; set; } = [];
    public List<string> Medios { get; set; } = [];
    public List<string> Tipos { get; set; } = [];
    public List<string> Estados { get; set; } = [];
    public List<string> Prioridades { get; set; } = [];
    public List<string> Areas { get; set; } = [];
    public int Oficinas { get; set; }
    public List<string> OficinasEjemplo { get; set; } = [];
}

/// <summary>Resultado del análisis de un Excel (se guarda en memoria hasta que el usuario confirma).</summary>
public sealed class AnalisisImportacion
{
    public string NombreArchivo { get; init; } = "";
    public string HashArchivo { get; init; } = "";
    public string? HojaSolicitudes { get; set; }
    public string? HojaTareas { get; set; }
    public int FilasLeidas { get; set; }
    public List<FilaSolicitudImportada> Solicitudes { get; } = [];
    public List<FilaTareaImportada> Tareas { get; } = [];
    public List<Incidencia> Incidencias { get; } = [];
    public CatalogosNuevos CatalogosNuevos { get; set; } = new();
}

public sealed record ResultadoImportacion(
    int ImportacionId,
    int SolicitudesNuevas,
    int SolicitudesExistentes,
    int FilasConError,
    int TareasExtraNuevas,
    int ProximoId);

/// <summary>
/// Importa el Excel histórico de la mesa de ayuda (hoja de solicitudes y hoja de tareas extra).
/// Nunca duplica: una solicitud cuyo ID ya existe se informa como "ya existente" y no se toca.
/// </summary>
public sealed class ImportadorExcel(BaseDatos db, Reloj reloj, SolicitudesServicio solicitudes, Notificador notificador)
{
    private const string HojaSolicitudesNombre = "Solicitudes";
    private const string HojaTareasNombre = "Tareas extra";

    private enum Col { Id, Fecha, Funcionario, Oficina, Medio, Tipo, Descripcion, Observaciones, Responsable, Duracion, Estado, Prioridad }

    private static readonly Dictionary<Col, string[]> Encabezados = new()
    {
        [Col.Id] = ["id solicitud", "id", "nro", "numero", "n°", "nº"],
        [Col.Fecha] = ["fecha y hora de ingreso", "fecha y hora", "fecha de ingreso", "fecha ingreso", "fecha"],
        [Col.Funcionario] = ["nombre funcionario", "funcionario", "nombre del funcionario", "nombre"],
        [Col.Oficina] = ["oficina"],
        [Col.Medio] = ["medio de contacto", "medio"],
        [Col.Tipo] = ["tipo solicitud", "tipo de solicitud", "tipo"],
        [Col.Descripcion] = ["descripcion solicitud", "descripcion de la solicitud", "descripcion", "problema"],
        [Col.Observaciones] = ["observaciones", "observacion"],
        [Col.Responsable] = ["responsable"],
        [Col.Duracion] = ["duracion estimada (min)", "duracion estimada", "duracion (min)", "duracion"],
        [Col.Estado] = ["estado"],
        [Col.Prioridad] = ["prioridad"],
    };

    // ------------------------------------------------------------------ análisis

    public async Task<AnalisisImportacion> AnalizarAsync(byte[] contenido, string nombreArchivo, CancellationToken ct)
    {
        LibroXlsx libro;
        using (var flujo = new MemoryStream(contenido, writable: false))
        {
            try
            {
                libro = LectorXlsx.Leer(flujo);
            }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or InvalidDataException)
            {
                throw ErrorApi.Validacion("No se pudo leer el archivo. Verificá que sea un Excel .xlsx (no .xls ni .csv).");
            }
        }

        var analisis = new AnalisisImportacion
        {
            NombreArchivo = nombreArchivo,
            HashArchivo = Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant(),
        };

        var encontroSolicitudes = false;
        var encontroTareas = false;
        foreach (var hoja in libro.Hojas)
        {
            if (!encontroSolicitudes && BuscarEncabezadoSolicitudes(hoja) is { } encabezado)
            {
                analisis.HojaSolicitudes = hoja.Nombre;
                LeerSolicitudes(hoja, encabezado.Fila, encabezado.Columnas, analisis);
                encontroSolicitudes = true;
                continue;
            }
            if (!encontroTareas && BuscarEncabezadoTareas(hoja) is { } encabezadoTareas)
            {
                analisis.HojaTareas = hoja.Nombre;
                LeerTareas(hoja, encabezadoTareas.Fila, encabezadoTareas.Columnas, analisis);
                encontroTareas = true;
            }
        }

        if (!encontroSolicitudes && !encontroTareas)
        {
            throw ErrorApi.Validacion(
                "No se encontró la tabla de solicitudes. La primera fila debe tener los encabezados " +
                "\"ID Solicitud\", \"Fecha y hora de ingreso\", \"Descripción solicitud\", etc.");
        }

        await MarcarExistentesAsync(analisis, ct);
        return analisis;
    }

    private static (int Fila, Dictionary<Col, int> Columnas)? BuscarEncabezadoSolicitudes(HojaXlsx hoja)
    {
        for (var fila = 1; fila <= Math.Min(20, hoja.UltimaFila); fila++)
        {
            if (!hoja.Filas.TryGetValue(fila, out var celdas)) continue;
            var columnas = new Dictionary<Col, int>();
            foreach (var (columna, celda) in celdas.OrderBy(c => c.Key))
            {
                var texto = Texto.Normalizar(celda.ComoTexto());
                if (texto.Length == 0) continue;
                foreach (var (campo, alias) in Encabezados)
                {
                    if (!columnas.ContainsKey(campo) && alias.Contains(texto))
                    {
                        columnas[campo] = columna;
                        break;
                    }
                }
            }
            if (columnas.ContainsKey(Col.Id) && columnas.ContainsKey(Col.Fecha) && columnas.ContainsKey(Col.Descripcion) && columnas.Count >= 6)
            {
                return (fila, columnas);
            }
        }
        return null;
    }

    private static (int Fila, Dictionary<string, int> Columnas)? BuscarEncabezadoTareas(HojaXlsx hoja)
    {
        for (var fila = 1; fila <= Math.Min(20, hoja.UltimaFila); fila++)
        {
            if (!hoja.Filas.TryGetValue(fila, out var celdas)) continue;
            var columnas = new Dictionary<string, int>();
            foreach (var (columna, celda) in celdas.OrderBy(c => c.Key))
            {
                var texto = Texto.Normalizar(celda.ComoTexto());
                if (texto.StartsWith("area", StringComparison.Ordinal)) columnas.TryAdd("area", columna);
                else if (texto == "tarea" || texto == "tareas") columnas.TryAdd("tarea", columna);
                else if (texto.StartsWith("impacto", StringComparison.Ordinal)) columnas.TryAdd("impacto", columna);
                else if (texto.StartsWith("carga", StringComparison.Ordinal)) columnas.TryAdd("carga", columna);
                else if (texto.StartsWith("implicados", StringComparison.Ordinal)) columnas.TryAdd("implicados", columna);
            }
            if (columnas.ContainsKey("tarea") && columnas.Count >= 3) return (fila, columnas);
        }
        return null;
    }

    private static void LeerSolicitudes(HojaXlsx hoja, int filaEncabezado, Dictionary<Col, int> columnas, AnalisisImportacion analisis)
    {
        // Límite: la tabla de Excel que contiene el encabezado, o la última fila con datos.
        var tabla = hoja.Tablas.FirstOrDefault(t => t.FilaInicio == filaEncabezado);
        var ultimaFila = tabla?.FilaFin ?? hoja.UltimaFila;

        Celda C(int fila, Col campo) => columnas.TryGetValue(campo, out var col) ? hoja.Obtener(fila, col) : Celda.Vacia;

        // 1) Filas con datos
        var filas = new List<int>();
        for (var fila = filaEncabezado + 1; fila <= ultimaFila; fila++)
        {
            var tieneDatos = columnas.Where(c => c.Key != Col.Id).Any(c => !hoja.Obtener(fila, c.Value).EstaVacia);
            if (tieneDatos) filas.Add(fila);
        }
        analisis.FilasLeidas = filas.Count;

        // 2) IDs. La columna del registro es una fórmula =FILA()-FILA(encabezado): si falta el valor,
        //    se usa la posición de la fila, igual que la fórmula.
        var idsCrudos = new int?[filas.Count];
        var porPosicion = new bool[filas.Count];
        var erroresId = new string?[filas.Count];
        for (var i = 0; i < filas.Count; i++)
        {
            var celda = C(filas[i], Col.Id);
            if (celda.Tipo == TipoCelda.Numero && Math.Abs(celda.Numero % 1) < 1e-9 && celda.Numero is >= 1 and <= int.MaxValue)
            {
                idsCrudos[i] = (int)celda.Numero;
            }
            else if (celda.Tipo == TipoCelda.Texto && int.TryParse(celda.Texto?.Trim().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var desdeTexto) && desdeTexto > 0)
            {
                idsCrudos[i] = desdeTexto;
            }
            else if (celda.EstaVacia || celda.EsFormula)
            {
                porPosicion[i] = true;
            }
            else
            {
                erroresId[i] = $"ID no válido: \"{celda.ComoTexto()}\".";
            }
        }

        // ¿El archivo numera por posición? (ID = fila - fila del encabezado en las filas vecinas)
        bool VecinoPorPosicion(int i)
        {
            for (var j = Math.Max(0, i - 3); j <= Math.Min(filas.Count - 1, i + 3); j++)
            {
                if (j != i && idsCrudos[j] is int id && id == filas[j] - filaEncabezado) return true;
            }
            return false;
        }

        // 3) Fechas
        var celdasFecha = filas.Select(f => C(f, Col.Fecha)).ToList();
        var mesPrimero = InterpreteFechas.DetectarMesPrimero(celdasFecha);
        var fechas = InterpreteFechas.Interpretar(celdasFecha, mesPrimero);

        var vistos = new HashSet<int>();
        for (var i = 0; i < filas.Count; i++)
        {
            var fila = filas[i];
            var advertencias = new List<string>();
            int id;
            if (erroresId[i] is { } errorId)
            {
                analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, null, "error", errorId));
                continue;
            }
            if (idsCrudos[i] is int crudo)
            {
                id = crudo;
            }
            else if (porPosicion[i] && VecinoPorPosicion(i))
            {
                id = fila - filaEncabezado;
                advertencias.Add($"ID vacío: se tomó el número que le corresponde por su posición ({id}).");
            }
            else
            {
                analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, null, "error", "La fila no tiene ID de solicitud."));
                continue;
            }

            if (!vistos.Add(id))
            {
                analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, id, "error", $"ID {id} repetido en el archivo: se importa solo la primera aparición."));
                continue;
            }

            var fecha = fechas[i];
            if (fecha.Fecha is not DateTime fechaIngreso)
            {
                analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, id, "error", fecha.Advertencia ?? "Fecha no válida."));
                continue;
            }
            if (fecha.Advertencia is not null) advertencias.Add(fecha.Advertencia);

            // Duración
            int? duracion = null;
            var celdaDuracion = C(fila, Col.Duracion);
            if (celdaDuracion.Tipo == TipoCelda.Numero && celdaDuracion.Numero is >= 0 and <= SolicitudesServicio.DuracionMaxima)
            {
                duracion = (int)Math.Round(celdaDuracion.Numero);
            }
            else if (!celdaDuracion.EstaVacia)
            {
                var textoDuracion = celdaDuracion.ComoTexto()?.Trim();
                if (int.TryParse(textoDuracion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutos) && minutos >= 0)
                    duracion = minutos;
                else
                    advertencias.Add($"Duración no numérica (\"{textoDuracion}\"): se dejó vacía.");
            }

            var descripcion = Texto.LimpiarLinea(C(fila, Col.Descripcion).ComoTexto());
            if (descripcion is { Length: > SolicitudesServicio.LargoDescripcion })
            {
                advertencias.Add($"Descripción recortada a {SolicitudesServicio.LargoDescripcion} caracteres.");
                descripcion = descripcion[..SolicitudesServicio.LargoDescripcion];
            }

            var registro = new FilaSolicitudImportada
            {
                Fila = fila,
                Id = id,
                FechaIngreso = fechaIngreso,
                NombreFuncionario = Texto.Recortar(Texto.LimpiarLinea(C(fila, Col.Funcionario).ComoTexto()), SolicitudesServicio.LargoFuncionario),
                Oficina = Texto.Recortar(Texto.LimpiarLinea(C(fila, Col.Oficina).ComoTexto()), OficinasRepositorio.LargoNombre),
                Medio = Texto.LimpiarLinea(C(fila, Col.Medio).ComoTexto()),
                Tipo = Texto.LimpiarLinea(C(fila, Col.Tipo).ComoTexto()),
                Descripcion = descripcion,
                Observaciones = Texto.Limpiar(C(fila, Col.Observaciones).ComoTexto()),
                Responsable = Texto.LimpiarLinea(C(fila, Col.Responsable).ComoTexto()),
                Duracion = duracion,
                Estado = Texto.LimpiarLinea(C(fila, Col.Estado).ComoTexto()),
                Prioridad = Texto.LimpiarLinea(C(fila, Col.Prioridad).ComoTexto()),
            };
            registro.Advertencias.AddRange(advertencias);
            foreach (var advertencia in advertencias)
            {
                analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, id, "advertencia", advertencia));
            }
            analisis.Solicitudes.Add(registro);
        }
    }

    private static void LeerTareas(HojaXlsx hoja, int filaEncabezado, Dictionary<string, int> columnas, AnalisisImportacion analisis)
    {
        string? Valor(int fila, string campo) =>
            columnas.TryGetValue(campo, out var col) ? Texto.Limpiar(hoja.Obtener(fila, col).ComoTexto()) : null;

        for (var fila = filaEncabezado + 1; fila <= hoja.UltimaFila; fila++)
        {
            var tarea = Valor(fila, "tarea");
            if (tarea is null)
            {
                if (columnas.Keys.Any(c => Valor(fila, c) is not null))
                    analisis.Incidencias.Add(new Incidencia(hoja.Nombre, fila, null, "error", "Tarea extra sin descripción de la tarea."));
                continue;
            }
            analisis.Tareas.Add(new FilaTareaImportada
            {
                Fila = fila,
                Area = Texto.Recortar(Texto.LimpiarLinea(Valor(fila, "area")), 150),
                Tarea = Texto.Recortar(tarea, 500)!,
                Impacto = Valor(fila, "impacto"),
                CargaTrabajo = Texto.Recortar(Valor(fila, "carga"), 1000),
                Implicados = SepararImplicados(Valor(fila, "implicados")),
            });
        }
    }

    /// <summary>"Agustín y Facundo" → [Agustín, Facundo]; "Valentín (Antiguo integrante)" → [Valentín].</summary>
    public static List<string> SepararImplicados(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var sinParentesis = System.Text.RegularExpressions.Regex.Replace(texto, @"\([^)]*\)", " ");
        return System.Text.RegularExpressions.Regex
            .Split(sinParentesis, @"\s*(?:,|;|/|&|\s+y\s+|\s+e\s+)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(Texto.LimpiarLinea)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task MarcarExistentesAsync(AnalisisImportacion analisis, CancellationToken ct)
    {
        await using var cn = await db.AbrirAsync(ct);

        // El ID del Excel es el número visible. Se considera existente si lo usa alguna solicitud
        // (activa o eliminada): importar dos veces el mismo archivo no agrega nada.
        var existentes = (await cn.QueryAsync<int>("SELECT DISTINCT Numero FROM Solicitud")).ToHashSet();
        foreach (var fila in analisis.Solicitudes)
        {
            fila.Existente = existentes.Contains(fila.Id);
        }

        var tareasExistentes = (await cn.QueryAsync<(string? Area, string Tarea)>(
                """
                SELECT a.Nombre AS Area, te.Tarea
                FROM TareaExtra te LEFT JOIN AreaAsse a ON a.Id = te.AreaAsseId
                WHERE te.EliminadoEn IS NULL
                """))
            .Select(t => ClaveTarea(t.Area, t.Tarea))
            .ToHashSet();
        foreach (var tarea in analisis.Tareas)
        {
            tarea.Existente = tareasExistentes.Contains(ClaveTarea(tarea.Area, tarea.Tarea));
        }

        // Valores de catálogos que no existen y se crearán.
        var catalogos = await CatalogosRepositorio.CargarMapasAsync(cn, null);
        var areas = await cn.QueryAsync<string>("SELECT Nombre FROM AreaAsse");
        var oficinas = (await cn.QueryAsync<string>("SELECT Nombre FROM Oficina")).Select(Texto.Normalizar).ToHashSet();
        var nuevas = analisis.Solicitudes.Where(s => !s.Existente).ToList();
        var tareasNuevas = analisis.Tareas.Where(t => !t.Existente).ToList();

        var responsablesExistentes = catalogos.Responsables.Values.Select(v => v.Nombre).ToList();
        var responsablesNuevos = Faltantes(nuevas.Select(s => s.Responsable), responsablesExistentes);
        analisis.CatalogosNuevos = new CatalogosNuevos
        {
            Responsables = responsablesNuevos,
            ResponsablesInactivos = Faltantes(tareasNuevas.SelectMany(t => t.Implicados), responsablesExistentes.Concat(responsablesNuevos)),
            Medios = Faltantes(nuevas.Select(s => s.Medio), catalogos.Medios.Values.Select(v => v.Nombre)),
            Tipos = Faltantes(nuevas.Select(s => s.Tipo), catalogos.Tipos.Values.Select(v => v.Nombre)),
            Estados = Faltantes(nuevas.Select(s => s.Estado), catalogos.Estados.Values.Select(v => v.Nombre)),
            Prioridades = Faltantes(nuevas.Select(s => s.Prioridad), catalogos.Prioridades.Values.Select(v => v.Nombre)),
            Areas = Faltantes(tareasNuevas.Select(t => t.Area), areas),
        };
        var oficinasNuevas = nuevas
            .Where(s => s.Oficina is not null && !oficinas.Contains(Texto.Normalizar(s.Oficina)))
            .GroupBy(s => Texto.Normalizar(s.Oficina))
            .Select(g => g.First().Oficina!)
            .ToList();
        analisis.CatalogosNuevos.Oficinas = oficinasNuevas.Count;
        analisis.CatalogosNuevos.OficinasEjemplo = oficinasNuevas.Take(8).ToList();
    }

    private static List<string> Faltantes(IEnumerable<string?> valores, IEnumerable<string> existentes)
    {
        var conocidos = existentes.Select(Texto.Normalizar).ToHashSet();
        return valores
            .Where(v => v is not null && !conocidos.Contains(Texto.Normalizar(v)))
            .Select(v => v!)
            .GroupBy(Texto.Normalizar)
            .Select(g => g.GroupBy(v => v).OrderByDescending(x => x.Count()).First().Key)
            .OrderBy(v => v)
            .ToList();
    }

    private static string ClaveTarea(string? area, string tarea) => $"{Texto.Normalizar(area)}|{Texto.Normalizar(tarea)}";

    // ------------------------------------------------------------------ confirmación

    public async Task<ResultadoImportacion> ConfirmarAsync(AnalisisImportacion analisis, UsuarioActual usuario, CancellationToken ct)
    {
        var ahora = reloj.Ahora();
        await using var cn = await db.AbrirAsync(ct);
        using var tx = cn.BeginTransaction();

        // Volver a verificar existentes dentro de la transacción (otro usuario pudo importar mientras tanto).
        // Bloquea altas concurrentes de solicitudes hasta el fin de la importación
        // (equivale al UPDLOCK, HOLDLOCK de la versión SQL Server).
        // Primero el bloqueo de numeración (mismo orden que el alta de solicitudes: sin interbloqueos).
        await cn.ExecuteAsync("SELECT pg_advisory_xact_lock(@clave)", new { clave = SolicitudesServicio.ClaveBloqueoNumeracion }, tx);
        await cn.ExecuteAsync("LOCK TABLE Solicitud IN SHARE ROW EXCLUSIVE MODE", transaction: tx);
        var existentes = (await cn.QueryAsync<int>("SELECT DISTINCT Numero FROM Solicitud", transaction: tx)).ToHashSet();
        var nuevas = analisis.Solicitudes.Where(s => !existentes.Contains(s.Id)).ToList();
        var existentesEnArchivo = analisis.Solicitudes.Count - nuevas.Count;

        var tareasActuales = (await cn.QueryAsync<(string? Area, string Tarea)>(
                """
                SELECT a.Nombre AS Area, te.Tarea
                FROM TareaExtra te LEFT JOIN AreaAsse a ON a.Id = te.AreaAsseId
                WHERE te.EliminadoEn IS NULL
                """, transaction: tx))
            .Select(t => ClaveTarea(t.Area, t.Tarea))
            .ToHashSet();
        var tareasNuevas = analisis.Tareas.Where(t => tareasActuales.Add(ClaveTarea(t.Area, t.Tarea))).ToList();

        // Catálogos (se crean los valores que falten)
        var responsables = await AsegurarCatalogoAsync(cn, tx, "Responsable", "Nombre", nuevas.Select(s => s.Responsable), activo: true, largo: 100);
        foreach (var (clave, id) in await AsegurarCatalogoAsync(cn, tx, "Responsable", "Nombre",
                     tareasNuevas.SelectMany(t => t.Implicados).Where(n => !responsables.ContainsKey(Texto.Normalizar(n))), activo: false, largo: 100))
        {
            responsables[clave] = id;
        }
        var medios = await AsegurarCatalogoAsync(cn, tx, "MedioContacto", "Nombre", nuevas.Select(s => s.Medio), activo: true, largo: 60);
        var tipos = await AsegurarCatalogoAsync(cn, tx, "TipoSolicitud", "Codigo", nuevas.Select(s => s.Tipo), activo: true, largo: 30);
        var estados = await AsegurarEstadosAsync(cn, tx, nuevas.Select(s => s.Estado));
        var prioridades = await AsegurarPrioridadesAsync(cn, tx, nuevas.Select(s => s.Prioridad));
        var areas = await AsegurarCatalogoAsync(cn, tx, "AreaAsse", "Nombre", tareasNuevas.Select(t => t.Area), activo: true, largo: 150);

        var oficinas = new Dictionary<string, int>();
        foreach (var nombre in nuevas.Select(s => s.Oficina).Where(o => o is not null).Distinct())
        {
            var clave = Texto.Normalizar(nombre);
            if (oficinas.ContainsKey(clave)) continue;
            var id = await OficinasRepositorio.ObtenerOCrearAsync(cn, tx, nombre, ahora);
            if (id is int idOficina) oficinas[clave] = idOficina;
        }

        var errores = analisis.Incidencias.Where(i => i.Tipo == "error").Select(i => (i.Hoja, i.Fila)).Distinct().Count();
        var conAdvertencia = analisis.Solicitudes.Count(s => s.Advertencias.Count > 0);
        var importacionId = await cn.ExecuteScalarAsync<int>(
            """
            INSERT INTO Importacion (FechaHora, UsuarioId, NombreArchivo, HashArchivo, FilasLeidas, SolicitudesNuevas,
                SolicitudesExistentes, FilasConError, FilasConAdvertencia, TareasExtraNuevas)
            VALUES (@ahora, @usuarioId, @nombre, @hash, @leidas, @nuevas, @existentes, @errores, @advertencias, @tareas)
            RETURNING Id
            """,
            new
            {
                ahora,
                usuarioId = usuario.Id,
                nombre = Texto.Recortar(analisis.NombreArchivo, 260),
                hash = analisis.HashArchivo,
                leidas = analisis.FilasLeidas,
                nuevas = nuevas.Count,
                existentes = existentesEnArchivo,
                errores,
                advertencias = conAdvertencia,
                tareas = tareasNuevas.Count,
            },
            tx);

        int? Buscar(Dictionary<string, int> mapa, string? valor) =>
            valor is not null && mapa.TryGetValue(Texto.Normalizar(valor), out var id) ? id : null;

        if (nuevas.Count > 0)
        {
            // Claves internas nuevas (un bloque de la secuencia); el ID del Excel se conserva como número visible.
            await SolicitudesServicio.AsegurarSecuenciaAsync(cn, tx);
            var ultimaClave = await cn.ExecuteScalarAsync<int>(
                """
                UPDATE Secuencia
                SET UltimoValor = GREATEST(UltimoValor, (SELECT COALESCE(MAX(Id), 0) FROM Solicitud)) + @cantidad
                WHERE Nombre = 'Solicitud'
                RETURNING UltimoValor
                """,
                new { cantidad = nuevas.Count }, tx);
            var claves = nuevas.Select((s, i) => (s, Clave: ultimaClave - nuevas.Count + 1 + i)).ToDictionary(x => x.s, x => x.Clave);

            var filasSolicitud = nuevas.Select(s => new
            {
                Id = claves[s],
                Numero = s.Id,
                s.FechaIngreso,
                DiaSemana = Reloj.DiaSemana(s.FechaIngreso),
                s.NombreFuncionario,
                OficinaId = Buscar(oficinas, s.Oficina),
                MedioContactoId = Buscar(medios, s.Medio),
                TipoSolicitudId = Buscar(tipos, s.Tipo),
                s.Descripcion,
                s.Observaciones,
                ResponsableId = Buscar(responsables, s.Responsable),
                DuracionEstimadaMin = s.Duracion,
                EstadoId = Buscar(estados, s.Estado),
                PrioridadId = Buscar(prioridades, s.Prioridad),
                ImportacionId = importacionId,
                FilaExcel = s.Fila,
                Ahora = ahora,
                UsuarioId = usuario.Id,
            }).ToList();

            await cn.ExecuteAsync(
                """
                INSERT INTO Solicitud
                    (Id, Numero, FechaIngreso, DiaSemana, NombreFuncionario, OficinaId, MedioContactoId, TipoSolicitudId,
                     Descripcion, Observaciones, ResponsableId, DuracionEstimadaMin, EstadoId, PrioridadId,
                     FechaResolucion, MinutosResolucion, Origen, ImportacionId, FilaExcel, Version,
                     CreadoPorId, CreadoEn, ActualizadoPorId, ActualizadoEn)
                VALUES
                    (@Id, @Numero, @FechaIngreso, @DiaSemana, @NombreFuncionario, @OficinaId, @MedioContactoId, @TipoSolicitudId,
                     @Descripcion, @Observaciones, @ResponsableId, @DuracionEstimadaMin, @EstadoId, @PrioridadId,
                     NULL, NULL, 'IMPORTACION', @ImportacionId, @FilaExcel, 1,
                     NULL, @Ahora, @UsuarioId, @Ahora)
                """,
                filasSolicitud, tx, commandTimeout: 600);

            var historial = nuevas.Select(s => new
            {
                SolicitudId = claves[s],
                FechaHora = ahora,
                UsuarioId = usuario.Id,
                Accion = "IMPORTADA",
                Detalle = Texto.Recortar(
                    $"Importada desde \"{analisis.NombreArchivo}\" (hoja {analisis.HojaSolicitudes}, fila {s.Fila})." +
                    (s.Advertencias.Count > 0 ? " " + string.Join(" ", s.Advertencias) : ""), 2000),
            }).ToList();
            await cn.ExecuteAsync(
                """
                INSERT INTO SolicitudHistorial (SolicitudId, FechaHora, UsuarioId, Accion, Detalle)
                VALUES (@SolicitudId, @FechaHora, @UsuarioId, @Accion, @Detalle)
                """,
                historial, tx, commandTimeout: 600);

        }

        foreach (var tarea in tareasNuevas)
        {
            var tareaId = await cn.ExecuteScalarAsync<int>(
                """
                INSERT INTO TareaExtra (AreaAsseId, Tarea, Impacto, CargaTrabajo, Origen, ImportacionId, CreadoPorId, CreadoEn, ActualizadoPorId, ActualizadoEn)
                VALUES (@area, @tarea, @impacto, @carga, 'IMPORTACION', @importacionId, @usuarioId, @ahora, @usuarioId, @ahora)
                RETURNING Id
                """,
                new
                {
                    area = Buscar(areas, tarea.Area),
                    tarea = tarea.Tarea,
                    impacto = tarea.Impacto,
                    carga = tarea.CargaTrabajo,
                    importacionId,
                    usuarioId = usuario.Id,
                    ahora,
                },
                tx);
            var implicados = tarea.Implicados.Select(n => Buscar(responsables, n)).OfType<int>().Distinct().ToList();
            if (implicados.Count > 0)
            {
                await cn.ExecuteAsync(
                    "INSERT INTO TareaExtraImplicado (TareaExtraId, ResponsableId) VALUES (@TareaExtraId, @ResponsableId)",
                    implicados.Select(r => new { TareaExtraId = tareaId, ResponsableId = r }).ToList(), tx);
            }
        }

        await RegistroAuditoria.RegistrarAsync(cn, tx, usuario.Id, ahora, "Importacion", importacionId.ToString(CultureInfo.InvariantCulture),
            "CONFIRMADA", new { analisis.NombreArchivo, nuevas = nuevas.Count, existentes = existentesEnArchivo, tareas = tareasNuevas.Count });
        await notificador.PublicarAsync(cn, tx, [TiposCambio.Solicitudes, TiposCambio.Tareas, TiposCambio.Catalogos], "importacion");
        tx.Commit();

        var proximoId = await solicitudes.ProximoIdAsync(ct);
        return new ResultadoImportacion(importacionId, nuevas.Count, existentesEnArchivo, errores, tareasNuevas.Count, proximoId);
    }

    /// <summary>Devuelve nombre normalizado → Id, creando los valores que no existan.</summary>
    private static async Task<Dictionary<string, int>> AsegurarCatalogoAsync(IDbConnection cn, IDbTransaction tx,
        string tabla, string columna, IEnumerable<string?> valores, bool activo, int largo)
    {
        // tabla y columna vienen de este archivo (lista fija), nunca del usuario.
        var mapa = new Dictionary<string, int>();
        foreach (var fila in await cn.QueryAsync<(int Id, string Nombre)>($"SELECT Id, {columna} AS Nombre FROM {tabla}", transaction: tx))
        {
            mapa.TryAdd(Texto.Normalizar(fila.Nombre), fila.Id);
        }
        var orden = await cn.ExecuteScalarAsync<int>($"SELECT COALESCE(MAX(Orden), 0) FROM {tabla}", transaction: tx);
        foreach (var valor in valores)
        {
            if (valor is null) continue;
            var clave = Texto.Normalizar(valor);
            if (clave.Length == 0 || mapa.ContainsKey(clave)) continue;
            var id = await cn.ExecuteScalarAsync<int>(
                $"INSERT INTO {tabla} ({columna}, Activo, Orden) VALUES (@valor, @activo, @orden) RETURNING Id",
                new { valor = Texto.Recortar(valor, largo), activo, orden = ++orden }, tx);
            mapa[clave] = id;
        }
        return mapa;
    }

    private static async Task<Dictionary<string, int>> AsegurarEstadosAsync(IDbConnection cn, IDbTransaction tx, IEnumerable<string?> valores)
    {
        var mapa = new Dictionary<string, int>();
        foreach (var fila in await cn.QueryAsync<(int Id, string Nombre)>("SELECT Id, Nombre FROM Estado", transaction: tx))
        {
            mapa.TryAdd(Texto.Normalizar(fila.Nombre), fila.Id);
        }
        var orden = await cn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Orden), 0) FROM Estado", transaction: tx);
        foreach (var valor in valores)
        {
            if (valor is null) continue;
            var clave = Texto.Normalizar(valor);
            if (clave.Length == 0 || mapa.ContainsKey(clave)) continue;
            var esResuelto = clave.StartsWith("resuelt", StringComparison.Ordinal) || clave.StartsWith("cerrad", StringComparison.Ordinal) ||
                             clave.StartsWith("finalizad", StringComparison.Ordinal);
            var id = await cn.ExecuteScalarAsync<int>(
                "INSERT INTO Estado (Nombre, EsResuelto, Color, Activo, Orden, Predeterminado) VALUES (@valor, @esResuelto, @color, TRUE, @orden, FALSE) RETURNING Id",
                new { valor = Texto.Recortar(valor, 60), esResuelto, color = esResuelto ? "verde" : "ambar", orden = ++orden }, tx);
            mapa[clave] = id;
        }
        return mapa;
    }

    private static async Task<Dictionary<string, int>> AsegurarPrioridadesAsync(IDbConnection cn, IDbTransaction tx, IEnumerable<string?> valores)
    {
        var mapa = new Dictionary<string, int>();
        foreach (var fila in await cn.QueryAsync<(int Id, string Nombre)>("SELECT Id, Nombre FROM Prioridad", transaction: tx))
        {
            mapa.TryAdd(Texto.Normalizar(fila.Nombre), fila.Id);
        }
        var nivel = await cn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Nivel), 0) FROM Prioridad", transaction: tx);
        foreach (var valor in valores)
        {
            if (valor is null) continue;
            var clave = Texto.Normalizar(valor);
            if (clave.Length == 0 || mapa.ContainsKey(clave)) continue;
            nivel++;
            var id = await cn.ExecuteScalarAsync<int>(
                "INSERT INTO Prioridad (Nombre, Nivel, Activo, Orden, Predeterminado) VALUES (@valor, @nivel, TRUE, @nivel, FALSE) RETURNING Id",
                new { valor = Texto.Recortar(valor, 30), nivel }, tx);
            mapa[clave] = id;
        }
        return mapa;
    }
}
