namespace PdfSigner.Core;

/// <summary>
/// Rectángulo en coordenadas normalizadas (0..1) relativas al área VISIBLE de la página.
/// </summary>
/// <remarks>
/// Por qué normalizado y no píxeles: la UI dibuja a un zoom y una densidad de pantalla que
/// cambian según el dispositivo, mientras que el PDF se mide en puntos (1/72"). Si el modelo
/// guardara píxeles, cada cambio de zoom o cada pantalla con distinto DPI introduciría un
/// error de posición. Guardando 0..1 el dato es independiente de ambos, y la conversión a
/// puntos ocurre en un único lugar (<see cref="PageGeometry"/>), que además es testeable.
/// </remarks>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>Recorta el rectángulo para que no se salga de la página.</summary>
    public NormalizedRect Clamped()
    {
        var w = Math.Clamp(Width, 0, 1);
        var h = Math.Clamp(Height, 0, 1);
        return new NormalizedRect(
            Math.Clamp(X, 0, 1 - w),
            Math.Clamp(Y, 0, 1 - h),
            w, h);
    }

    /// <summary>Desplaza el rectángulo, sin salirse de la página.</summary>
    public NormalizedRect Moved(double deltaX, double deltaY) =>
        new NormalizedRect(X + deltaX, Y + deltaY, Width, Height).Clamped();

    /// <summary>
    /// Redimensiona arrastrando una esquina; la opuesta se queda donde está.
    /// </summary>
    /// <remarks>
    /// Esto vive en el núcleo y no en la interfaz a propósito. Es aritmética que puede fallar
    /// en silencio —el rectángulo se invierte, el elemento salta al otro lado del cursor— y la
    /// interfaz no tiene pruebas automáticas, mientras que aquí sí.
    /// </remarks>
    /// <param name="minimo">Tamaño mínimo, en proporción de página.</param>
    public NormalizedRect Resized(ResizeCorner corner, double deltaX, double deltaY, double minimo = 0.02)
    {
        double x = X, y = Y, w = Width, h = Height;

        // Arrastrar un borde izquierdo o superior mueve el origen además de cambiar el
        // tamaño; los bordes derecho e inferior solo cambian el tamaño.
        switch (corner)
        {
            case ResizeCorner.BottomRight:
                w += deltaX;
                h += deltaY;
                break;

            case ResizeCorner.BottomLeft:
                x += deltaX;
                w -= deltaX;
                h += deltaY;
                break;

            case ResizeCorner.TopRight:
                y += deltaY;
                h -= deltaY;
                w += deltaX;
                break;

            case ResizeCorner.TopLeft:
                x += deltaX;
                w -= deltaX;
                y += deltaY;
                h -= deltaY;
                break;
        }

        // Al llegar al mínimo el borde arrastrado se detiene en lugar de cruzar al otro lado.
        // Sin esto, seguir arrastrando daría la vuelta al rectángulo y el elemento saltaría
        // al lado contrario del cursor.
        if (w < minimo)
        {
            if (corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft)
                x = X + Width - minimo;
            w = minimo;
        }

        if (h < minimo)
        {
            if (corner is ResizeCorner.TopLeft or ResizeCorner.TopRight)
                y = Y + Height - minimo;
            h = minimo;
        }

        return new NormalizedRect(x, y, w, h).Clamped();
    }
}

/// <summary>Esquina por la que se arrastra al redimensionar un elemento.</summary>
public enum ResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Elemento colocado sobre una página concreta del documento.</summary>
public abstract class PlacedElement
{
    /// <summary>Índice de página (base 0). Ancla el elemento a la página donde se soltó.</summary>
    public int PageIndex { get; set; }

    public NormalizedRect Bounds { get; set; }
}

/// <summary>Imagen de firma (PNG o JPG). Se embebe en el PDF como XObject.</summary>
public sealed class ImageElement : PlacedElement
{
    public required byte[] Data { get; init; }
}

/// <summary>Bloque de texto multilínea. Se embebe como texto real, seleccionable.</summary>
public sealed class TextElement : PlacedElement
{
    public string Text { get; set; } = string.Empty;
    public double FontSizePt { get; set; } = 11;
    public string ColorHex { get; set; } = "#000000";
    public bool Bold { get; set; }

    /// <summary>Separación entre líneas, como múltiplo del tamaño de fuente.</summary>
    public double LineSpacing { get; set; } = 1.25;
}
