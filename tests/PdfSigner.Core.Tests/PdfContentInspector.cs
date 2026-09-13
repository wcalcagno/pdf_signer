using System.Text;
using System.Text.RegularExpressions;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Lee el PDF generado y comprueba qué operadores contiene.
/// </summary>
/// <remarks>
/// Por qué se inspecciona el archivo crudo en vez de rasterizarlo: permite verificar que el
/// contenido es texto e imagen REALES (y no un rasterizado) sin necesidad de un motor de
/// renderizado, con lo que la suite corre en CI en Linux sin emuladores ni Mac.
/// Los tests guardan con compress:false para que los operadores queden legibles.
/// </remarks>
internal sealed partial class PdfContentInspector
{
    private readonly string _raw;

    public PdfContentInspector(MemoryStream pdf) =>
        _raw = Encoding.Latin1.GetString(pdf.ToArray());

    /// <summary>Operadores de texto: prueban que el texto es seleccionable, no dibujado.</summary>
    public int TextOperators => CountOperator("Tj") + CountOperator("TJ");

    /// <summary>Invocaciones a XObject: cada imagen dibujada.</summary>
    public int DrawnXObjects => CountOperator("Do");

    /// <summary>Imágenes realmente embebidas (distinto de cuántas veces se dibujan).</summary>
    /// <remarks>
    /// No sirve contar "/Subtype/Image": un PNG con transparencia genera DOS objetos imagen,
    /// el propio dibujo y su /SMask. Se cuentan los números de objeto distintos a los que
    /// apuntan los recursos /XObject, que es lo que de verdad ocupa espacio en el archivo.
    /// </remarks>
    public int EmbeddedImages =>
        ImageResource().Matches(_raw).Select(m => m.Groups[1].Value).Distinct().Count();

    /// <summary>True si algún "cm" del stream lleva componente de giro.</summary>
    /// <remarks>
    /// PDFsharp NO mete la rotación en la matriz propia de la imagen: la emite como un "cm"
    /// aparte, antes de dibujar. Buscarla solo en la matriz de la imagen da un falso negativo.
    /// </remarks>
    public bool HasRotationTransform => Matrices.Any(m => Math.Abs(m[1]) > 0.001 || Math.Abs(m[2]) > 0.001);

    /// <summary>Todas las matrices "cm" del documento, en orden de aparición.</summary>
    public IReadOnlyList<double[]> Matrices =>
    [
        .. AnyMatrix().Matches(_raw).Select(Parse)
    ];

    /// <summary>
    /// Esquina inferior izquierda de la imagen en el espacio del MediaBox, componiendo la
    /// matriz de página con la propia de la imagen. Es la comprobación geométrica de verdad.
    /// </summary>
    /// <remarks>
    /// PDFsharp no acumula sus transformaciones en la matriz de la imagen: emite un "cm" por
    /// cada una (desplazamiento del MediaBox, giro de la página, y la propia de la imagen).
    /// Por eso hay que componerlas todas y no solo una.
    ///
    /// En PDF, "cm" concatena como CTM = M x CTM_anterior, de modo que el punto pasa primero
    /// por la ÚLTIMA matriz emitida y al final por la primera: se recorren en orden inverso.
    /// </remarks>
    public (double X, double Y) ComposedImageOrigin()
    {
        double x = 0, y = 0;

        foreach (var m in Matrices.Reverse())
        {
            var nx = m[0] * x + m[2] * y + m[4];
            var ny = m[1] * x + m[3] * y + m[5];
            (x, y) = (nx, ny);
        }

        return (x, y);
    }

    private static double[] Parse(Match m) =>
    [
        .. m.Groups.Cast<Group>().Skip(1).Take(6)
            .Select(g => double.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture))
    ];

    /// <summary>Presencia de máscara suave: el canal alfa del PNG sobrevivió.</summary>
    public bool HasAlphaMask => _raw.Contains("/SMask", StringComparison.Ordinal);

    /// <summary>Fuente embebida en el documento.</summary>
    public bool HasEmbeddedFont => FontFile().IsMatch(_raw);

    public bool ContainsText(string fragment) => _raw.Contains(fragment, StringComparison.Ordinal);

    /// <summary>Matrices de transformación de imagen: "a b c d e f cm" seguido de "/Ix Do".</summary>
    public IReadOnlyList<double[]> ImageMatrices => [.. ImageMatrix().Matches(_raw).Select(Parse)];

    private int CountOperator(string op)
    {
        var count = 0;
        var i = 0;
        while ((i = _raw.IndexOf(op, i, StringComparison.Ordinal)) >= 0)
        {
            var leftOk = i == 0 || char.IsWhiteSpace(_raw[i - 1]) || _raw[i - 1] is ')' or ']';
            var end = i + op.Length;
            var rightOk = end >= _raw.Length || char.IsWhiteSpace(_raw[end]);
            if (leftOk && rightOk)
                count++;
            i = end;
        }
        return count;
    }

    [GeneratedRegex(@"/I\d+\s+(\d+)\s+0\s+R")]
    private static partial Regex ImageResource();

    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) cm")]
    private static partial Regex AnyMatrix();

    [GeneratedRegex(@"/FontFile\d?")]
    private static partial Regex FontFile();

    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) cm\s*/I\d+ Do")]
    private static partial Regex ImageMatrix();
}
