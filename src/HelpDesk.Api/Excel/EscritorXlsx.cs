using System.IO.Compression;

namespace HelpDesk.Api.Excel;

public enum FormatoColumna
{
    Texto,
    Entero,
    Decimal,
    FechaHora,
    Fecha,
}

public sealed record ColumnaXlsx(string Titulo, double Ancho, FormatoColumna Formato = FormatoColumna.Texto);

/// <summary>Una hoja a generar: encabezado + filas (cada fila es un arreglo con un valor por columna).</summary>
public sealed class HojaNueva
{
    public string Nombre { get; init; } = "Hoja1";
    public List<ColumnaXlsx> Columnas { get; init; } = [];
    public List<object?[]> Filas { get; init; } = [];
    /// <summary>Si es true, la última fila se escribe en negrita (fila de totales).</summary>
    public bool UltimaFilaEsTotal { get; init; }
}

/// <summary>
/// Generador mínimo de archivos .xlsx usando solo librerías de .NET.
/// Encabezado en negrita, primera fila fija, filtros y fechas reales de Excel.
/// </summary>
public static class EscritorXlsx
{
    public const string TipoContenido = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Índices de estilo (cellXfs en styles.xml)
    private const int EstiloEncabezado = 1;
    private const int EstiloFechaHora = 2;
    private const int EstiloFecha = 3;
    private const int EstiloEntero = 4;
    private const int EstiloDecimal = 5;
    private const int EstiloTotal = 6;

