using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;

namespace PdfSigner.Core;

/// <summary>
/// Dibuja el contenido visible de un campo de formulario relleno.
/// </summary>
/// <remarks>
/// Un campo de PDF guarda su valor en <c>/V</c>, pero lo que el visor PINTA es el flujo de
/// apariencia de <c>/AP /N</c>. Son dos cosas distintas: si solo se escribe el valor, el
/// documento lleva el dato correcto y se ve en blanco. PDFsharp genera apariencia para los
/// campos de texto y para ningún otro, así que aquí se genera para todos por igual: una sola
/// ruta de código, y el mismo aspecto se rellene lo que se rellene.
///
/// Se podría delegar en el visor poniendo <c>/NeedAppearances true</c>, pero eso es una
/// petición, no una garantía: los rasterizadores del sistema la ignoran, y entonces el PDF
/// exportado se vería vacío justo en la vista previa de la propia aplicación.
/// </remarks>
internal static class FormAppearance
{
    private const double Margen = 2;

    /// <summary>Genera y asigna la apariencia de un widget con contenido de texto.</summary>
    public static void Escribir(
        PdfAcroForm form, PdfDictionary widget, string? texto,
        bool multilinea = false, bool desdeArriba = false)
    {
        var doc = widget.Owner;
        if (Tamano(widget) is not var (ancho, alto) || ancho <= 0 || alto <= 0)
            return;

        var (nombreFuente, tamano, color) = LeerApariencia(widget, form, alto);
        var fuente = BuscarFuente(form, nombreFuente) ?? CrearHelvetica(doc);

        string[] lineas = string.IsNullOrEmpty(texto)
            ? []
            : multilinea ? texto.Replace("\r\n", "\n").Split('\n') : [texto.Replace("\n", " ")];

        var contenido = new StringBuilder();

        // /Tx BMC ... EMC marca el bloque como contenido de formulario: es lo que permite al
        // visor reconocer la apariencia como suya y regenerarla si el usuario edita el campo.
        contenido.AppendLine("/Tx BMC");
        contenido.AppendLine("q");

        if (lineas.Length > 0)
        {
            // Recorte a la caja: un valor más largo que el campo se corta en el borde en vez
            // de desbordar sobre el resto de la página.
            contenido.AppendLine($"1 1 {F(ancho - 2)} {F(alto - 2)} re W n");
            contenido.AppendLine("BT");
            contenido.AppendLine($"{nombreFuente} {F(tamano)} Tf");
            contenido.AppendLine(color);

            // Una línea suelta se centra verticalmente en su caja; un bloque de varias, y
            // la fila elegida de una lista, se pegan al borde de arriba.
            var interlineado = tamano * 1.2;
            var baseY = desdeArriba || lineas.Length > 1
                ? alto - Margen - tamano * 0.85
                : (alto - tamano) / 2 + tamano * 0.25;

            contenido.AppendLine($"{F(Margen)} {F(baseY)} Td");
            contenido.AppendLine($"{F(interlineado)} TL");
            contenido.AppendLine($"{Literal(lineas[0])} Tj");
            for (var i = 1; i < lineas.Length; i++)
                contenido.AppendLine($"T* {Literal(lineas[i])} Tj");

            contenido.AppendLine("ET");
        }

        contenido.AppendLine("Q");
        contenido.AppendLine("EMC");

        var xo = new PdfDictionary(doc);
        doc.Internals.AddObject(xo);
        xo.Elements["/Type"] = new PdfName("/XObject");
        xo.Elements["/Subtype"] = new PdfName("/Form");
        xo.Elements["/FormType"] = new PdfInteger(1);
        xo.Elements["/BBox"] = new PdfArray(doc,
            new PdfReal(0), new PdfReal(0), new PdfReal(ancho), new PdfReal(alto));

        var fuentes = new PdfDictionary(doc);
        fuentes.Elements[nombreFuente] = fuente;
        var recursos = new PdfDictionary(doc);
        recursos.Elements["/Font"] = fuentes;
        xo.Elements["/Resources"] = recursos;

        xo.CreateStream(Encoding.Latin1.GetBytes(contenido.ToString()));

        // /AP se deja como diccionario DIRECTO por la misma razón que en las casillas: con un
        // objeto indirecto PDFsharp deja de encontrar los estados al releer el documento.
        var ap = new PdfDictionary(doc);
        ap.Elements["/N"] = xo.Reference!;
        widget.Elements["/AP"] = ap;
    }

