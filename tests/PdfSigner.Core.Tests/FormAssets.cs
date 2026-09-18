using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Genera formularios PDF de prueba, con los cinco tipos de campo.
/// </summary>
/// <remarks>
/// Se construyen a mano manipulando los diccionarios del PDF porque PDFsharp sabe LEER y
/// rellenar formularios, pero no crearlos. No es un rodeo: obliga a escribir las estructuras
/// tal como las produce un generador real, así que la prueba se parece más a un formulario
/// de verdad que si lo montara una API de conveniencia.
///
/// La rotación y el origen del MediaBox son parámetros a propósito: son las dos trampas que
/// puede tener la conversión de coordenadas, y hay que poder provocarlas.
/// </remarks>
internal static class FormAssets
{
    public const double AnchoPt = 595;
    public const double AltoPt = 842;

    /// <summary>Rectángulo PDF del campo de texto principal, para poder afirmar sobre él.</summary>
    public static readonly (double X1, double Y1, double X2, double Y2) RectTexto = (100, 700, 300, 720);

    private static PdfDocument _doc = null!;
    private static PdfPage _page = null!;

    public static MemoryStream CrearFormulario(
        int rotacion = 0, double origenX = 0, double origenY = 0)
    {
        EmbeddedFontResolver.Register();

        _doc = new PdfDocument();
        _page = _doc.AddPage();
        _page.MediaBox = new PdfRectangle(
            new XPoint(origenX, origenY),
            new XPoint(origenX + AnchoPt, origenY + AltoPt));
        _page.Rotate = rotacion;

        var helv = Dic();
        helv.Elements["/Type"] = new PdfName("/Font");
        helv.Elements["/Subtype"] = new PdfName("/Type1");
        helv.Elements["/BaseFont"] = new PdfName("/Helvetica");
        helv.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");

        // Las coordenadas se desplazan con el origen del MediaBox, como en un PDF real.
        double X(double v) => v + origenX;
        double Y(double v) => v + origenY;

        var campos = new List<PdfDictionary>
        {
            Texto("nombre", X(RectTexto.X1), Y(RectTexto.Y1), 200, 20, maxLength: 60),
            Texto("notas", X(100), Y(640), 200, 50, multilinea: true),
            Texto("bloqueado", X(100), Y(600), 200, 20, soloLectura: true),
            Casilla("acepto", X(100), Y(560)),
            Radio("respuesta", [("si", X(100), Y(520)), ("no", X(160), Y(520))]),
            Eleccion("color", X(100), Y(470), 150, 20, combo: true, ["Azul", "Verde", "Rojo"]),
            Eleccion("turno", X(100), Y(400), 150, 50, combo: false, ["Mañana", "Tarde"]),
        };

        var annots = new PdfArray(_doc, campos.SelectMany(Widgets).ToArray());
        _doc.Internals.AddObject(annots);
        _page.Elements["/Annots"] = annots.Reference!;

        var lista = new PdfArray(_doc, campos.Select(c => (PdfItem)c.Reference!).ToArray());
        _doc.Internals.AddObject(lista);

        var recursos = Dic();
        var fuentes = Dic();
        fuentes.Elements["/Helv"] = helv.Reference!;
        recursos.Elements["/Font"] = fuentes;

        var acro = Dic();
        acro.Elements["/Fields"] = lista.Reference!;
        acro.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");
        acro.Elements["/DR"] = recursos;
        _doc.Internals.Catalog.Elements["/AcroForm"] = acro.Reference!;

        var ms = new MemoryStream();
        _doc.Save(ms, closeStream: false);
        _doc.Close();
        ms.Position = 0;
        return ms;
    }

