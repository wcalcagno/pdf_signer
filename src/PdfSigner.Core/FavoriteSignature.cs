namespace PdfSigner.Core;

/// <summary>Firma guardada para reutilizar entre sesiones: imagen más bloque de texto.</summary>
/// <remarks>
/// La imagen se guarda aparte, como archivo, y aquí solo viaja su nombre. Meter los bytes de un
/// PNG dentro de este objeto obligaría a serializarlo en base64 en las preferencias, que no está
/// pensado para datos de ese tamaño.
///
/// El texto se guarda SIN resolver: si contiene <c>{fecha}</c>, debe seguir conteniéndolo, para
/// que al firmar dentro de seis meses se estampe la fecha de ese día y no la de hoy.
/// </remarks>
public sealed class FavoriteSignature
{
    /// <summary>Nombre del archivo de imagen dentro del almacenamiento de la aplicación.</summary>
    public string? ImageFileName { get; set; }

    public string Text { get; set; } = string.Empty;

    public double FontSizePt { get; set; } = 11;

    public string ColorHex { get; set; } = "#000000";

    public bool Bold { get; set; }

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageFileName);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool IsEmpty => !HasImage && !HasText;
}
