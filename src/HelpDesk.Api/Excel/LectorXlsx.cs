using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace HelpDesk.Api.Excel;

public enum TipoCelda
{
    Vacia,
    Texto,
    Numero,
    Booleano,
    Fecha,
    Error,
}

/// <summary>Valor de una celda tal como quedó guardado en el archivo (valor calculado, no la fórmula).</summary>
public sealed class Celda
{
    public static readonly Celda Vacia = new() { Tipo = TipoCelda.Vacia };

    public TipoCelda Tipo { get; init; }
    public string? Texto { get; init; }
    public double Numero { get; init; }
    public DateTime? Fecha { get; init; }
    /// <summary>Fórmula de la celda, si la tiene ("" para fórmulas compartidas).</summary>
    public string? Formula { get; init; }

    public bool EsFormula => Formula is not null;

    public bool EstaVacia =>
        Tipo == TipoCelda.Vacia || (Tipo == TipoCelda.Texto && string.IsNullOrWhiteSpace(Texto));

    /// <summary>Representación en texto: los números enteros sin decimales (99266456 → "99266456").</summary>
    public string? ComoTexto() => Tipo switch
    {
        TipoCelda.Texto => Texto,
        TipoCelda.Numero => Math.Abs(Numero % 1) < 1e-9 && Math.Abs(Numero) < 1e15
            ? ((long)Numero).ToString(CultureInfo.InvariantCulture)
            : Numero.ToString(CultureInfo.InvariantCulture),
        TipoCelda.Fecha => Fecha?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        TipoCelda.Booleano => Numero != 0 ? "VERDADERO" : "FALSO",
        TipoCelda.Error => Texto,
        _ => null,
    };
}

public sealed class TablaXlsx
{
    public string Nombre { get; init; } = "";
    public int FilaInicio { get; init; }
    public int FilaFin { get; init; }
    public int ColumnaInicio { get; init; }
    public int ColumnaFin { get; init; }
}

public sealed class HojaXlsx
{
    public string Nombre { get; init; } = "";
    public Dictionary<int, Dictionary<int, Celda>> Filas { get; } = new();
    public List<TablaXlsx> Tablas { get; } = [];

    public int UltimaFila => Filas.Count == 0 ? 0 : Filas.Keys.Max();

    public Celda Obtener(int fila, int columna) =>
        Filas.TryGetValue(fila, out var celdas) && celdas.TryGetValue(columna, out var celda) ? celda : Celda.Vacia;
}

public sealed class LibroXlsx
{
    public List<HojaXlsx> Hojas { get; } = [];
}

