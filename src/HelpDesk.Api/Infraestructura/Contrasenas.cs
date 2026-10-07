using System.Security.Cryptography;

namespace HelpDesk.Api.Infraestructura;

/// <summary>Hash de contraseñas con PBKDF2-SHA256 (incluido en .NET, sin dependencias).</summary>
public static class Contrasenas
{
    private const string Prefijo = "pbkdf2-sha256";
    private const int Iteraciones = 600_000;
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;

    // Hash de referencia para igualar tiempos cuando el usuario no existe.
    private static readonly Lazy<string> HashFicticio = new(() => Hashear(Guid.NewGuid().ToString()));

    public static string Hashear(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, Iteraciones, HashAlgorithmName.SHA256, TamanoHash);
        return $"{Prefijo}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string password, string? almacenado)
    {
        if (string.IsNullOrEmpty(almacenado))
        {
            Verificar(password, HashFicticio.Value);
            return false;
        }

        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones))
        {
            return false;
        }

        byte[] sal;
        byte[] esperado;
        try
        {
            sal = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(password, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    /// <summary>Devuelve un mensaje si la contraseña no cumple los requisitos mínimos.</summary>
    public static string? Validar(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return "La contraseña debe tener al menos 8 caracteres.";
        }
        if (password.Length > 128)
        {
            return "La contraseña es demasiado larga.";
        }
        return null;
    }

    public static string Generar(int largo = 12)
    {
        const string caracteres = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(caracteres, largo);
    }
}
