using System.Text.RegularExpressions;
using HelpDesk.Api.Excel;

namespace HelpDesk.Api.Modulos.Importacion;

public sealed record FechaInterpretada(DateTime? Fecha, string? Advertencia);

/// <summary>
/// Interpreta la columna "Fecha y hora de ingreso" del Excel histórico.
///
/// El registro usa texto con formato M/D/AAAA - H:MM ("11/5/2025 - 13:40"), con algunos
/// errores de tipeo ("1//22/2026", "3/25/20256", "910/2026 - 10:44", "9-46").
/// Como los IDs son correlativos, las filas vecinas sirven de referencia: cuando una fecha
/// admite varias correcciones se elige la que queda más cerca de sus vecinas, y cuando no
/// se puede interpretar se toma la de la fila anterior. Todo queda como advertencia.
/// </summary>
public static partial class InterpreteFechas
{
    [GeneratedRegex(@"(\d{1,2})\s*[:\-\.hH]\s*(\d{2})\s*$")]
    private static partial Regex Hora();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numeros();

    private sealed record Candidato(DateTime Dia, int Ediciones);

    private sealed record Analisis(List<Candidato> Candidatos, int? Hora, int? Minuto);

    /// <summary>
    /// Detecta si el archivo escribe primero el mes (M/D/AAAA) o el día (D/M/AAAA).
    /// </summary>
    public static bool DetectarMesPrimero(IEnumerable<Celda> celdas)
    {
        int evidenciaMesPrimero = 0, evidenciaDiaPrimero = 0;
        foreach (var celda in celdas)
        {
            if (celda.Tipo != TipoCelda.Texto || celda.Texto is null) continue;
            var numeros = Numeros().Matches(celda.Texto);
            if (numeros.Count < 3) continue;
            if (!int.TryParse(numeros[0].Value, out var a) || !int.TryParse(numeros[1].Value, out var b)) continue;
            if (a <= 12 && b is > 12 and <= 31) evidenciaMesPrimero++;
            if (b <= 12 && a is > 12 and <= 31) evidenciaDiaPrimero++;
        }
        return evidenciaMesPrimero >= evidenciaDiaPrimero;
    }

    public static List<FechaInterpretada> Interpretar(IReadOnlyList<Celda> celdas, bool mesPrimero)
    {
        // 1) Fechas inequívocas: sirven de referencia para las demás.
        var limpias = new DateTime?[celdas.Count];
        for (var i = 0; i < celdas.Count; i++)
        {
            limpias[i] = FechaInequivoca(celdas[i], mesPrimero);
        }

        var resultado = new List<FechaInterpretada>(celdas.Count);
        for (var i = 0; i < celdas.Count; i++)
        {
            var (mediana, anterior) = Referencias(limpias, i);
            var celda = celdas[i];
            var original = celda.ComoTexto();

            if (limpias[i] is DateTime fecha)
            {
                if (mediana is DateTime m && Math.Abs((fecha - m).TotalDays) > 180)
                {
                    // Error típico de año (2/4/2025 entre fechas de 2026).
                    foreach (var delta in new[] { -1, 1 })
                    {
                        var alternativa = SumarAnios(fecha, delta);
                        if (alternativa is DateTime alt && Math.Abs((alt - m).TotalDays) <= 7)
                        {
                            resultado.Add(new FechaInterpretada(alt, $"Año corregido (original: \"{original}\")."));
                            goto siguiente;
                        }
                    }
                    resultado.Add(new FechaInterpretada(fecha, $"Fecha fuera de secuencia respecto de las filas vecinas (original: \"{original}\")."));
                }
                else if (mediana is DateTime m2 && Math.Abs((fecha - m2).TotalDays) > 7)
                {
                    resultado.Add(new FechaInterpretada(fecha, $"Fecha fuera de secuencia respecto de las filas vecinas (original: \"{original}\")."));
                }
                else
                {
                    resultado.Add(new FechaInterpretada(fecha, null));
                }
                goto siguiente;
            }

            if (celda.EstaVacia)
            {
                resultado.Add(anterior is DateTime previa
                    ? new FechaInterpretada(previa, "Sin fecha: se tomó la de la fila anterior.")
                    : new FechaInterpretada(null, "Sin fecha y sin una fila anterior de referencia."));
                goto siguiente;
            }

            var analisis = Analizar(original ?? "", mesPrimero);
            if (analisis.Candidatos.Count > 0 && mediana is DateTime referencia)
            {
                var elegido = analisis.Candidatos
                    .OrderBy(c => Math.Abs((c.Dia - referencia).TotalDays) > 7 ? 1 : 0)
                    .ThenBy(c => c.Ediciones)
                    .ThenBy(c => Math.Abs((c.Dia - referencia).TotalSeconds))
                    .First();
                if (analisis.Hora is int h && analisis.Minuto is int mi)
                {
                    resultado.Add(new FechaInterpretada(elegido.Dia.AddHours(h).AddMinutes(mi),
                        $"Fecha corregida (original: \"{original}\")."));
                }
                else
                {
                    var hora = anterior is DateTime previa && previa.Date == elegido.Dia ? previa.TimeOfDay : TimeSpan.Zero;
                    resultado.Add(new FechaInterpretada(elegido.Dia + hora, $"Hora faltante (original: \"{original}\")."));
                }
                goto siguiente;
            }

            resultado.Add(anterior is DateTime anteriorValida
                ? new FechaInterpretada(anteriorValida, $"Fecha ilegible (\"{original}\"): se tomó la de la fila anterior.")
                : new FechaInterpretada(null, $"Fecha ilegible (\"{original}\")."));

            siguiente:;
        }
        return resultado;
    }

