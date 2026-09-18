using System.Globalization;
using System.Text;
using PdfSharp.Pdf;

namespace PdfSigner.Core;

/// <summary>
/// Convierte los campos de formulario en contenido fijo de la página.
/// </summary>
/// <remarks>
/// Aplanar es pasar lo que hoy dibuja el visor a partir del campo (su <c>/AP /N</c>) al flujo
/// de contenido de la página, y luego quitar el campo. Lo que se ve queda igual, pero ya no
/// hay nada que rellenar: el documento se convierte en el equivalente a un papel escrito.
///
/// Aquí NO hay que compensar la rotación de la página ni el origen del MediaBox, al revés que
/// al firmar. El <c>/Rect</c> de un widget y el flujo de contenido viven en el mismo espacio
/// de coordenadas, el del MediaBox sin rotar; el <c>/Rotate</c> lo aplica el visor después,
/// a la página entera. Añadir aquí la corrección que necesita la firma descolocaría el
/// estampado justo en los documentos girados.
/// </remarks>
internal static class FormFlattener
{
    private const string Prefijo = "/PsFld";

    public static void Aplanar(PdfDocument doc)
    {
        for (var i = 0; i < doc.PageCount; i++)
            AplanarPagina(doc, doc.Pages[i]);

        // Un /AcroForm que apunta a campos que ya no existen deja el documento en un estado
        // que algunos visores rechazan, así que se retira entero.
        doc.Internals.Catalog.Elements.Remove("/AcroForm");
    }

