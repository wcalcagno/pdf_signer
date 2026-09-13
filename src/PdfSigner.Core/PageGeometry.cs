using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfSigner.Core;

/// <summary>
/// Geometría de una página tal como la VE el usuario, y conversión a coordenadas de dibujo.
/// </summary>
/// <remarks>
/// Aquí se resuelve la trampa más importante del proyecto. Comprobado empíricamente con
/// PDFsharp 6.2.4:
///
///  1. <c>XGraphics</c> ya usa origen arriba-izquierda con Y hacia abajo, y hace solo el
///     volteo al sistema de coordenadas del PDF (origen abajo-izquierda). No hay que invertir
///     Y a mano.
///  2. Pero <c>XGraphics</c> IGNORA la entrada <c>/Rotate</c> de la página: en una página con
///     /Rotate 90 informa 595x842 cuando el usuario está viendo 842x595. Sin corregirlo, toda
///     firma sobre un PDF escaneado o apaisado sale girada y fuera de lugar.
///  3. El <c>MediaBox</c> no siempre empieza en (0,0); hay PDFs con origen desplazado.
///
/// Esta clase encapsula los tres puntos para que el resto del código trabaje siempre en
/// "espacio visible": lo que el usuario ve es lo que se dibuja.
/// </remarks>
public sealed class PageGeometry
{
    /// <summary>Ancho del MediaBox sin rotar, en puntos.</summary>
    public double RawWidthPt { get; }

    /// <summary>Alto del MediaBox sin rotar, en puntos.</summary>
    public double RawHeightPt { get; }

    /// <summary>Rotación normalizada a 0, 90, 180 o 270.</summary>
    public int Rotation { get; }

    /// <summary>Esquina inferior izquierda del MediaBox. Casi siempre (0,0), pero no siempre.</summary>
    public double OriginX { get; }

    /// <summary>Componente vertical del origen del MediaBox.</summary>
    public double OriginY { get; }

    /// <summary>True si el MediaBox no arranca en el origen de coordenadas.</summary>
    public bool HasOffsetOrigin => Math.Abs(OriginX) > 0.001 || Math.Abs(OriginY) > 0.001;

    /// <summary>Ancho tal como lo ve el usuario (ya intercambiado si la página está rotada).</summary>
    public double VisibleWidthPt => IsQuarterTurned ? RawHeightPt : RawWidthPt;

    /// <summary>Alto tal como lo ve el usuario.</summary>
    public double VisibleHeightPt => IsQuarterTurned ? RawWidthPt : RawHeightPt;

    /// <summary>True si la rotación intercambia ancho y alto (90° o 270°).</summary>
    public bool IsQuarterTurned => Rotation == 90 || Rotation == 270;

    public PageGeometry(
        double rawWidthPt, double rawHeightPt, int rotation,
        double originX = 0, double originY = 0)
    {
        if (rawWidthPt <= 0 || rawHeightPt <= 0)
            throw new ArgumentOutOfRangeException(nameof(rawWidthPt), "Las dimensiones deben ser positivas.");

        RawWidthPt = rawWidthPt;
        RawHeightPt = rawHeightPt;
        OriginX = originX;
        OriginY = originY;

        // Los PDFs reales traen valores como -90 o 450; normalizamos a [0,360) en pasos de 90.
        var r = ((rotation % 360) + 360) % 360;
        Rotation = (r / 90) * 90;
    }

    public static PageGeometry FromPage(PdfPage page)
    {
        // Se usa el MediaBox y no page.Width/Height para no depender de si PDFsharp ya
        // aplicó o no la rotación internamente: el MediaBox es siempre el rectángulo crudo.
        var box = page.MediaBox;
        return new PageGeometry(box.Width, box.Height, page.Rotate, box.X1, box.Y1);
    }

    /// <summary>Convierte un rectángulo normalizado al rectángulo en puntos del espacio visible.</summary>
    public XRect ToVisibleRect(NormalizedRect r)
    {
        var c = r.Clamped();
        return new XRect(
            c.X * VisibleWidthPt,
            c.Y * VisibleHeightPt,
            c.Width * VisibleWidthPt,
            c.Height * VisibleHeightPt);
    }

    /// <summary>
    /// Aplica a <paramref name="gfx"/> la transformación que compensa <c>/Rotate</c>.
    /// Después de llamarla se puede dibujar directamente en espacio visible.
    /// </summary>
    /// <remarks>
    /// Las matrices salen de exigir que la esquina superior izquierda de lo que el usuario ve
    /// caiga en la esquina del MediaBox que el visor coloca ahí al rotar la página:
    ///   90°  → esquina inferior izquierda del MediaBox
    ///   180° → esquina inferior derecha
    ///   270° → esquina superior derecha
    /// </remarks>
    public void ApplyRotationTransform(XGraphics gfx)
    {
        // PDFsharp trabaja como si el MediaBox empezara siempre en (0,0). Cuando no es así
        // —habitual en PDFs de imprenta o recortados— la firma se desplaza exactamente ese
        // offset. Se corrige antes de rotar, para que el giro ocurra en coordenadas locales
        // de la página.
        //
        // El signo de Y va invertido porque XGraphics tiene el eje hacia abajo y PDFsharp
        // convierte con pdf_y = alto - y: para sumar OriginY al resultado hay que restarlo aquí.
        if (HasOffsetOrigin)
            gfx.TranslateTransform(OriginX, -OriginY);

        switch (Rotation)
        {
            case 90:
                gfx.TranslateTransform(0, RawHeightPt);
                gfx.RotateTransform(-90);
                break;
            case 180:
                gfx.TranslateTransform(RawWidthPt, RawHeightPt);
                gfx.RotateTransform(180);
                break;
            case 270:
                gfx.TranslateTransform(RawWidthPt, 0);
                gfx.RotateTransform(90);
                break;
            // 0° no necesita transformación.
        }
    }

    /// <summary>
    /// Mapea un punto del espacio visible al espacio del MediaBox sin rotar.
    /// Se usa en los tests para verificar dónde acaba realmente el contenido.
    /// </summary>
    public (double X, double Y) VisibleToRaw(double vx, double vy) => Rotation switch
    {
        90  => (vy, RawHeightPt - vx),
        180 => (RawWidthPt - vx, RawHeightPt - vy),
        270 => (RawWidthPt - vy, vx),
        _   => (vx, vy),
    };
}
