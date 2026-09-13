using System.Reflection;
using PdfSharp.Fonts;

namespace PdfSigner.Core;

/// <summary>Resuelve las fuentes desde recursos embebidos en este ensamblado.</summary>
/// <remarks>
/// Por qué es obligatorio y no una comodidad: el build Core de PDFsharp no tiene estrategia de
/// fuentes propia, y en iOS/Android no hay una carpeta de fuentes del sistema accesible. Sin un
/// IFontResolver, cualquier DrawString revienta en móvil.
///
/// Además, embeber los .ttf evita depender de la reflexión sobre el sistema de archivos, que es
/// justo lo que el trimmer de iOS (activo por defecto en Release) tiende a romper.
/// </remarks>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Open Sans";

    private const string RegularFace = "OpenSans#Regular";
    private const string BoldFace = "OpenSans#Bold";

    private static readonly Lazy<byte[]> Regular = new(() => Load("OpenSans-Regular.ttf"));
    private static readonly Lazy<byte[]> Bold = new(() => Load("OpenSans-Bold.ttf"));

    private static bool _registered;
    private static readonly Lock RegistrationLock = new();

    /// <summary>Registra el resolver globalmente. Idempotente: llamarlo de más no hace daño.</summary>
    public static void Register()
    {
        lock (RegistrationLock)
        {
            if (_registered)
                return;

            GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        // Se ignora el nombre de familia pedido a propósito: solo embebemos una familia, y así
        // el documento se ve idéntico en las cuatro plataformas en vez de caer en sustituciones.
        => new FontResolverInfo(isBold ? BoldFace : RegularFace);

    public byte[]? GetFont(string faceName)
        => faceName == BoldFace ? Bold.Value : Regular.Value;

    private static byte[] Load(string fileName)
    {
        var asm = typeof(EmbeddedFontResolver).GetTypeInfo().Assembly;
        var resource = $"PdfSigner.Core.Fonts.{fileName}";

        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{resource}'. " +
                $"Recursos disponibles: {string.Join(", ", asm.GetManifestResourceNames())}");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