    private static void AplanarPagina(PdfDocument doc, PdfPage page)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null)
            return;

        var quedan = new List<PdfItem>();
        var estampas = new List<(PdfDictionary Widget, PdfDictionary Apariencia)>();

        for (var i = 0; i < annots.Elements.Count; i++)
        {
            var item = annots.Elements[i];
            var dic = Resolver(item);

            // Los enlaces, las notas y cualquier otra anotación que no sea de formulario se
            // quedan donde están: aplanar un formulario no es aplanar el documento entero.
            if (dic is null || dic.Elements.GetName("/Subtype") != "/Widget")
            {
                quedan.Add(item);
                continue;
            }

            if (!Invisible(dic) && AparienciaNormal(dic) is { } apariencia)
                estampas.Add((dic, apariencia));
        }

        if (estampas.Count > 0)
            Estampar(doc, page, estampas);

        if (quedan.Count == annots.Elements.Count)
            return;

        if (quedan.Count == 0)
            page.Elements.Remove("/Annots");
        else
            page.Elements["/Annots"] = new PdfArray(doc, [.. quedan]);
    }

    // ------------------------------------------------------------- apariencia

    /// <summary>Flujo que el visor dibuja hoy para este widget, o null si no dibuja nada.</summary>
    /// <remarks>
    /// En un campo de texto <c>/AP /N</c> es directamente el flujo. En una casilla o un radio
    /// es un diccionario de estados, y el que vale es el que nombra <c>/AS</c>: por eso el
    /// aplanado de un botón sale bien o mal según se haya escrito <c>/AS</c> al rellenarlo.
    /// </remarks>
    private static PdfDictionary? AparienciaNormal(PdfDictionary widget)
    {
        var n = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (n is null)
            return null;

        if (n.Stream is not null)
            return n;

        var estado = widget.Elements.GetName("/AS");
        return string.IsNullOrEmpty(estado) ? null : Resolver(n.Elements[estado]);
    }

    /// <summary>True si el campo está marcado para no verse.</summary>
    /// <remarks>Bit 2 de /F = oculto, bit 6 = no se muestra en pantalla.</remarks>
    private static bool Invisible(PdfDictionary widget)
    {
        var f = widget.Elements.GetInteger("/F");
        return (f & 2) != 0 || (f & 32) != 0;
    }

    // -------------------------------------------------------------- estampado

    private static void Estampar(
        PdfDocument doc, PdfPage page, List<(PdfDictionary Widget, PdfDictionary Apariencia)> estampas)
    {
        var recursos = Recursos(page);
        var xobjects = recursos.Elements.GetDictionary("/XObject");
        if (xobjects is null)
        {
            xobjects = new PdfDictionary(doc);
            recursos.Elements["/XObject"] = xobjects;
        }

        var dibujo = new StringBuilder();

        foreach (var (widget, apariencia) in estampas)
        {
            if (Rectangulo(widget) is not { } rect)
                continue;

            if (Matriz(apariencia, rect) is not { } cm)
                continue;

            // Para poder nombrarla en los recursos, la apariencia tiene que ser un objeto
            // indirecto. Las que genera esta aplicación ya lo son; las de otros programas,
            // casi siempre. Si viniera suelta, se registra ahora.
            if (apariencia.Reference is null)
                doc.Internals.AddObject(apariencia);

            var nombre = NombreLibre(xobjects);
            xobjects.Elements[nombre] = apariencia.Reference!;

            dibujo.AppendLine("q");
            dibujo.AppendLine($"{cm} cm");
            dibujo.AppendLine($"{nombre} Do");
            dibujo.AppendLine("Q");
        }

        if (dibujo.Length == 0)
            return;

        // El contenido original se envuelve en q/Q. Si el documento termina con una
        // transformación abierta —pasa en PDF generados a trozos—, el estampado la heredaría
        // y saldría desplazado o a otra escala, sin ningún error que lo delatara.
        var apertura = page.Contents.AppendContent();
        apertura.CreateStream("q\n"u8.ToArray());
        page.Contents.Elements.RemoveAt(page.Contents.Elements.Count - 1);
        page.Contents.Elements.Insert(0, apertura.Reference!);

        var cierre = page.Contents.AppendContent();
        cierre.CreateStream(Encoding.Latin1.GetBytes("Q\n" + dibujo));
    }

    /// <summary>
    /// Matriz que encaja la apariencia dentro del rectángulo del campo.
    /// </summary>
    /// <remarks>
    /// Es el algoritmo de la norma (PDF 32000-1, 12.5.5): se transforman las cuatro esquinas
    /// de la <c>/BBox</c> por la <c>/Matrix</c> de la apariencia, se toma la envolvente del
    /// resultado y se calcula la transformación que la lleva al <c>/Rect</c>. La
    /// <c>/Matrix</c> no se incluye aquí porque el operador <c>Do</c> la aplica él mismo;
    /// multiplicarla dos veces es el error clásico con las apariencias giradas.
    /// </remarks>
    private static string? Matriz(PdfDictionary apariencia, (double X1, double Y1, double X2, double Y2) rect)
    {
        var bbox = apariencia.Elements.GetArray("/BBox");
        if (bbox is null || bbox.Elements.Count < 4)
            return null;

        var m = apariencia.Elements.GetArray("/Matrix");
        double[] t = m is not null && m.Elements.Count >= 6
            ? [.. Enumerable.Range(0, 6).Select(m.Elements.GetReal)]
            : [1, 0, 0, 1, 0, 0];

        double bx1 = bbox.Elements.GetReal(0), by1 = bbox.Elements.GetReal(1);
        double bx2 = bbox.Elements.GetReal(2), by2 = bbox.Elements.GetReal(3);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var (x, y) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = t[0] * x + t[2] * y + t[4];
            var ty = t[1] * x + t[3] * y + t[5];
            minX = Math.Min(minX, tx); maxX = Math.Max(maxX, tx);
            minY = Math.Min(minY, ty); maxY = Math.Max(maxY, ty);
        }

        var anchoB = maxX - minX;
        var altoB = maxY - minY;
        if (anchoB <= 0 || altoB <= 0)
            return null;

        var sx = Math.Abs(rect.X2 - rect.X1) / anchoB;
        var sy = Math.Abs(rect.Y2 - rect.Y1) / altoB;

        var x0 = Math.Min(rect.X1, rect.X2) - minX * sx;
        var y0 = Math.Min(rect.Y1, rect.Y2) - minY * sy;

        return $"{F(sx)} 0 0 {F(sy)} {F(x0)} {F(y0)}";
    }

    // -------------------------------------------------------------- recursos

    /// <summary>Recursos de la página, listos para añadirles un XObject.</summary>
    /// <remarks>
    /// <c>/Resources</c> es heredable: una página sin los suyos usa los del nodo padre, y unos
    /// propios vacíos los ESCONDERÍAN, dejando al contenido original sin fuentes. PDFsharp ya
    /// resuelve la herencia al materializar esta propiedad —comprobado sobre un documento con
    /// los recursos colgados del padre— así que no hace falta copiarlos a mano.
    /// </remarks>
    private static PdfDictionary Recursos(PdfPage page) => page.Resources;

    private static string NombreLibre(PdfDictionary xobjects)
    {
        for (var i = 0; ; i++)
        {
            var nombre = Prefijo + i.ToString(CultureInfo.InvariantCulture);
            if (!xobjects.Elements.ContainsKey(nombre))
                return nombre;
        }
    }

    // ------------------------------------------------------------ utilidades

    private static (double X1, double Y1, double X2, double Y2)? Rectangulo(PdfDictionary dic)
    {
        var rect = dic.Elements.GetArray("/Rect");
        if (rect is null || rect.Elements.Count < 4)
            return null;

        return (rect.Elements.GetReal(0), rect.Elements.GetReal(1),
                rect.Elements.GetReal(2), rect.Elements.GetReal(3));
    }

    private static PdfDictionary? Resolver(PdfItem? item) => item switch
    {
        PdfSharp.Pdf.Advanced.PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };

    private static string F(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);
}