    /// <summary>Un PDF normal, sin formulario, para comprobar que no se inventa campos.</summary>
    public static MemoryStream CrearSinFormulario()
    {
        EmbeddedFontResolver.Register();

        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        using (var g = XGraphics.FromPdfPage(page))
            g.DrawString("Sin formulario", new XFont("Open Sans", 12), XBrushes.Black, new XPoint(60, 60));

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    // ------------------------------------------------------------- utilidades

    private static IEnumerable<PdfItem> Widgets(PdfDictionary campo)
    {
        var kids = campo.Elements.GetArray("/Kids");
        if (kids is null)
            return [campo.Reference!];

        return Enumerable.Range(0, kids.Elements.Count).Select(i => kids.Elements[i]);
    }

    private static PdfDictionary Dic()
    {
        var d = new PdfDictionary(_doc);
        _doc.Internals.AddObject(d);
        return d;
    }

    private static PdfArray Rect(double x, double y, double w, double h) =>
        new(_doc, new PdfReal(x), new PdfReal(y), new PdfReal(x + w), new PdfReal(y + h));

    private static PdfDictionary Texto(
        string nombre, double x, double y, double w, double h,
        int maxLength = 0, bool multilinea = false, bool soloLectura = false)
    {
        var d = Dic();
        d.Elements["/Type"] = new PdfName("/Annot");
        d.Elements["/Subtype"] = new PdfName("/Widget");
        d.Elements["/FT"] = new PdfName("/Tx");
        d.Elements["/T"] = new PdfString(nombre);
        d.Elements["/Rect"] = Rect(x, y, w, h);
        d.Elements["/F"] = new PdfInteger(4);
        d.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");
        d.Elements["/P"] = _page.Reference!;

        if (maxLength > 0)
            d.Elements["/MaxLen"] = new PdfInteger(maxLength);

        // Bit 13 = Multiline, bit 1 = ReadOnly
        var flags = 0;
        if (multilinea) flags |= 4096;
        if (soloLectura) flags |= 1;
        if (flags != 0) d.Elements["/Ff"] = new PdfInteger(flags);

        return d;
    }

    private static PdfDictionary Apariencia(string contenido)
    {
        var xo = Dic();
        xo.Elements["/Type"] = new PdfName("/XObject");
        xo.Elements["/Subtype"] = new PdfName("/Form");
        xo.Elements["/BBox"] = new PdfArray(_doc,
            new PdfReal(0), new PdfReal(0), new PdfReal(18), new PdfReal(18));
        xo.CreateStream(Encoding.ASCII.GetBytes(contenido));
        return xo;
    }

    private static PdfDictionary Casilla(string nombre, double x, double y)
    {
        var d = Dic();
        d.Elements["/Type"] = new PdfName("/Annot");
        d.Elements["/Subtype"] = new PdfName("/Widget");
        d.Elements["/FT"] = new PdfName("/Btn");
        d.Elements["/T"] = new PdfString(nombre);
        d.Elements["/Rect"] = Rect(x, y, 18, 18);
        d.Elements["/F"] = new PdfInteger(4);
        d.Elements["/AS"] = new PdfName("/Off");
        d.Elements["/P"] = _page.Reference!;

        // /AP y /N van como diccionarios DIRECTOS: con objetos indirectos, PDFsharp no
        // encuentra el nombre del estado marcado y lanza al intentar marcar la casilla.
        var n = new PdfDictionary(_doc);
        n.Elements["/Off"] = Apariencia("0.6 w 0 G 1 1 16 16 re S").Reference!;
        n.Elements["/Yes"] = Apariencia("0.6 w 0 G 1 1 16 16 re S 1.8 w 4 9 m 8 5 l 14 14 l S").Reference!;
        var ap = new PdfDictionary(_doc);
        ap.Elements["/N"] = n;
        d.Elements["/AP"] = ap;
        return d;
    }

    private static PdfDictionary Radio(string nombre, (string valor, double x, double y)[] opciones)
    {
        var padre = Dic();
        padre.Elements["/FT"] = new PdfName("/Btn");
        padre.Elements["/T"] = new PdfString(nombre);
        // Bit 16 = Radio, bit 15 = NoToggleToOff
        padre.Elements["/Ff"] = new PdfInteger(32768 | 16384);
        padre.Elements["/V"] = new PdfName("/Off");

        var kids = new PdfArray(_doc);
        foreach (var (valor, x, y) in opciones)
        {
            var k = Dic();
            k.Elements["/Type"] = new PdfName("/Annot");
            k.Elements["/Subtype"] = new PdfName("/Widget");
            k.Elements["/Rect"] = Rect(x, y, 18, 18);
            k.Elements["/F"] = new PdfInteger(4);
            k.Elements["/AS"] = new PdfName("/Off");
            k.Elements["/Parent"] = padre.Reference!;
            k.Elements["/P"] = _page.Reference!;

            var n = new PdfDictionary(_doc);
            n.Elements["/Off"] = Apariencia("0.6 w 0 G 1 1 16 16 re S").Reference!;
            n.Elements["/" + valor] = Apariencia("0.6 w 0 G 1 1 16 16 re S 0 g 5 5 8 8 re f").Reference!;
            var ap = new PdfDictionary(_doc);
            ap.Elements["/N"] = n;
            k.Elements["/AP"] = ap;

            kids.Elements.Add(k.Reference!);
        }

        _doc.Internals.AddObject(kids);
        padre.Elements["/Kids"] = kids.Reference!;
        return padre;
    }

    private static PdfDictionary Eleccion(
        string nombre, double x, double y, double w, double h, bool combo, string[] opciones)
    {
        var d = Dic();
        d.Elements["/Type"] = new PdfName("/Annot");
        d.Elements["/Subtype"] = new PdfName("/Widget");
        d.Elements["/FT"] = new PdfName("/Ch");
        d.Elements["/T"] = new PdfString(nombre);
        d.Elements["/Rect"] = Rect(x, y, w, h);
        d.Elements["/F"] = new PdfInteger(4);
        d.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");
        d.Elements["/P"] = _page.Reference!;

        // Bit 18 = Combo
        if (combo)
            d.Elements["/Ff"] = new PdfInteger(131072);

        var opt = new PdfArray(_doc, opciones.Select(o => (PdfItem)new PdfString(o)).ToArray());
        _doc.Internals.AddObject(opt);
        d.Elements["/Opt"] = opt.Reference!;
        return d;
    }
}
