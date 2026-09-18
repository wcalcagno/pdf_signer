#if DEBUG
using System.IO.Compression;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSigner.Core;

namespace PdfSigner.App.Services;

/// <summary>
/// Genera un documento y una firma de ejemplo para poder arrancar la aplicación con contenido
/// ya cargado.
/// </summary>
/// <remarks>
/// Solo existe en compilaciones de depuración y solo se activa con la variable de entorno
/// PDFSIGNER_DEMO. Sirve para revisar estados de la interfaz que de otro modo exigen abrir un
/// archivo a mano: elemento seleccionado, inspector con datos, asas de redimensionado.
///
/// No es una funcionalidad de la aplicación, es andamio de desarrollo.
/// </remarks>
internal static class DemoContent
{
    public const string VariableEntorno = "PDFSIGNER_DEMO";

    public static bool Activo =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VariableEntorno));

    /// <summary>
    /// Con PDFSIGNER_DEMO=drag el elemento arranca marcado como si se estuviera arrastrando.
    /// </summary>
    /// <remarks>
    /// Existe porque una captura de pantalla es estática y no puede simular un gesto: sin
    /// esto no habría forma de comprobar con la vista que el aviso de arrastre se dibuja.
    /// </remarks>
    public static bool SimularArrastre => Modo == "drag";

    /// <summary>Modo del arnés: "1", "drag", "moved", "undo" o "form".</summary>
    public static string Modo =>
        Environment.GetEnvironmentVariable(VariableEntorno)?.ToLowerInvariant() ?? string.Empty;

    /// <summary>Con PDFSIGNER_DEMO=form o formlleno se carga un formulario en vez del contrato.</summary>
    public static bool EsFormulario => Modo is "form" or "formlleno" or "formexporta" or "formabierto";

    /// <summary>
    /// Con PDFSIGNER_DEMO=formlleno el formulario arranca con todos los campos escritos.
    /// </summary>
    /// <remarks>
    /// Una captura no puede teclear ni pulsar, así que sin esto no habria forma de comprobar
    /// con la vista que marcar una opcion apaga las demas del grupo, que es justo la parte
    /// que depende de que varias zonas compartan el mismo objeto de campo.
    /// </remarks>
    public static bool RellenarDemo => Modo is "formlleno" or "formexporta" or "formabierto";

    /// <summary>
    /// Con PDFSIGNER_DEMO=formexporta se rellena, se exporta a un archivo y se avisa dónde.
    /// </summary>
    /// <remarks>
    /// El diálogo de guardar no se puede automatizar, así que sin esto la exportación es lo
    /// único de la aplicación que nadie llega a ejercitar. Recorre la MISMA tubería que el
    /// botón, solo que escribiendo el resultado en la carpeta temporal.
    ///
    /// Con formabierto hace lo mismo pero sin bloquear los campos, para poder comparar.
    /// </remarks>
    public static bool ExportarDemo => Modo is "formexporta" or "formabierto";

    /// <summary>Con formabierto el PDF exportado conserva los campos editables.</summary>
    public static bool ExportarSinBloquear => Modo == "formabierto";

    /// <summary>
    /// Formulario de una página con los cinco tipos de campo.
    /// </summary>
    /// <remarks>
    /// Se monta a mano con diccionarios porque PDFsharp sabe leer y rellenar formularios pero
    /// no crearlos. Duplica en parte lo que hace el generador de las pruebas, y es a propósito:
    /// aquél sirve para afirmar sobre estructuras, éste para poder MIRAR la pantalla. Juntarlos
    /// obligaría a que el núcleo expusiera un constructor de formularios que la aplicación no
    /// necesita para nada.
    /// </remarks>
    public static byte[] CrearFormularioPdf()
    {
        EmbeddedFontResolver.Register();

        var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        PdfDictionary Dic()
        {
            var d = new PdfDictionary(doc);
            doc.Internals.AddObject(d);
            return d;
        }

        PdfArray Rect(double x, double y, double w, double h) =>
            new(doc, new PdfReal(x), new PdfReal(y), new PdfReal(x + w), new PdfReal(y + h));

        PdfDictionary Base(string nombre, string tipo, PdfArray rect)
        {
            var d = Dic();
            d.Elements["/Type"] = new PdfName("/Annot");
            d.Elements["/Subtype"] = new PdfName("/Widget");
            d.Elements["/FT"] = new PdfName(tipo);
            d.Elements["/T"] = new PdfString(nombre);
            d.Elements["/Rect"] = rect;
            d.Elements["/F"] = new PdfInteger(4);
            d.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");
            d.Elements["/P"] = page.Reference!;
            return d;
        }

        // Los rótulos van dibujados en la página, como en un formulario de verdad: el nombre
        // interno del campo solo se ve como pista dentro de la caja vacía.
        using (var g = XGraphics.FromPdfPage(page))
        {
            var titulo = new XFont("Open Sans", 14, XFontStyleEx.Bold);
            var texto = new XFont("Open Sans", 10);

            g.DrawString("SOLICITUD DE INSCRIPCION", titulo, XBrushes.DimGray, new XPoint(60, 70));

            foreach (var (etiqueta, y) in new[]
            {
                ("Nombre completo", 130.0), ("Observaciones", 180.0), ("Acepto las condiciones", 265.0),
                ("Asistira", 300.0), ("Turno preferido", 340.0),
            })
            {
                g.DrawString(etiqueta, texto, XBrushes.Black, new XPoint(60, y));
            }

            g.DrawString("Si", texto, XBrushes.Black, new XPoint(198, 305));
            g.DrawString("No", texto, XBrushes.Black, new XPoint(258, 305));
        }

        var nombre = Base("nombre", "/Tx", Rect(200, 725, 230, 20));
        var observaciones = Base("observaciones", "/Tx", Rect(200, 640, 230, 55));
        observaciones.Elements["/Ff"] = new PdfInteger(4096);   // multilínea

        var acepto = Base("acepto", "/Btn", Rect(200, 568, 16, 16));
        acepto.Elements["/AS"] = new PdfName("/Off");
        acepto.Elements["/AP"] = Apariencias(doc, ["Off", "Yes"]);

        var asistira = Dic();
        asistira.Elements["/FT"] = new PdfName("/Btn");
        asistira.Elements["/T"] = new PdfString("asistira");
        asistira.Elements["/Ff"] = new PdfInteger(32768 | 16384);   // radio, sin apagar
        asistira.Elements["/V"] = new PdfName("/Off");

        var opciones = new PdfArray(doc);
        foreach (var (valor, x) in new[] { ("si", 180.0), ("no", 240.0) })
        {
            var k = Dic();
            k.Elements["/Type"] = new PdfName("/Annot");
            k.Elements["/Subtype"] = new PdfName("/Widget");
            k.Elements["/Rect"] = Rect(x, 533, 16, 16);
            k.Elements["/F"] = new PdfInteger(4);
            k.Elements["/AS"] = new PdfName("/Off");
            k.Elements["/Parent"] = asistira.Reference!;
            k.Elements["/P"] = page.Reference!;
            k.Elements["/AP"] = Apariencias(doc, ["Off", valor]);
            opciones.Elements.Add(k.Reference!);
        }

        doc.Internals.AddObject(opciones);
        asistira.Elements["/Kids"] = opciones.Reference!;

        var turno = Base("turno", "/Ch", Rect(200, 493, 150, 20));
        turno.Elements["/Ff"] = new PdfInteger(131072);   // desplegable
        var opt = new PdfArray(doc,
            new PdfString("Mañana"), new PdfString("Tarde"), new PdfString("Indiferente"));
        doc.Internals.AddObject(opt);
        turno.Elements["/Opt"] = opt.Reference!;

        var campos = new List<PdfDictionary> { nombre, observaciones, acepto, asistira, turno };

        var annots = new PdfArray(doc, [
            nombre.Reference!, observaciones.Reference!, acepto.Reference!,
            opciones.Elements[0], opciones.Elements[1], turno.Reference!,
        ]);
        doc.Internals.AddObject(annots);
        page.Elements["/Annots"] = annots.Reference!;

        var lista = new PdfArray(doc, [.. campos.Select(c => (PdfItem)c.Reference!)]);
        doc.Internals.AddObject(lista);

        var acro = Dic();
        acro.Elements["/Fields"] = lista.Reference!;
        acro.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");
        doc.Internals.Catalog.Elements["/AcroForm"] = acro.Reference!;

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        doc.Close();
        return ms.ToArray();
    }

    /// <summary>Diccionario /AP con una apariencia por estado.</summary>
    /// <remarks>
    /// /AP y /N tienen que ser diccionarios DIRECTOS: con objetos indirectos PDFsharp no
    /// encuentra los estados al releer el documento.
    /// </remarks>
    private static PdfDictionary Apariencias(PdfDocument doc, string[] estados)
    {
        var n = new PdfDictionary(doc);

        foreach (var estado in estados)
        {
            var xo = new PdfDictionary(doc);
            doc.Internals.AddObject(xo);
            xo.Elements["/Type"] = new PdfName("/XObject");
            xo.Elements["/Subtype"] = new PdfName("/Form");
            xo.Elements["/BBox"] = new PdfArray(doc,
                new PdfReal(0), new PdfReal(0), new PdfReal(16), new PdfReal(16));
            xo.CreateStream(Encoding.ASCII.GetBytes(estado == "Off"
                ? "0.6 w 0 G 1 1 14 14 re S"
                : "0.6 w 0 G 1 1 14 14 re S 0 g 4 4 8 8 re f"));
            n.Elements["/" + estado] = xo.Reference!;
        }

        var ap = new PdfDictionary(doc);
        ap.Elements["/N"] = n;
        return ap;
    }

    /// <summary>Documento de dos páginas con aspecto de contrato.</summary>
    public static byte[] CrearPdf()
    {
        EmbeddedFontResolver.Register();

        using var doc = new PdfDocument();
        foreach (var titulo in new[] { "CONTRATO DE PRESTACION DE SERVICIOS", "ANEXO I - CONDICIONES" })
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;

            using var g = XGraphics.FromPdfPage(page);
            g.DrawString(titulo, new XFont("Open Sans", 14, XFontStyleEx.Bold),
                XBrushes.DimGray, new XPoint(60, 70));

            for (var i = 0; i < 20; i++)
                g.DrawRectangle(XBrushes.WhiteSmoke, 60, 100 + i * 22, 475, 12);
        }

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    /// <summary>PNG con canal alfa que simula una firma manuscrita.</summary>
    public static byte[] CrearFirma(int w = 360, int h = 130)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < w; x++)
            {
                var t = x / (double)w;
                var curva = h / 2 + Math.Sin(t * 9) * h * 0.3 - t * h * 0.15;
                var tinta = Math.Abs(y - curva) < 2.5 + Math.Sin(t * 20) * 1.2;
                raw.WriteByte(20);
                raw.WriteByte(20);
                raw.WriteByte(90);
                raw.WriteByte((byte)(tinta ? 255 : 0));
            }
        }

        var comprimido = new MemoryStream();
        using (var z = new ZLibStream(comprimido, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var ihdr = new MemoryStream();
        ihdr.Write(BigEndian(w));
        ihdr.Write(BigEndian(h));
        ihdr.Write([8, 6, 0, 0, 0]);
        Bloque(png, "IHDR", ihdr.ToArray());
        Bloque(png, "IDAT", comprimido.ToArray());
        Bloque(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static void Bloque(Stream s, string tipo, byte[] datos)
    {
        s.Write(BigEndian(datos.Length));
        var cuerpo = Encoding.ASCII.GetBytes(tipo).Concat(datos).ToArray();
        s.Write(cuerpo);
        s.Write(BigEndian(unchecked((int)Crc32(cuerpo))));
    }

    private static uint Crc32(byte[] d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d)
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
#endif
