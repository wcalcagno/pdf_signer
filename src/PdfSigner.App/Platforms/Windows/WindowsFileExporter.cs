using PdfSigner.App.Services;
using Windows.Storage.Pickers;

namespace PdfSigner.App.Platforms.Windows;

/// <summary>Muestra el diálogo nativo de "Guardar como" de Windows.</summary>
/// <remarks>
/// En escritorio la hoja de compartir se siente fuera de lugar: lo que el usuario espera es
/// elegir carpeta y nombre. FileSavePicker es parte del sistema, así que no añade dependencias.
///
/// El detalle importante es InitializeWithWindow: en una app desempaquetada (WindowsPackageType
/// None, como esta) el selector no sabe a qué ventana asociarse y lanza una excepción si no se
/// le pasa el handle a mano.
/// </remarks>
public sealed class WindowsFileExporter : IFileExporter
{
    public async Task<bool> ExportAsync(
        string suggestedFileName, byte[] data, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentNullException.ThrowIfNull(data);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
        };
        picker.FileTypeChoices.Add("Documento PDF", [".pdf"]);

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not MauiWinUIWindow platformWindow)
            throw new InvalidOperationException("No se pudo localizar la ventana de la aplicación.");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, platformWindow.WindowHandle);

        var file = await picker.PickSaveFileAsync().AsTask(ct).ConfigureAwait(false);
        if (file is null)
            return false;

        await File.WriteAllBytesAsync(file.Path, data, ct).ConfigureAwait(false);
        return true;
    }
}
