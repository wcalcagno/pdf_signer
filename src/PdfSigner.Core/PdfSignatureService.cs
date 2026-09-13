using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfSigner.Core;

/// <summary>
/// Inserta imágenes y bloques de texto en un PDF existente como contenido real del documento.
/// </summary>
/// <remarks>
/// Esto es firma VISUAL, no criptográfica: no hay PAdES ni certificados X.509. Equivale a
/// estampar una firma sobre el papel, no a probar matemáticamente la autoría.
///
/// El resultado NO es una captura de pantalla: el texto queda seleccionable y buscable, y la
/// imagen se embebe como XObject conservando su canal alfa. Un PDF de 88 KB en vez de los
/// varios MB que produce rasterizar la página.
/// </remarks>
public sealed class PdfSignatureService
{
    public PdfSignatureService() => EmbeddedFontResolver.Register();

    /// <summary>Lee la geometría de cada página sin modificar el documento.</summary>
    public IReadOnlyList<PageGeometry> Inspect(Stream pdf)
    {
        using var doc = Abrir(pdf, PdfDocumentOpenMode.Import);
        return [.. doc.Pages.Cast<PdfPage>().Select(PageGeometry.FromPage)];
    }

    /// <summary>Abre el documento traduciendo los fallos a mensajes que el usuario entienda.</summary>
    private static PdfDocument Abrir(Stream pdf, PdfDocumentOpenMode modo)
    {
        try
        {
            return PdfReader.Open(AsSeekable(pdf), modo);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfSignerException(
                "El PDF está protegido con contraseña. Quítale la protección antes de firmarlo.", ex);
        }
        catch (Exception ex) when (ex is not PdfSignerException)
        {
            // Se agrupa todo lo demás a propósito: PDFsharp lanza desde ArgumentOutOfRangeException
            // hasta InvalidOperationException según por dónde se rompa el archivo, y para quien
            // firma un contrato la causa concreta es irrelevante.
            throw new PdfSignerException(
                "No se pudo leer el archivo: no parece un PDF válido o está dañado.", ex);
        }
    }

    /// <summary>
    /// Escribe <paramref name="elements"/> sobre <paramref name="input"/> y guarda en
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="now">
    /// Momento con el que se resuelve <c>{fecha}</c>. Inyectable para que los tests no dependan
    /// del reloj del sistema.
    /// </param>
    public void Sign(
        Stream input,
        Stream output,
        IEnumerable<PlacedElement> elements,
        DateTime? now = null,
        bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(elements);

        var timestamp = now ?? DateTime.Now;
        using var doc = Abrir(input, PdfDocumentOpenMode.Modify);

        // Una misma imagen usada en varias páginas debe embeberse UNA sola vez: sin esta caché
        // firmar 20 páginas multiplicaría por 20 el peso del archivo.
        var imageCache = new Dictionary<byte[], XImage>(ReferenceEqualityComparer.Instance);
        var openStreams = new List<Stream>();

        try
        {
            // Agrupar por página evita abrir un XGraphics (y un content stream) por elemento.
            foreach (var group in elements.GroupBy(e => e.PageIndex).OrderBy(g => g.Key))
            {
                if (group.Key < 0 || group.Key >= doc.PageCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(elements),
                        $"El elemento apunta a la página {group.Key}, pero el documento tiene {doc.PageCount}.");

                var page = doc.Pages[group.Key];
                var geometry = PageGeometry.FromPage(page);

                // Append preserva el contenido original y añade el nuestro encima.
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                geometry.ApplyRotationTransform(gfx);

                foreach (var element in group)
                {
                    var rect = geometry.ToVisibleRect(element.Bounds);

                    switch (element)
                    {
                        case ImageElement image:
                            DrawImage(gfx, rect, image, imageCache, openStreams);
                            break;

                        case TextElement text:
                            DrawText(gfx, rect, text, timestamp);
                            break;
                    }
                }
            }

            doc.Options.CompressContentStreams = compress;
            doc.Options.NoCompression = !compress;
            doc.Save(output, closeStream: false);
        }
        finally
        {
            foreach (var image in imageCache.Values)
                image.Dispose();
            foreach (var stream in openStreams)
                stream.Dispose();
        }
    }

    private static void DrawImage(
        XGraphics gfx,
        XRect rect,
        ImageElement element,
        Dictionary<byte[], XImage> cache,
        List<Stream> openStreams)
    {
        if (!cache.TryGetValue(element.Data, out var image))
        {
            // XImage lee del stream de forma diferida, así que el stream debe seguir vivo
            // hasta que el documento se guarde; por eso se cierra al final y no aquí.
            var ms = new MemoryStream(element.Data, writable: false);
            openStreams.Add(ms);

            try
            {
                image = XImage.FromStream(ms);
            }
            catch (Exception ex)
            {
                throw new PdfSignerException(
                    "La imagen no tiene un formato admitido. Usa un archivo PNG o JPG.", ex);
            }

            cache[element.Data] = image;
        }

        gfx.DrawImage(image, rect);
    }

    private static void DrawText(XGraphics gfx, XRect rect, TextElement element, DateTime now)
    {
        var resolved = PlaceholderResolver.Resolve(element.Text, now);
        if (string.IsNullOrWhiteSpace(resolved))
            return;

        var font = new XFont(
            EmbeddedFontResolver.FamilyName,
            element.FontSizePt,
            element.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

        var brush = new XSolidBrush(ParseColor(element.ColorHex));
        var lineHeight = element.FontSizePt * element.LineSpacing;

        var lines = resolved.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Se dibuja línea a línea con un rect propio para que PDFsharp calcule la línea
            // base; posicionarla a mano es una fuente clásica de desalineación entre fuentes.
            var lineRect = new XRect(rect.X, rect.Y + i * lineHeight, rect.Width, lineHeight);
            gfx.DrawString(lines[i], font, brush, lineRect, XStringFormats.TopLeft);
        }
    }

    /// <summary>Convierte "#RRGGBB" o "#AARRGGBB" a un color de PDFsharp.</summary>
    internal static XColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return XColors.Black;

        var value = hex.TrimStart('#');
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            return XColors.Black;

        return value.Length switch
        {
            6 => XColor.FromArgb((int)(0xFF000000 | parsed)),
            8 => XColor.FromArgb((int)parsed),
            _ => XColors.Black,
        };
    }

    /// <summary>PdfReader necesita poder hacer seek; los streams de red o de picker a veces no.</summary>
    private static Stream AsSeekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
