using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfSigner.Core;

/// <summary>Lee los campos de formulario de un PDF.</summary>
/// <remarks>
/// Rellenar y aplanar llegan en fases posteriores. Esta primera se ocupa solo de averiguar
/// qué campos hay, de qué tipo, y DÓNDE están en la página, que es la parte con trampa:
/// el <c>/Rect</c> de un campo viene en coordenadas PDF y hay que traducirlo al espacio
/// visible contando la rotación de la página y el origen del MediaBox.
/// </remarks>
public sealed class PdfFormService
{
    /// <summary>True si el documento trae un formulario con al menos un campo.</summary>
    public bool HasForm(Stream pdf)
    {
        using var doc = PdfStreams.Open(pdf, PdfDocumentOpenMode.Modify);
        return ObtenerFormulario(doc) is { Fields.Count: > 0 };
    }

    /// <summary>
    /// Obtiene el formulario del documento, o null si no tiene.
    /// </summary>
    /// <remarks>
    /// Existe porque <c>PdfDocument.AcroForm</c> NO devuelve null cuando el documento carece
    /// de formulario: LANZA una InvalidOperationException desde su propia comprobación de
    /// nulos. Como la mayoría de los PDF no son formularios, acceder a la propiedad sin este
    /// rodeo tumbaría la aplicación al abrir casi cualquier documento.
    /// </remarks>
    private static PdfAcroForm? ObtenerFormulario(PdfDocument doc)
    {
        if (!doc.Internals.Catalog.Elements.ContainsKey("/AcroForm"))
            return null;

        try
        {
            return doc.AcroForm;
        }
        catch (InvalidOperationException)
        {
            // La entrada existe pero no se puede materializar: formulario corrupto o vacío.
            return null;
        }
    }

    /// <summary>Lee todos los campos del formulario, o una lista vacía si no hay.</summary>
    public IReadOnlyList<PdfFormField> Read(Stream pdf)
    {
        using var doc = PdfStreams.Open(pdf, PdfDocumentOpenMode.Modify);

        var form = ObtenerFormulario(doc);
        if (form is null || form.Fields.Count == 0)
            return [];

        var geometrias = doc.Pages.Cast<PdfPage>().Select(PageGeometry.FromPage).ToList();
        var paginaDeAnotacion = MapearAnotacionesAPaginas(doc);

        var campos = new List<PdfFormField>();

        foreach (var nombre in form.Fields.Names)
        {
            var campo = form.Fields[nombre];
            if (campo is null)
                continue;

            var tipo = Clasificar(campo);
            var widgets = LeerWidgets(campo, tipo, geometrias, paginaDeAnotacion);

            // Un campo sin ninguna zona dibujable no se puede mostrar ni tocar. Ocurre con
            // campos ocultos o heredados de plantillas; se omiten en vez de aparecer como
            // cajas fantasma en la esquina superior izquierda.
            if (widgets.Count == 0)
                continue;

            campos.Add(new PdfFormField
            {
                Name = nombre,
                Kind = tipo,
                Widgets = widgets,
                Value = LeerValor(campo, tipo),
                Options = LeerOpciones(campo, tipo, widgets),
                ReadOnly = campo.ReadOnly,
                MaxLength = campo is PdfTextField t ? t.MaxLength : 0,
                MultiLine = campo is PdfTextField m && m.MultiLine,
            });
        }

        return campos;
    }

    // ------------------------------------------------------------------ tipo

    private static PdfFormFieldKind Clasificar(PdfAcroField campo) => campo switch
    {
        PdfTextField => PdfFormFieldKind.Text,
        PdfCheckBoxField => PdfFormFieldKind.CheckBox,
        PdfRadioButtonField => PdfFormFieldKind.RadioGroup,
        PdfComboBoxField => PdfFormFieldKind.ComboBox,
        PdfListBoxField => PdfFormFieldKind.ListBox,
        PdfSignatureField => PdfFormFieldKind.Signature,
        _ => PdfFormFieldKind.Unsupported,
    };

    // --------------------------------------------------------------- widgets

    private static IReadOnlyList<PdfFormWidget> LeerWidgets(
        PdfAcroField campo,
        PdfFormFieldKind tipo,
        IReadOnlyList<PageGeometry> geometrias,
        IReadOnlyDictionary<PdfObjectID, int> paginaDeAnotacion)
    {
        var widgets = new List<PdfFormWidget>();

        // Con /Kids el campo se dibuja en varios sitios: un grupo de radio tiene una zona por
        // opción. Sin /Kids, el propio diccionario del campo es también su widget.
        var hijos = campo.Elements.GetArray("/Kids");
        var fuentes = new List<PdfDictionary>();

        if (hijos is not null)
        {
            for (var i = 0; i < hijos.Elements.Count; i++)
            {
                if (Resolver(hijos.Elements[i]) is { } hijo)
                    fuentes.Add(hijo);
            }
        }
        else
        {
            fuentes.Add(campo);
        }

        foreach (var fuente in fuentes)
        {
            var rect = LeerRectangulo(fuente);
            if (rect is null)
                continue;

            var pagina = BuscarPagina(fuente, paginaDeAnotacion);
            if (pagina < 0 || pagina >= geometrias.Count)
                continue;

            var (x1, y1, x2, y2) = rect.Value;
            var bounds = geometrias[pagina].ToNormalizedRect(x1, y1, x2, y2);

            // En un grupo de radio, el valor de cada opción es la clave de su apariencia
            // distinta de /Off. Es el dato que hará falta para marcar la correcta.
            var opcion = tipo == PdfFormFieldKind.RadioGroup ? LeerNombreDeOpcion(fuente) : null;

            widgets.Add(new PdfFormWidget(pagina, bounds, opcion));
        }

        return widgets;
    }

