using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Routify.Web.Services.Seguridad;

/// <summary>Generación de tokens aleatorios no predecibles (RNF-08) y su hash.</summary>
public static class Tokens
{
    /// <summary>Token URL-seguro de 32 bytes aleatorios (43 caracteres).</summary>
    public static string Nuevo(int bytes = 32) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
