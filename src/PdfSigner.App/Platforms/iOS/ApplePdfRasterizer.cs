using CoreGraphics;
using Foundation;
using PdfSigner.Core;
using UIKit;

namespace PdfSigner.App.Platforms.Apple;

/// <summary>Rasteriza páginas con CoreGraphics, compartido por iOS y macCatalyst.</summary>
/// <remarks>
/// El csproj enlaza este archivo también desde macCatalyst: la API de CoreGraphics es idéntica
/// en ambos, así que duplicarlo no aportaría nada.
///
/// Es la pieza que más pesa en la decisión de no usar PDFium: la incidencia abierta de
/// PDFtoImage (#141) describe rechazos de App Store porque libpdfium.dylib no viaja empaquetado
/// como framework. CoreGraphics ya está en el sistema y no tiene ese problema.
/// </remarks>
public sealed class ApplePdfRasterizer : IPdfRasterizer
{
    public Task<RenderedPage> RenderPageAsync(
        Stream pdf, int pageIndex, int targetWidthPx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidthPx);

        if (pdf.CanSeek)
            pdf.Position = 0;

        using var buffer = new MemoryStream();
        pdf.CopyTo(buffer);

        using var data = NSData.FromArray(buffer.ToArray());
        using var provider = new CGDataProvider(data);
        using var document = CGPDFDocument.FromProvider(provider)
            ?? throw new InvalidOperationException("El archivo no se pudo leer como PDF.");

        if (pageIndex < 0 || pageIndex >= document.Pages)
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"La página {pageIndex} no existe; el documento tiene {document.Pages}.");

        // CGPDFDocument numera las páginas desde 1.
        using var page = document.GetPage(pageIndex + 1)
            ?? throw new InvalidOperationException($"No se pudo abrir la página {pageIndex}.");

        var box = page.GetBoxRect(CGPDFBox.Media);

        // A diferencia de Windows y Android, aquí la rotación NO viene aplicada al tamaño:
        // hay que intercambiar ancho y alto a mano si la página está girada un cuarto de vuelta.
        var rotation = ((page.RotationAngle % 360) + 360) % 360;
        var turned = rotation is 90 or 270;
        var visibleWidth = turned ? box.Height : box.Width;
        var visibleHeight = turned ? box.Width : box.Height;

        var height = (int)Math.Max(1, Math.Round(targetWidthPx * visibleHeight / visibleWidth));
        var size = new CGSize(targetWidthPx, height);

        var renderer = new UIGraphicsImageRenderer(size, new UIGraphicsImageRendererFormat { Scale = 1 });
        var image = renderer.CreateImage(context =>
        {
            var cg = context.CGContext;

            // Sin fondo blanco la página sale transparente, igual que en Android.
            cg.SetFillColor(1f, 1f, 1f, 1f);
            cg.FillRect(new CGRect(CGPoint.Empty, size));

            // CoreGraphics tiene el origen abajo-izquierda; se voltea para que coincida con el
            // sistema de coordenadas de la imagen resultante.
            cg.TranslateCTM(0, height);
            cg.ScaleCTM(1, -1);

            // GetDrawingTransform resuelve de una vez el encaje en el destino y la rotación
            // declarada en la página; hacerlo a mano es una fuente conocida de errores.
            cg.ConcatCTM(page.GetDrawingTransform(
                CGPDFBox.Media, new CGRect(CGPoint.Empty, size), 0, true));

            cg.DrawPDFPage(page);
        });

        ct.ThrowIfCancellationRequested();

        using var png = image.AsPNG()
            ?? throw new InvalidOperationException("No se pudo codificar la página como PNG.");

        return Task.FromResult(new RenderedPage(png.ToArray(), targetWidthPx, height));
    }
}
