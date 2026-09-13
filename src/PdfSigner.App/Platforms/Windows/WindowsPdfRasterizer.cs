using PdfSigner.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PdfSigner.App.Platforms.Windows;

/// <summary>Rasteriza páginas con el motor PDF que ya trae Windows.</summary>
/// <remarks>
/// Windows.Data.Pdf forma parte del sistema desde Windows 8.1, así que no añade ni un byte al
/// instalador. La alternativa era empaquetar PDFium (~15-25 MB por arquitectura) únicamente
/// para dibujar una vista previa.
///
/// Ventaja adicional: este motor aplica /Rotate por su cuenta y devuelve la página tal como el
/// usuario la ve. Eso encaja con el "espacio visible" que usa PageGeometry y evita compensar
/// la rotación por segunda vez en la interfaz.
/// </remarks>
public sealed class WindowsPdfRasterizer : IPdfRasterizer
{
    public async Task<RenderedPage> RenderPageAsync(
        Stream pdf, int pageIndex, int targetWidthPx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidthPx);

        // La API de WinRT necesita un IRandomAccessStream, no un Stream de .NET.
        using var input = new InMemoryRandomAccessStream();
        if (pdf.CanSeek)
            pdf.Position = 0;

        // El adaptador que devuelve AsStreamForWrite escribe en un búfer propio y NO vuelca
        // al IRandomAccessStream hasta que se le hace flush. Sin esta llamada, PdfDocument
        // recibe un flujo vacío y falla con 0x8004808E ("Invalid file, zero length"), que no
        // sugiere en absoluto cuál es la causa.
        var destino = input.AsStreamForWrite();
        await pdf.CopyToAsync(destino, ct).ConfigureAwait(false);
        await destino.FlushAsync(ct).ConfigureAwait(false);

        input.Seek(0);

        var document = await PdfDocument.LoadFromStreamAsync(input).AsTask(ct).ConfigureAwait(false);

        if (pageIndex < 0 || pageIndex >= document.PageCount)
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"La página {pageIndex} no existe; el documento tiene {document.PageCount}.");

        using var page = document.GetPage((uint)pageIndex);

        // page.Size ya viene con la rotación aplicada: es el tamaño que se ve en pantalla.
        var height = (uint)Math.Max(1, Math.Round(targetWidthPx * page.Size.Height / page.Size.Width));

        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)targetWidthPx,
            DestinationHeight = height,
        }).AsTask(ct).ConfigureAwait(false);

        output.Seek(0);
        var bytes = new byte[output.Size];
        await output.AsStreamForRead().ReadExactlyAsync(bytes, ct).ConfigureAwait(false);

        return new RenderedPage(bytes, targetWidthPx, (int)height);
    }
}