    private static DateTime? FechaInequivoca(Celda celda, bool mesPrimero)
    {
        switch (celda.Tipo)
        {
            case TipoCelda.Fecha:
                return celda.Fecha;
            case TipoCelda.Numero when celda.Numero is > 36526 and < 73051: // años 2000 a 2100 como número de serie
                var serie = DateTime.FromOADate(celda.Numero);
                return new DateTime(serie.Year, serie.Month, serie.Day, serie.Hour, serie.Minute, 0);
            case TipoCelda.Texto when celda.Texto is not null:
                var analisis = Analizar(celda.Texto, mesPrimero);
                var exactos = analisis.Candidatos.Where(c => c.Ediciones == 0).ToList();
                if (exactos.Count == 1 && analisis.Hora is int h && analisis.Minuto is int m)
                {
                    return exactos[0].Dia.AddHours(h).AddMinutes(m);
                }
                return null;
            default:
                return null;
        }
    }

    private static (DateTime? Mediana, DateTime? Anterior) Referencias(DateTime?[] limpias, int indice)
    {
        var previas = new List<DateTime>();
        for (var j = indice - 1; j >= 0 && j >= indice - 8 && previas.Count < 3; j--)
        {
            if (limpias[j] is DateTime d) previas.Add(d);
        }
        var siguientes = new List<DateTime>();
        for (var j = indice + 1; j < limpias.Length && j <= indice + 8 && siguientes.Count < 3; j++)
        {
            if (limpias[j] is DateTime d) siguientes.Add(d);
        }

        var vecinas = previas.Concat(siguientes).OrderBy(d => d).ToList();
        DateTime? mediana = vecinas.Count == 0 ? null : vecinas[vecinas.Count / 2];

        DateTime? anterior = previas.Count > 0 ? previas[0] : null;
        if (anterior is null)
        {
            for (var j = indice - 1; j >= 0; j--)
            {
                if (limpias[j] is DateTime d) { anterior = d; break; }
            }
        }
        anterior ??= siguientes.Count > 0 ? siguientes[0] : null;
        return (mediana, anterior);
    }

