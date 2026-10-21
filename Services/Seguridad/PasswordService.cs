using System.Security.Cryptography;

namespace Routify.Web.Services.Seguridad;

public interface IPasswordService
{
    string Hash(string clave);
    bool Verificar(string clave, string almacenado);
}

/// <summary>
/// Hash de contraseñas con PBKDF2-HMAC-SHA256 y sal aleatoria por usuario (RNF-07).
/// Formato almacenado: v1.{iteraciones}.{salBase64}.{hashBase64}
/// </summary>
public class PasswordService : IPasswordService
{
    private const int Iteraciones = 100_000;
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;

    public string Hash(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, Iteraciones, HashAlgorithmName.SHA256, TamanoHash);
        return $"v1.{Iteraciones}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string clave, string almacenado)
    {
        if (string.IsNullOrEmpty(almacenado)) return false;
        var partes = almacenado.Split('.');
        if (partes.Length != 4 || partes[0] != "v1" || !int.TryParse(partes[1], out int iter) || iter < 1) return false;

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(clave, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Política mínima: 8 caracteres, mayúscula, minúscula y dígito. Devuelve null si es válida.</summary>
    public static string? Validar(string? clave)
    {
        if (string.IsNullOrEmpty(clave) || clave.Length < 8) return "La contraseña debe tener al menos 8 caracteres.";
        if (!clave.Any(char.IsUpper)) return "La contraseña debe incluir una letra mayúscula.";
        if (!clave.Any(char.IsLower)) return "La contraseña debe incluir una letra minúscula.";
        if (!clave.Any(char.IsDigit)) return "La contraseña debe incluir un número.";
        return null;
    }
}