    private static (double X1, double Y1, double X2, double Y2)? LeerRectangulo(PdfDictionary dic)
    {
        var rect = dic.Elements.GetArray("/Rect");
        if (rect is null || rect.Elements.Count < 4)
            return null;

        return (rect.Elements.GetReal(0), rect.Elements.GetReal(1),
                rect.Elements.GetReal(2), rect.Elements.GetReal(3));
    }

    /// <summary>Clave de la apariencia "encendida" de un widget de radio.</summary>
    private static string? LeerNombreDeOpcion(PdfDictionary widget)
    {
        var normales = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (normales is null)
            return null;

        foreach (var clave in normales.Elements.Keys)
        {
            if (!string.Equals(clave, "/Off", StringComparison.Ordinal))
                return clave.TrimStart('/');
        }

        return null;
    }

    // --------------------------------------------------------------- páginas

    /// <summary>
    /// Relaciona cada anotación con la página que la contiene.
    /// </summary>
    /// <remarks>
    /// Se recorren las páginas en vez de fiarse de la entrada <c>/P</c> del widget: es
    /// opcional en el estándar y hay generadores que la omiten. La lista de anotaciones de
    /// la página, en cambio, tiene que estar para que el campo se vea.
    /// </remarks>
    private static Dictionary<PdfObjectID, int> MapearAnotacionesAPaginas(PdfDocument doc)
    {
        var mapa = new Dictionary<PdfObjectID, int>();

        for (var i = 0; i < doc.PageCount; i++)
        {
            var annots = doc.Pages[i].Elements.GetArray("/Annots");
            if (annots is null)
                continue;

            for (var j = 0; j < annots.Elements.Count; j++)
            {
                if (annots.Elements[j] is PdfReference r)
                    mapa[r.ObjectID] = i;
            }
        }

        return mapa;
    }

    private static int BuscarPagina(PdfDictionary widget, IReadOnlyDictionary<PdfObjectID, int> mapa)
    {
        // Lo normal: el widget aparece en la lista de anotaciones de alguna página.
        if (widget.Reference is { } referencia && mapa.TryGetValue(referencia.ObjectID, out var pagina))
            return pagina;

        // Reserva para documentos donde el widget no está en /Annots pero sí declara su
        // página en /P. Es un PDF mal formado, pero se encuentran.
        if (widget.Elements["/P"] is PdfReference p && mapa.TryGetValue(p.ObjectID, out var porP))
            return porP;

        return -1;
    }

    // --------------------------------------------------------- valor y opciones

    private static string? LeerValor(PdfAcroField campo, PdfFormFieldKind tipo)
    {
        if (tipo == PdfFormFieldKind.Text && campo is PdfTextField t)
            return t.Text;

        return ComoTexto(campo.Elements["/V"]);
    }

    private static IReadOnlyList<string> LeerOpciones(
        PdfAcroField campo, PdfFormFieldKind tipo, IReadOnlyList<PdfFormWidget> widgets)
    {
        if (tipo == PdfFormFieldKind.RadioGroup)
        {
            return [.. widgets.Select(w => w.OptionName).Where(o => o is not null).Distinct()!];
        }

        if (tipo is not (PdfFormFieldKind.ComboBox or PdfFormFieldKind.ListBox))
            return [];

        var opt = campo.Elements.GetArray("/Opt");
        if (opt is null)
            return [];

        var opciones = new List<string>();
        for (var i = 0; i < opt.Elements.Count; i++)
        {
            // Cada entrada puede ser una cadena suelta o un par [valor, etiqueta]. Se queda
            // la etiqueta cuando existe, porque es lo que el usuario espera leer.
            var item = Resolver(opt.Elements[i]) as PdfItem ?? opt.Elements[i];
            if (item is PdfArray par && par.Elements.Count > 0)
            {
                var indice = par.Elements.Count > 1 ? 1 : 0;
                if (ComoTexto(par.Elements[indice]) is { } etiqueta)
                    opciones.Add(etiqueta);
            }
            else if (ComoTexto(item) is { } suelta)
            {
                opciones.Add(suelta);
            }
        }

        return opciones;
    }

    private static string? ComoTexto(PdfItem? item) => item switch
    {
        PdfString s => s.Value,
        PdfName n => n.Value.TrimStart('/'),
        PdfReference r => ComoTexto(r.Value),
        _ => null,
    };

    private static PdfDictionary? Resolver(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };
}
