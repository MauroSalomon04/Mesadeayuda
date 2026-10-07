using System.Text.RegularExpressions;

namespace HelpDesk.Api.Infraestructura;

/// <summary>Utilidades de texto: limpieza, normalización sin tildes y palabras clave.</summary>
public static partial class Texto
{
    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "a", "al", "ante", "con", "como", "cual", "da", "de", "del", "desde", "dice", "donde", "el", "ella",
        "en", "entre", "es", "esa", "ese", "eso", "esta", "este", "esto", "fue", "ha", "hay", "la", "las",
        "le", "les", "lo", "los", "mas", "me", "mi", "muy", "no", "nos", "o", "para", "pero", "por", "que",
        "se", "ser", "si", "sin", "sobre", "son", "su", "sus", "te", "tiene", "un", "una", "uno", "unos",
        "unas", "y", "ya", "puede", "pide", "solicita", "quiere", "le", "usuario", "usuaria"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NoAlfanumerico();

    /// <summary>Recorta espacios; devuelve null si queda vacío. Conserva saltos de línea.</summary>
    public static string? Limpiar(string? valor)
    {
        if (valor is null) return null;
        var recortado = valor.Trim();
        return recortado.Length == 0 ? null : recortado;
    }

    /// <summary>Recorta y colapsa espacios internos (para nombres de una línea).</summary>
    public static string? LimpiarLinea(string? valor)
    {
        var limpio = Limpiar(valor);
        return limpio is null ? null : Espacios().Replace(limpio, " ");
    }

    /// <summary>Minúsculas, sin tildes y con espacios colapsados. "Contraseña " → "contrasena".</summary>
    public static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        var descompuesto = valor.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return Espacios().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Clave para agrupar textos parecidos (sin tildes, sin signos finales).</summary>
    public static string ClaveAgrupacion(string? valor) =>
        Normalizar(valor).TrimEnd('.', ',', ';', ':', '-', ' ');

    /// <summary>Palabras significativas de un texto, normalizadas y sin repetir.</summary>
    public static List<string> PalabrasClave(string? valor, int maximo = 8)
    {
        var resultado = new List<string>();
        foreach (var palabra in NoAlfanumerico().Split(Normalizar(valor)))
        {
            if (palabra.Length < 3 && !palabra.Any(char.IsDigit)) continue;
            if (PalabrasVacias.Contains(palabra)) continue;
            if (resultado.Contains(palabra)) continue;
            resultado.Add(palabra);
            if (resultado.Count >= maximo) break;
        }
        return resultado;
    }

    /// <summary>Escapa los comodines de LIKE de PostgreSQL (el carácter de escape por defecto es la barra invertida).</summary>
    public static string EscaparLike(string valor) =>
        valor.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Recorta un texto a un largo máximo.</summary>
    public static string? Recortar(string? valor, int largo) =>
        valor is null || valor.Length <= largo ? valor : valor[..largo];
}
