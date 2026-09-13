namespace PdfSigner.App.Services;

/// <summary>Entrega el PDF firmado al usuario por el cauce propio de cada plataforma.</summary>
/// <remarks>
/// No hay un gesto común: en escritorio se espera un diálogo de "Guardar como" y en móvil una
/// hoja de compartir. Forzar el mismo comportamiento en todas partes daría una app que se siente
/// extraña en al menos dos de las cuatro plataformas.
/// </remarks>
public interface IFileExporter
{
    /// <returns>true si el archivo se entregó; false si el usuario canceló.</returns>
    Task<bool> ExportAsync(string suggestedFileName, byte[] data, CancellationToken ct = default);
}
