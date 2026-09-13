using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using PdfSigner.Core;

namespace PdfSigner.App.Platforms.Android;

/// <summary>Rasteriza páginas con android.graphics.pdf.PdfRenderer, parte del sistema.</summary>
/// <remarks>
/// Disponible desde API 21, así que cubre cualquier dispositivo que hoy tenga sentido soportar
/// y no suma peso al APK.
///
/// La pega de esta API es que exige un descriptor de archivo con acceso aleatorio: no acepta un
/// Stream en memoria. Por eso el PDF se vuelca a un temporal en la caché, que se borra al salir.
/// </remarks>
public sealed class AndroidPdfRasterizer : IPdfRasterizer
{
    public async Task<RenderedPage> RenderPageAsync(
        Stream pdf, int pageIndex, int targetWidthPx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidthPx);

        // Path y Color van cualificados: Android.Graphics define los suyos y chocan con
        // System.IO.Path y Microsoft.Maui.Graphics.Color.
        var tempPath = System.IO.Path.Combine(FileSystem.CacheDirectory, $"render-{Guid.NewGuid():N}.pdf");

        try
        {
            await using (var file = File.Create(tempPath))
            {
                if (pdf.CanSeek)
                    pdf.Position = 0;
                await pdf.CopyToAsync(file, ct).ConfigureAwait(false);
            }

            using var descriptor = ParcelFileDescriptor.Open(
                    new Java.IO.File(tempPath), ParcelFileMode.ReadOnly)
                ?? throw new InvalidOperationException("No se pudo abrir el PDF temporal.");

            using var renderer = new PdfRenderer(descriptor);

            if (pageIndex < 0 || pageIndex >= renderer.PageCount)
                throw new ArgumentOutOfRangeException(
                    nameof(pageIndex),
                    $"La página {pageIndex} no existe; el documento tiene {renderer.PageCount}.");

            using var page = renderer.OpenPage(pageIndex)
                ?? throw new InvalidOperationException($"No se pudo abrir la página {pageIndex}.");

            var height = Math.Max(1, (int)Math.Round(targetWidthPx * (double)page.Height / page.Width));

            using var bitmap = Bitmap.CreateBitmap(targetWidthPx, height, Bitmap.Config.Argb8888!)
                ?? throw new InvalidOperationException("No se pudo reservar el bitmap de la página.");

            // PdfRenderer dibuja sobre fondo transparente. Sin pintarlo de blanco antes, una
            // página normal se vería como un rectángulo transparente sobre el fondo de la app.
            bitmap.EraseColor(Android.Graphics.Color.White.ToArgb());
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

            using var ms = new MemoryStream();
            await bitmap.CompressAsync(Bitmap.CompressFormat.Png!, 100, ms).ConfigureAwait(false);

            return new RenderedPage(ms.ToArray(), targetWidthPx, height);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Un temporal que no se pudo borrar no justifica tumbar la vista previa;
                // el sistema limpia la caché por su cuenta.
            }
        }
    }
}