/// <summary>
/// Lector mínimo de archivos .xlsx (Office Open XML) usando solo librerías de .NET.
/// Lee valores, fórmulas (con su último valor calculado), fechas y tablas.
/// </summary>
public static class LectorXlsx
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsRelacion = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace NsPaquete = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XmlReaderSettings Opciones = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
    };

    private sealed record Relacion(string Ruta, string Tipo);

    public static LibroXlsx Leer(Stream flujo)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(flujo, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new FormatException("El archivo no es un Excel .xlsx válido.");
        }

        using (zip)
        {
            var libroXml = CargarXml(zip, "xl/workbook.xml")
                ?? throw new FormatException("El archivo no es un Excel .xlsx válido (falta xl/workbook.xml).");
            var raiz = libroXml.Root!;
            var atributo1904 = (string?)raiz.Element(Ns + "workbookPr")?.Attribute("date1904");
            var es1904 = atributo1904 is "1" or "true";

            var relaciones = LeerRelaciones(zip, "xl/_rels/workbook.xml.rels", "xl/");
            var compartidas = LeerCadenasCompartidas(zip);
            var estilosFecha = LeerEstilosFecha(zip);

            var libro = new LibroXlsx();
            var hojas = raiz.Element(Ns + "sheets")?.Elements(Ns + "sheet") ?? Enumerable.Empty<XElement>();
            foreach (var hojaXml in hojas)
            {
                var id = (string?)hojaXml.Attribute(NsRelacion + "id");
                if (id is null || !relaciones.TryGetValue(id, out var relacion)) continue;
                var entrada = zip.GetEntry(relacion.Ruta);
                if (entrada is null) continue;

                var hoja = new HojaXlsx { Nombre = (string?)hojaXml.Attribute("name") ?? "" };
                using (var contenido = entrada.Open())
                {
                    LeerHoja(contenido, hoja, compartidas, estilosFecha, es1904);
                }

                var relacionesHoja = LeerRelaciones(zip, RutaRelaciones(relacion.Ruta), Directorio(relacion.Ruta));
                foreach (var rel in relacionesHoja.Values.Where(r => r.Tipo.EndsWith("/table", StringComparison.Ordinal)))
                {
                    var tablaXml = CargarXml(zip, rel.Ruta)?.Root;
                    var rango = (string?)tablaXml?.Attribute("ref");
                    if (tablaXml is null || rango is null) continue;
                    var (inicio, fin) = ParsearRango(rango);
                    hoja.Tablas.Add(new TablaXlsx
                    {
                        Nombre = (string?)tablaXml.Attribute("displayName") ?? (string?)tablaXml.Attribute("name") ?? "",
                        FilaInicio = inicio.Fila,
                        ColumnaInicio = inicio.Columna,
                        FilaFin = fin.Fila,
                        ColumnaFin = fin.Columna,
                    });
                }

                libro.Hojas.Add(hoja);
            }
            return libro;
        }
    }

    private static void LeerHoja(Stream contenido, HojaXlsx hoja, List<string> compartidas, HashSet<int> estilosFecha, bool es1904)
    {
        using var lector = XmlReader.Create(contenido, Opciones);
        var filaActual = 0;
        var columnaActual = 0;

        while (lector.Read())
        {
            if (lector.NodeType != XmlNodeType.Element) continue;

            if (lector.LocalName == "row")
            {
                filaActual = int.TryParse(lector.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : filaActual + 1;
                columnaActual = 0;
                continue;
            }

            if (lector.LocalName != "c") continue;

            var referencia = lector.GetAttribute("r");
            var tipo = lector.GetAttribute("t");
            var estilo = int.TryParse(lector.GetAttribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
            int fila, columna;
            if (referencia is not null)
            {
                var posicion = ParsearReferencia(referencia);
                fila = posicion.Fila;
                columna = posicion.Columna;
            }
            else
            {
                fila = filaActual;
                columna = columnaActual + 1;
            }
            columnaActual = columna;

            string? valor = null;
            string? formula = null;
            string? enLinea = null;

            if (!lector.IsEmptyElement)
            {
                var profundidad = lector.Depth;
                lector.Read();
                while (!lector.EOF && !(lector.NodeType == XmlNodeType.EndElement && lector.Depth == profundidad))
                {
                    if (lector.NodeType == XmlNodeType.Element)
                    {
                        switch (lector.LocalName)
                        {
                            case "v":
                                valor = lector.ReadElementContentAsString();
                                continue;
                            case "f":
                                formula = lector.ReadElementContentAsString();
                                continue;
                            case "is":
                                var elemento = (XElement)XNode.ReadFrom(lector);
                                enLinea = TextoDeCadena(elemento);
                                continue;
                        }
                    }
                    lector.Read();
                }
            }

            var celda = CrearCelda(tipo, valor, enLinea, formula, estilo, compartidas, estilosFecha, es1904);
            if (celda.Tipo == TipoCelda.Vacia && celda.Formula is null) continue;

            if (!hoja.Filas.TryGetValue(fila, out var celdas))
            {
                celdas = new Dictionary<int, Celda>();
                hoja.Filas[fila] = celdas;
            }
            celdas[columna] = celda;
        }
    }

    private static Celda CrearCelda(string? tipo, string? valor, string? enLinea, string? formula, int estilo,
        List<string> compartidas, HashSet<int> estilosFecha, bool es1904)
    {
        switch (tipo)
        {
            case "s":
                if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indice) &&
                    indice >= 0 && indice < compartidas.Count)
                {
                    return new Celda { Tipo = TipoCelda.Texto, Texto = compartidas[indice], Formula = formula };
                }
                return new Celda { Tipo = TipoCelda.Vacia, Formula = formula };
            case "inlineStr":
                return new Celda { Tipo = TipoCelda.Texto, Texto = enLinea ?? valor ?? "", Formula = formula };
            case "str":
                return valor is null
                    ? new Celda { Tipo = TipoCelda.Vacia, Formula = formula }
                    : new Celda { Tipo = TipoCelda.Texto, Texto = valor, Formula = formula };
            case "b":
                return new Celda { Tipo = TipoCelda.Booleano, Numero = valor == "1" ? 1 : 0, Formula = formula };
            case "e":
                return new Celda { Tipo = TipoCelda.Error, Texto = valor, Formula = formula };
            case "d":
                return DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso)
                    ? new Celda { Tipo = TipoCelda.Fecha, Fecha = iso, Formula = formula }
                    : new Celda { Tipo = TipoCelda.Texto, Texto = valor, Formula = formula };
            default:
                if (valor is null || !double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero))
                {
                    return new Celda { Tipo = TipoCelda.Vacia, Formula = formula };
                }
                if (estilosFecha.Contains(estilo) && numero is > -1 and < 2958466)
                {
                    var fecha = DateTime.FromOADate(es1904 ? numero + 1462 : numero);
                    fecha = new DateTime(fecha.Year, fecha.Month, fecha.Day, fecha.Hour, fecha.Minute, fecha.Second,
                        DateTimeKind.Unspecified).AddSeconds(fecha.Millisecond >= 500 ? 1 : 0);
                    return new Celda { Tipo = TipoCelda.Fecha, Fecha = fecha, Numero = numero, Formula = formula };
                }
                return new Celda { Tipo = TipoCelda.Numero, Numero = numero, Formula = formula };
        }
    }

    private static List<string> LeerCadenasCompartidas(ZipArchive zip)
    {
        var lista = new List<string>();
        var documento = CargarXml(zip, "xl/sharedStrings.xml");
        if (documento?.Root is null) return lista;
        foreach (var si in documento.Root.Elements(Ns + "si"))
        {
            lista.Add(TextoDeCadena(si));
        }
        return lista;
    }

    /// <summary>Texto de un elemento si/is: &lt;t&gt; directo o la suma de los &lt;r&gt;&lt;t&gt; (sin textos fonéticos).</summary>
    private static string TextoDeCadena(XElement elemento)
    {
        var directo = elemento.Element(Ns + "t");
        if (directo is not null) return directo.Value;
        return string.Concat(elemento.Elements(Ns + "r").Select(r => r.Element(Ns + "t")?.Value ?? ""));
    }

    private static HashSet<int> LeerEstilosFecha(ZipArchive zip)
    {
        var resultado = new HashSet<int>();
        var documento = CargarXml(zip, "xl/styles.xml");
        if (documento?.Root is null) return resultado;

        var formatos = new Dictionary<int, string>();
        foreach (var formato in documento.Root.Element(Ns + "numFmts")?.Elements(Ns + "numFmt") ?? Enumerable.Empty<XElement>())
        {
            if (int.TryParse((string?)formato.Attribute("numFmtId"), out var id))
            {
                formatos[id] = (string?)formato.Attribute("formatCode") ?? "";
            }
        }

        var indice = 0;
        foreach (var xf in documento.Root.Element(Ns + "cellXfs")?.Elements(Ns + "xf") ?? Enumerable.Empty<XElement>())
        {
            var idFormato = int.TryParse((string?)xf.Attribute("numFmtId"), out var n) ? n : 0;
            if (EsFormatoFecha(idFormato, formatos.GetValueOrDefault(idFormato)))
            {
                resultado.Add(indice);
            }
            indice++;
        }
        return resultado;
    }

    private static bool EsFormatoFecha(int id, string? codigo)
    {
        if (id is >= 14 and <= 22 or >= 45 and <= 47) return true;
        if (string.IsNullOrEmpty(codigo)) return false;

        var limpio = new StringBuilder();
        var entreComillas = false;
        var entreCorchetes = false;
        foreach (var c in codigo)
        {
            if (c == '"') { entreComillas = !entreComillas; continue; }
            if (entreComillas) continue;
            if (c == '[') { entreCorchetes = true; continue; }
            if (c == ']') { entreCorchetes = false; continue; }
            if (entreCorchetes || c == '\\') continue;
            limpio.Append(char.ToLowerInvariant(c));
        }
        var texto = limpio.ToString();
        return texto.IndexOfAny(['d', 'm', 'y', 'h', 's']) >= 0 && !texto.Contains("general", StringComparison.Ordinal);
    }

    private static Dictionary<string, Relacion> LeerRelaciones(ZipArchive zip, string ruta, string directorioBase)
    {
        var resultado = new Dictionary<string, Relacion>(StringComparer.Ordinal);
        var documento = CargarXml(zip, ruta);
        if (documento?.Root is null) return resultado;
        foreach (var relacion in documento.Root.Elements(NsPaquete + "Relationship"))
        {
            var id = (string?)relacion.Attribute("Id");
            var destino = (string?)relacion.Attribute("Target");
            if (id is null || destino is null) continue;
            if ((string?)relacion.Attribute("TargetMode") == "External") continue;
            resultado[id] = new Relacion(ResolverRuta(directorioBase, destino), (string?)relacion.Attribute("Type") ?? "");
        }
        return resultado;
    }

    private static XDocument? CargarXml(ZipArchive zip, string ruta)
    {
        var entrada = zip.GetEntry(ruta);
        if (entrada is null) return null;
        using var flujo = entrada.Open();
        using var lector = XmlReader.Create(flujo, Opciones);
        return XDocument.Load(lector);
    }

    private static string RutaRelaciones(string ruta)
    {
        var directorio = Directorio(ruta);
        var archivo = ruta[directorio.Length..];
        return $"{directorio}_rels/{archivo}.rels";
    }

    private static string Directorio(string ruta)
    {
        var barra = ruta.LastIndexOf('/');
        return barra < 0 ? "" : ruta[..(barra + 1)];
    }

    private static string ResolverRuta(string directorioBase, string destino)
    {
        destino = destino.Replace('\\', '/');
        var completa = destino.StartsWith('/') ? destino.TrimStart('/') : directorioBase + destino;
        var partes = new List<string>();
        foreach (var parte in completa.Split('/'))
        {
            if (parte is "" or ".") continue;
            if (parte == "..")
            {
                if (partes.Count > 0) partes.RemoveAt(partes.Count - 1);
                continue;
            }
            partes.Add(parte);
        }
        return string.Join('/', partes);
    }

    /// <summary>"AB12" → (fila 12, columna 28).</summary>
    public static (int Fila, int Columna) ParsearReferencia(string referencia)
    {
        var columna = 0;
        var i = 0;
        while (i < referencia.Length && char.IsLetter(referencia[i]))
        {
            columna = columna * 26 + (char.ToUpperInvariant(referencia[i]) - 'A' + 1);
            i++;
        }
        var fila = int.TryParse(referencia.AsSpan(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0;
        return (fila, columna);
    }

    private static ((int Fila, int Columna) Inicio, (int Fila, int Columna) Fin) ParsearRango(string rango)
    {
        var partes = rango.Replace("$", "").Split(':');
        var inicio = ParsearReferencia(partes[0]);
        var fin = partes.Length > 1 ? ParsearReferencia(partes[1]) : inicio;
        return (inicio, fin);
    }
}