    // ------------------------------------------------------------------- datos

    private static (double Ancho, double Alto)? Tamano(PdfDictionary widget)
    {
        var rect = widget.Elements.GetArray("/Rect");
        if (rect is null || rect.Elements.Count < 4)
            return null;

        return (Math.Abs(rect.Elements.GetReal(2) - rect.Elements.GetReal(0)),
                Math.Abs(rect.Elements.GetReal(3) - rect.Elements.GetReal(1)));
    }

    /// <summary>
    /// Tamaño de letra con el que se va a escribir este campo, en puntos.
    /// </summary>
    /// <remarks>
    /// Lo usa la interfaz para dibujar la vista previa del campo con el MISMO tamaño que va a
    /// tener en el PDF exportado. Calcularlo por separado en la aplicación llevaría a que lo
    /// que se ve en pantalla y lo que se guarda no coincidieran en cuanto una de las dos
    /// reglas cambiara.
    /// </remarks>
    public static double TamanoDeLetra(PdfDictionary widget, PdfAcroForm form, double altoPt) =>
        LeerApariencia(widget, form, altoPt).Tamano;

    /// <summary>Lee fuente, tamaño y color del <c>/DA</c> del campo o, si no tiene, del formulario.</summary>
    private static (string Fuente, double Tamano, string Color) LeerApariencia(
        PdfDictionary widget, PdfAcroForm form, double alto)
    {
        var da = widget.Elements.GetString("/DA");
        if (string.IsNullOrEmpty(da))
            da = form.Elements.GetString("/DA");

        var fuente = "/Helv";
        double tamano = 0;
        var color = "0 g";

        var t = (da ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == "Tf" && i >= 2)
            {
                fuente = t[i - 2];
                _ = double.TryParse(t[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out tamano);
            }
            else if (t[i] is "g" or "rg" or "k")
            {
                // El operador de color va detrás de sus operandos: se copian tal cual en vez
                // de interpretarlos, así funcionan por igual gris, RGB y CMYK.
                var operandos = t[i] switch { "g" => 1, "rg" => 3, _ => 4 };
                if (i >= operandos)
                    color = string.Join(' ', t[(i - operandos)..(i + 1)]);
            }
        }

        // Tamaño 0 significa "ajústalo tú". Se escoge uno que quepa en la caja, con un tope
        // para que un campo alto no acabe con letras enormes.
        if (tamano <= 0)
            tamano = Math.Clamp(alto - 2 * Margen, 6, 12);

        return (fuente, tamano, color);
    }

    private static PdfItem? BuscarFuente(PdfAcroForm form, string nombre)
    {
        var fuentes = form.Elements.GetDictionary("/DR")?.Elements.GetDictionary("/Font");
        return fuentes is not null && fuentes.Elements.ContainsKey(nombre) ? fuentes.Elements[nombre] : null;
    }

    /// <summary>Helvetica estándar, para formularios que no declaran recursos propios.</summary>
    /// <remarks>
    /// Es una de las catorce fuentes que todo visor de PDF trae incorporadas, así que no hay
    /// que embeber nada y el archivo no engorda.
    /// </remarks>
    private static PdfItem CrearHelvetica(PdfDocument doc)
    {
        var f = new PdfDictionary(doc);
        doc.Internals.AddObject(f);
        f.Elements["/Type"] = new PdfName("/Font");
        f.Elements["/Subtype"] = new PdfName("/Type1");
        f.Elements["/BaseFont"] = new PdfName("/Helvetica");
        f.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        return f.Reference!;
    }

    // ------------------------------------------------------------------- texto

    private static string F(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);

    /// <summary>Convierte el texto en una cadena literal de PDF.</summary>
    /// <remarks>
    /// Se codifica en Latin-1 porque la fuente declara /WinAnsiEncoding, que coincide con
    /// Latin-1 en todo el rango de las letras acentuadas. Lo que caiga fuera (un guion largo,
    /// el símbolo del euro) sale como '?', preferible a escribir un byte que el visor
    /// interpretaría como otra letra.
    /// </remarks>
    private static string Literal(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var c in s)
        {
            if (c is '(' or ')' or '\\')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append(')').ToString();
    }
}