    private static Analisis Analizar(string texto, bool mesPrimero)
    {
        var limpio = texto.Trim();
        int? hora = null, minuto = null;
        var parteFecha = limpio;
        var coincidenciaHora = Hora().Match(limpio);
        if (coincidenciaHora.Success)
        {
            var h = int.Parse(coincidenciaHora.Groups[1].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(coincidenciaHora.Groups[2].Value, CultureInfo.InvariantCulture);
            parteFecha = limpio[..coincidenciaHora.Index];
            if (h <= 23 && m <= 59)
            {
                hora = h;
                minuto = m;
            }
        }

        var grupos = Numeros().Matches(parteFecha).Select(x => x.Value).ToList();
        var combinaciones = new List<(string A, string B, string Anio, int Ediciones)>();
        if (grupos.Count == 3)
        {
            combinaciones.Add((grupos[0], grupos[1], grupos[2], 0));
        }
        else if (grupos.Count == 2 && grupos[0].Length is 3 or 4)
        {
            // "910/2026" → 9/10/2026 o 91/0/2026 ...
            for (var i = 1; i < grupos[0].Length; i++)
            {
                combinaciones.Add((grupos[0][..i], grupos[0][i..], grupos[1], 1));
            }
        }

        var candidatos = new List<Candidato>();
        foreach (var (a, b, anio, edicionesBase) in combinaciones)
        {
            // Orden del archivo (0 ediciones) y orden invertido (1 edición).
            foreach (var invertido in new[] { false, true })
            {
                var mesPrimeroAhora = mesPrimero != invertido;
                var grupoMes = mesPrimeroAhora ? a : b;
                var grupoDia = mesPrimeroAhora ? b : a;
                foreach (var (mes, edMes) in VariantesChicas(grupoMes, 12))
                foreach (var (dia, edDia) in VariantesChicas(grupoDia, 31))
                foreach (var (anioValor, edAnio) in VariantesAnio(anio))
                {
                    if (dia > DateTime.DaysInMonth(anioValor, mes)) continue;
                    candidatos.Add(new Candidato(new DateTime(anioValor, mes, dia),
                        edicionesBase + (invertido ? 1 : 0) + edMes + edDia + edAnio));
                }
            }
        }
        return new Analisis(candidatos, hora, minuto);
    }

    /// <summary>Valores posibles para día o mes; quitar un dígito cuenta como una edición.</summary>
    private static List<(int Valor, int Ediciones)> VariantesChicas(string grupo, int maximo)
    {
        var resultado = new List<(int, int)>();
        if (grupo.Length is >= 1 and <= 2 && int.TryParse(grupo, out var exacto) && exacto >= 1 && exacto <= maximo)
        {
            resultado.Add((exacto, 0));
        }
        if (grupo.Length >= 2)
        {
            for (var i = 0; i < grupo.Length; i++)
            {
                var recortado = grupo.Remove(i, 1);
                if (recortado.Length is >= 1 and <= 2 && int.TryParse(recortado, out var valor) && valor >= 1 && valor <= maximo)
                {
                    resultado.Add((valor, 1));
                }
            }
        }
        return resultado;
    }

    private static List<(int Valor, int Ediciones)> VariantesAnio(string grupo)
    {
        var resultado = new List<(int, int)>();
        if (grupo.Length == 4 && int.TryParse(grupo, out var exacto)) resultado.Add((exacto, 0));
        else if (grupo.Length == 2 && int.TryParse(grupo, out var corto)) resultado.Add((2000 + corto, 1));
        else if (grupo.Length == 3 && grupo[0] == '0' && int.TryParse(grupo, out var tres)) resultado.Add((2000 + tres, 1));

        if (grupo.Length is 4 or 5)
        {
            for (var i = 0; i < grupo.Length; i++)
            {
                var recortado = grupo.Remove(i, 1);
                if (recortado.Length == 4 && int.TryParse(recortado, out var valor)) resultado.Add((valor, 1));
            }
        }
        return resultado.Where(x => x.Item1 is >= 2000 and <= 2100).ToList();
    }

    private static DateTime? SumarAnios(DateTime fecha, int anios)
    {
        try
        {
            return fecha.AddYears(anios);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