    public static byte[] Generar(params HojaNueva[] hojas)
    {
        if (hojas.Length == 0) throw new ArgumentException("Se necesita al menos una hoja.", nameof(hojas));

        var nombres = new List<string>();
        foreach (var hoja in hojas)
        {
            nombres.Add(NombreHojaValido(hoja.Nombre, nombres));
        }

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Agregar(zip, "[Content_Types].xml", TiposDeContenido(hojas.Length));
            Agregar(zip, "_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
                """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
                """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>""" +
                """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>""" +
                """<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>""" +
                "</Relationships>");
            Agregar(zip, "docProps/core.xml", Propiedades());
            Agregar(zip, "docProps/app.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
                """<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>Mesa de Ayuda ASSE</Application></Properties>""");
            Agregar(zip, "xl/workbook.xml", Libro(hojas, nombres));
            Agregar(zip, "xl/_rels/workbook.xml.rels", RelacionesLibro(hojas.Length));
            Agregar(zip, "xl/styles.xml", Estilos);
            for (var i = 0; i < hojas.Length; i++)
            {
                Agregar(zip, $"xl/worksheets/sheet{i + 1}.xml", Hoja(hojas[i]));
            }
        }
        return memoria.ToArray();
    }

    private static void Agregar(ZipArchive zip, string ruta, string contenido)
    {
        var entrada = zip.CreateEntry(ruta, CompressionLevel.Optimal);
        using var escritor = new StreamWriter(entrada.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        escritor.Write(contenido);
    }

    private static string TiposDeContenido(int cantidadHojas)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        sb.Append("""<Default Extension="xml" ContentType="application/xml"/>""");
        sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        for (var i = 1; i <= cantidadHojas; i++)
        {
            sb.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        }
        sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        sb.Append("""<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>""");
        sb.Append("""<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>""");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string Propiedades()
    {
        var ahora = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
               """<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">""" +
               "<dc:creator>Mesa de Ayuda ASSE</dc:creator>" +
               $"""<dcterms:created xsi:type="dcterms:W3CDTF">{ahora}</dcterms:created>""" +
               $"""<dcterms:modified xsi:type="dcterms:W3CDTF">{ahora}</dcterms:modified>""" +
               "</cp:coreProperties>";
    }

    private static string Libro(HojaNueva[] hojas, List<string> nombres)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        sb.Append("""<bookViews><workbookView activeTab="0"/></bookViews><sheets>""");
        for (var i = 0; i < hojas.Length; i++)
        {
            sb.Append($"""<sheet name="{Escapar(nombres[i])}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        }
        sb.Append("</sheets><definedNames>");
        for (var i = 0; i < hojas.Length; i++)
        {
            var ultimaFila = Math.Max(1, hojas[i].Filas.Count + 1 - (hojas[i].UltimaFilaEsTotal ? 1 : 0));
            var ultimaColumna = LetraColumna(Math.Max(1, hojas[i].Columnas.Count));
            var nombreHoja = nombres[i].Replace("'", "''");
            sb.Append($"""<definedName name="_xlnm._FilterDatabase" localSheetId="{i}" hidden="1">{Escapar($"'{nombreHoja}'!$A$1:${ultimaColumna}${ultimaFila}")}</definedName>""");
        }
        sb.Append("</definedNames></workbook>");
        return sb.ToString();
    }

    private static string RelacionesLibro(int cantidadHojas)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (var i = 1; i <= cantidadHojas; i++)
        {
            sb.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        sb.Append($"""<Relationship Id="rId{cantidadHojas + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private const string Estilos =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""" +
        """<numFmts count="2"><numFmt numFmtId="164" formatCode="dd/mm/yyyy hh:mm"/><numFmt numFmtId="165" formatCode="dd/mm/yyyy"/></numFmts>""" +
        """<fonts count="2">""" +
        """<font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font>""" +
        """<font><b/><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font>""" +
        """</fonts>""" +
        """<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>""" +
        """<fill><patternFill patternType="solid"><fgColor rgb="FFDCE6F1"/><bgColor indexed="64"/></patternFill></fill></fills>""" +
        """<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border>""" +
        """<border><left/><right/><top style="thin"><color auto="1"/></top><bottom/><diagonal/></border></borders>""" +
        """<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>""" +
        """<cellXfs count="7">""" +
        """<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>""" +
        """<xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/>""" +
        """<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>""" +
        """<xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>""" +
        """<xf numFmtId="1" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>""" +
        """<xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>""" +
        """<xf numFmtId="0" fontId="1" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1"/>""" +
        """</cellXfs>""" +
        """<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>""" +
        """</styleSheet>""";

    private static string Hoja(HojaNueva hoja)
    {
        var columnas = hoja.Columnas;
        var totalFilas = hoja.Filas.Count + 1;
        var ultimaColumna = LetraColumna(Math.Max(1, columnas.Count));
        var filasFiltro = Math.Max(1, totalFilas - (hoja.UltimaFilaEsTotal ? 1 : 0));

        var sb = new StringBuilder(4096 + hoja.Filas.Count * 256);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        sb.Append($"""<dimension ref="A1:{ultimaColumna}{totalFilas}"/>""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft" activeCell="A2" sqref="A2"/></sheetView></sheetViews>""");
        sb.Append("""<sheetFormatPr defaultRowHeight="15"/>""");

        if (columnas.Count > 0)
        {
            sb.Append("<cols>");
            for (var c = 0; c < columnas.Count; c++)
            {
                var ancho = columnas[c].Ancho.ToString("0.##", CultureInfo.InvariantCulture);
                sb.Append($"""<col min="{c + 1}" max="{c + 1}" width="{ancho}" customWidth="1"/>""");
            }
            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        sb.Append("""<row r="1">""");
        for (var c = 0; c < columnas.Count; c++)
        {
            CeldaTexto(sb, $"{LetraColumna(c + 1)}1", columnas[c].Titulo, EstiloEncabezado);
        }
        sb.Append("</row>");

        for (var f = 0; f < hoja.Filas.Count; f++)
        {
            var numeroFila = f + 2;
            var esTotal = hoja.UltimaFilaEsTotal && f == hoja.Filas.Count - 1;
            var valores = hoja.Filas[f];
            sb.Append($"""<row r="{numeroFila}">""");
            for (var c = 0; c < columnas.Count && c < valores.Length; c++)
            {
                var valor = valores[c];
                if (valor is null) continue;
                var referencia = $"{LetraColumna(c + 1)}{numeroFila}";
                switch (valor)
                {
                    case string texto:
                        if (texto.Length > 0) CeldaTexto(sb, referencia, texto, esTotal ? EstiloTotal : 0);
                        break;
                    case DateTime fecha:
                        var estiloFecha = columnas[c].Formato == FormatoColumna.Fecha ? EstiloFecha : EstiloFechaHora;
                        CeldaNumero(sb, referencia, fecha.ToOADate(), esTotal ? EstiloTotal : estiloFecha);
                        break;
                    case bool logico:
                        sb.Append($"""<c r="{referencia}" t="b"><v>{(logico ? 1 : 0)}</v></c>""");
                        break;
                    case int or long or short or byte:
                        CeldaNumero(sb, referencia, Convert.ToDouble(valor, CultureInfo.InvariantCulture), esTotal ? EstiloTotal : EstiloEntero);
                        break;
                    case double or float or decimal:
                        var estiloNumero = columnas[c].Formato == FormatoColumna.Entero ? EstiloEntero : EstiloDecimal;
                        CeldaNumero(sb, referencia, Convert.ToDouble(valor, CultureInfo.InvariantCulture), esTotal ? EstiloTotal : estiloNumero);
                        break;
                    default:
                        CeldaTexto(sb, referencia, Convert.ToString(valor, CultureInfo.InvariantCulture) ?? "", esTotal ? EstiloTotal : 0);
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");

        if (columnas.Count > 0)
        {
            sb.Append($"""<autoFilter ref="A1:{ultimaColumna}{filasFiltro}"/>""");
        }
        sb.Append("""<pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>""");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void CeldaTexto(StringBuilder sb, string referencia, string texto, int estilo)
    {
        if (texto.Length > 32767) texto = texto[..32767];
        var atributoEstilo = estilo == 0 ? "" : $" s=\"{estilo}\"";
        sb.Append($"""<c r="{referencia}" t="inlineStr"{atributoEstilo}><is><t xml:space="preserve">{Escapar(texto)}</t></is></c>""");
    }

    private static void CeldaNumero(StringBuilder sb, string referencia, double numero, int estilo)
    {
        var atributoEstilo = estilo == 0 ? "" : $" s=\"{estilo}\"";
        sb.Append($"""<c r="{referencia}"{atributoEstilo}><v>{numero.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
    }

    public static string LetraColumna(int numero)
    {
        var resultado = "";
        while (numero > 0)
        {
            var resto = (numero - 1) % 26;
            resultado = (char)('A' + resto) + resultado;
            numero = (numero - 1) / 26;
        }
        return resultado;
    }

    /// <summary>Escapa XML y quita caracteres de control no permitidos.</summary>
    private static string Escapar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                case '\t' or '\n' or '\r':
                    sb.Append(c);
                    break;
                default:
                    if (c >= ' ' && c != '￾' && c != '￿') sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string NombreHojaValido(string nombre, List<string> existentes)
    {
        var limpio = new string(nombre.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (limpio.Length == 0) limpio = "Hoja";
        if (limpio.Length > 31) limpio = limpio[..31];
        var candidato = limpio;
        var n = 2;
        while (existentes.Contains(candidato, StringComparer.OrdinalIgnoreCase))
        {
            var sufijo = $" ({n++})";
            candidato = (limpio.Length + sufijo.Length > 31 ? limpio[..(31 - sufijo.Length)] : limpio) + sufijo;
        }
        return candidato;
    }
}
