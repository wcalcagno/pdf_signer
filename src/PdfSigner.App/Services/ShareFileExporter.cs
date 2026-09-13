namespace PdfSigner.App.Services;

/// <summary>Entrega el PDF mediante la hoja de compartir del sistema.</summary>
/// <remarks>
/// Es la vía natural en iOS, Android y macCatalyst: desde ahí el usuario puede guardarlo en
/// Archivos, mandarlo por correo o abrirlo en otra app, sin que nosotros pidamos permisos de
/// almacenamiento. El archivo se deja en la caché, que el sistema limpia por su cuenta.
/// </remarks>
public sealed class ShareFileExporter : IFileExporter
{
    public async Task<bool> ExportAsync(
        string suggestedFileName, byte[] data, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentNullException.ThrowIfNull(data);

        var path = Path.Combine(FileSystem.CacheDirectory, suggestedFileName);
        await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Compartir PDF firmado",
            File = new ShareFile(path),
        }).ConfigureAwait(false);

        // La hoja de compartir no informa de si el usuario completó la acción o la descartó,
        // así que se considera entregado en cuanto se muestra.
        return true;
    }
}
