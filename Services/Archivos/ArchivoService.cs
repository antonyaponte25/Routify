using Routify.Web.Helpers;

namespace Routify.Web.Services.Archivos;

public interface IArchivoService
{
    /// <summary>Guarda la evidencia en disco y devuelve la ruta relativa. Lanza InvalidOperationException si el archivo no es válido.</summary>
    Task<string> GuardarPruebaAsync(int pedidoId, Stream contenido, string tipo, CancellationToken ct = default);

    /// <summary>Ruta física de un archivo guardado (valida que no salga de la carpeta de uploads).</summary>
    string? RutaFisica(string relativa);
}

/// <summary>
/// Almacenamiento de fotografías y firmas en disco, FUERA de wwwroot (App_Data/uploads).
/// Se sirven únicamente mediante un controlador que exige sesión iniciada.
/// </summary>
public class ArchivoService : IArchivoService
{
    private readonly string _raiz;
    private readonly long _maxBytes;

    public ArchivoService(IConfiguration cfg, IWebHostEnvironment env)
    {
        var ruta = cfg["Archivos:RutaUploads"] ?? "App_Data/uploads";
        _raiz = Path.GetFullPath(Path.IsPathRooted(ruta) ? ruta : Path.Combine(env.ContentRootPath, ruta));
        _maxBytes = (long)(cfg.GetValue("Archivos:TamanoMaximoMB", 5)) * 1024 * 1024;
        Directory.CreateDirectory(_raiz);
    }

    public async Task<string> GuardarPruebaAsync(int pedidoId, Stream contenido, string tipo, CancellationToken ct = default)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await contenido.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + leidos > _maxBytes)
                throw new InvalidOperationException($"El archivo supera el tamaño máximo de {_maxBytes / 1024 / 1024} MB.");
            memoria.Write(buffer, 0, leidos);
        }

        var bytes = memoria.ToArray();
        string? extension = DetectarExtension(bytes);
        if (extension == null)
            throw new InvalidOperationException("Formato de imagen no permitido (solo JPEG, PNG o WebP).");
        if (tipo == TipoPrueba.Firma && extension != ".png")
            throw new InvalidOperationException("La firma debe ser una imagen PNG.");

        string relativa = Path.Combine("pruebas", pedidoId.ToString(), $"{Guid.NewGuid():N}{extension}");
        string fisica = Path.GetFullPath(Path.Combine(_raiz, relativa));
        Directory.CreateDirectory(Path.GetDirectoryName(fisica)!);
        await File.WriteAllBytesAsync(fisica, bytes, ct);

        return relativa.Replace('\\', '/');
    }

    public string? RutaFisica(string relativa)
    {
        if (string.IsNullOrWhiteSpace(relativa)) return null;
        var completa = Path.GetFullPath(Path.Combine(_raiz, relativa));
        if (!completa.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(completa) ? completa : null;
    }

    /// <summary>Valida el contenido real del archivo por sus bytes iniciales (no por la extensión declarada).</summary>
    private static string? DetectarExtension(byte[] b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return null;
    }
}
