namespace PdfSigner.Core;

/// <summary>Una página del PDF rasterizada a imagen, lista para mostrarse en pantalla.</summary>
public sealed record RenderedPage(byte[] PngData, int PixelWidth, int PixelHeight);

/// <summary>
/// Convierte una página del PDF en imagen para la vista previa.
/// </summary>
/// <remarks>
/// Por qué es una interfaz y no una implementación concreta: ninguna librería PDF libre en .NET
/// rasteriza de forma portable, pero los cuatro sistemas operativos ya traen su propio motor
/// (Windows.Data.Pdf, Android PdfRenderer, CoreGraphics). Cada plataforma implementa esto con
/// su API nativa, lo que evita arrastrar PDFium (~15-25 MB por ABI) y su conocido problema de
/// empaquetado en App Store.
///
/// Nota: la rasterización es SOLO vista previa; no interviene en el PDF exportado. Por eso el
/// Core puede testearse por completo en CI aunque esta interfaz no tenga implementación ahí.
/// </remarks>
public interface IPdfRasterizer
{
    /// <param name="targetWidthPx">Ancho deseado en píxeles; el alto se deriva manteniendo proporción.</param>
    Task<RenderedPage> RenderPageAsync(
        Stream pdf, int pageIndex, int targetWidthPx, CancellationToken ct = default);
}
