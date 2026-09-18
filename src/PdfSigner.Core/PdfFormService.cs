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
    /// <summary>
    /// Registra el resolver de fuentes, que hace falta incluso para LEER un formulario.
    /// </summary>
    /// <remarks>
    /// No es precaución: el constructor de PdfTextField de PDFsharp crea un XFont, así que
    /// enumerar los campos de un documento revienta con «No appropriate font found» si no hay
    /// resolver registrado. Dentro de la aplicación no se notaba porque el servicio de firma
    /// lo registra al construirse, y en las pruebas tampoco porque lo hace el generador de
    /// formularios: solo aparece al usar esta clase por su cuenta, que es justo lo que haría
    /// cualquiera que se lleve la biblioteca.
    /// </remarks>
    public PdfFormService() => EmbeddedFontResolver.Register();

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
            var widgets = LeerWidgets(campo, tipo, form, geometrias, paginaDeAnotacion);

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
                FontSizePt = LeerTamanoDeLetra(campo, form, widgets),
            });
        }

        return campos;
    }

    // ========================================================== rellenar

    /// <summary>
    /// Escribe los valores indicados en los campos del formulario y guarda el resultado.
    /// </summary>
    /// <param name="values">
    /// Valor por nombre de campo. Para una casilla, vacío o <c>"Off"</c> la desmarca y
    /// cualquier otra cosa la marca; para un grupo de radio hay que dar el nombre exacto de
    /// la opción. Los campos de solo lectura y los de firma se ignoran, de modo que se pueda
    /// devolver el mapa completo que dio <see cref="Read"/> sin filtrarlo.
    /// </param>
    /// <param name="flatten">
    /// Si es true, los campos se convierten en contenido fijo de la página y desaparecen del
    /// documento: lo que se ve queda igual pero ya no se puede modificar.
    /// </param>
    /// <remarks>
    /// Sin aplanar, los campos siguen siendo editables: rellenar no bloquea nada.
    /// </remarks>
    public void Fill(
        Stream input,
        Stream output,
        IReadOnlyDictionary<string, string?> values,
        bool flatten = false,
        bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(values);

        using var doc = PdfStreams.Open(input, PdfDocumentOpenMode.Modify);
        var form = ObtenerFormulario(doc);

        if (form is null || form.Fields.Count == 0)
        {
            if (values.Count > 0)
                throw new PdfSignerException(
                    "El documento no tiene campos de formulario, así que no hay nada que rellenar.");
        }
        else
        {
            var conocidos = form.Fields.Names;

            foreach (var (nombre, valor) in values)
            {
                // Un nombre que no existe casi siempre es una errata, y dejarlo pasar en
                // silencio significa exportar un documento a medio rellenar sin enterarse.
                if (!conocidos.Contains(nombre))
                    throw new PdfSignerException($"El formulario no tiene ningún campo llamado «{nombre}».");

                var campo = form.Fields[nombre];
                if (campo is null)
                    continue;

                var tipo = Clasificar(campo);
                if (campo.ReadOnly || tipo is PdfFormFieldKind.Signature or PdfFormFieldKind.Unsupported)
                    continue;

                Rellenar(form, campo, tipo, valor);
            }
        }

        if (flatten)
            FormFlattener.Aplanar(doc);

        doc.Options.CompressContentStreams = compress;
        doc.Options.NoCompression = !compress;
        doc.Save(output, closeStream: false);
    }

    /// <summary>
    /// Fija los campos tal y como están: pasa lo que se ve al contenido de la página y
    /// elimina el formulario.
    /// </summary>
    /// <remarks>
    /// Un documento ya aplanado no tiene campos, así que volver a aplanarlo no hace nada; y
    /// uno que nunca tuvo formulario sale igual que entró.
    /// </remarks>
    public void Flatten(Stream input, Stream output, bool compress = true) =>
        Fill(input, output, new Dictionary<string, string?>(), flatten: true, compress);

    private static void Rellenar(PdfAcroForm form, PdfAcroField campo, PdfFormFieldKind tipo, string? valor)
    {
        switch (tipo)
        {
            case PdfFormFieldKind.Text:
                RellenarTexto(form, campo, valor);
                break;

            case PdfFormFieldKind.CheckBox:
            case PdfFormFieldKind.RadioGroup:
                MarcarBoton(campo, tipo, valor);
                break;

            case PdfFormFieldKind.ComboBox:
            case PdfFormFieldKind.ListBox:
                RellenarEleccion(form, campo, tipo, valor);
                break;
        }
    }

    private static void RellenarTexto(PdfAcroForm form, PdfAcroField campo, string? valor)
    {
        var texto = valor ?? string.Empty;

        // /MaxLen es un límite del formulario, no una sugerencia: un visor no dejaría teclear
        // más, y guardar de más produce un documento que no se puede volver a editar entero.
        var maximo = campo is PdfTextField t ? t.MaxLength : 0;
        if (maximo > 0 && texto.Length > maximo)
            texto = texto[..maximo];

        if (texto.Length == 0)
            campo.Elements.Remove("/V");
        else
            campo.Elements["/V"] = CadenaPdf(texto);

        var multilinea = campo is PdfTextField m && m.MultiLine;
        foreach (var widget in FuentesDeWidget(campo))
            FormAppearance.Escribir(form, widget, texto, multilinea, desdeArriba: multilinea);
    }

    /// <summary>Marca una casilla o la opción elegida de un grupo de radio.</summary>
    /// <remarks>
    /// Se escriben los diccionarios a mano en vez de usar <c>Checked</c> o
    /// <c>SelectedIndex</c> de PDFsharp. <c>SelectedIndex</c> de un grupo de radio no hace
    /// NADA y no avisa: deja el valor en <c>/Off</c> y todos los widgets apagados, porque
    /// busca la opción en un array <c>/Opt</c> que los grupos de radio normalmente no traen.
    ///
    /// La clave está en <c>/AS</c>: es el estado que el visor DIBUJA, y hay que ponerlo en
    /// cada widget además del <c>/V</c> del campo. Si solo se escribe <c>/V</c>, el documento
    /// lleva la respuesta correcta y se ve sin marcar.
    /// </remarks>
    private static void MarcarBoton(PdfAcroField campo, PdfFormFieldKind tipo, string? valor)
    {
        var widgets = FuentesDeWidget(campo);

        var apagado = string.IsNullOrWhiteSpace(valor)
            || string.Equals(valor, "Off", StringComparison.OrdinalIgnoreCase)
            || (tipo == PdfFormFieldKind.CheckBox && valor is "false" or "0");

        string? elegido = null;
        if (!apagado)
        {
            // La casilla tiene un único estado encendido, sea cual sea su nombre (/Yes, /On,
            // /1...), así que vale el primero que aparezca. El grupo de radio tiene uno por
            // opción y hay que acertar con el pedido.
            elegido = tipo == PdfFormFieldKind.CheckBox
                ? widgets.Select(LeerNombreDeOpcion).FirstOrDefault(n => n is not null)
                : widgets.Select(LeerNombreDeOpcion)
                         .FirstOrDefault(n => string.Equals(n, valor, StringComparison.Ordinal));

            if (elegido is null)
                throw new PdfSignerException(
                    $"El campo «{campo.Name}» no admite el valor «{valor}».");
        }

        campo.Elements["/V"] = new PdfName("/" + (elegido ?? "Off"));

        foreach (var widget in widgets)
        {
            var suyo = LeerNombreDeOpcion(widget);
            var encendido = elegido is not null && string.Equals(suyo, elegido, StringComparison.Ordinal);
            widget.Elements["/AS"] = new PdfName(encendido ? "/" + elegido : "/Off");
        }
    }

    /// <summary>Selecciona una opción de un desplegable o de una lista.</summary>
    /// <remarks>
    /// Tampoco se usa <c>SelectedIndex</c> de PDFsharp: guarda la REPRESENTACIÓN del elemento
    /// del array <c>/Opt</c>, paréntesis incluidos, y el campo acaba valiendo literalmente
    /// <c>(Verde)</c> en vez de <c>Verde</c>.
    /// </remarks>
    private static void RellenarEleccion(
        PdfAcroForm form, PdfAcroField campo, PdfFormFieldKind tipo, string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            campo.Elements.Remove("/V");
            campo.Elements.Remove("/I");
        }
        else
        {
            var opciones = LeerOpciones(campo, tipo, []);
            var indice = opciones.ToList().IndexOf(valor);

            // Un valor fuera de la lista se rechaza: en un desplegable de verdad el usuario no
            // podría elegirlo, y aceptarlo generaría un formulario que otro programa no valida.
            if (indice < 0 && opciones.Count > 0)
                throw new PdfSignerException(
                    $"«{valor}» no es una de las opciones del campo «{campo.Name}».");

            campo.Elements["/V"] = CadenaPdf(valor);

            // /I es el índice resaltado. Las listas lo necesitan para marcar la fila; en los
            // desplegables es opcional, pero ponerlo no molesta y ayuda a los visores viejos.
            if (indice >= 0)
                campo.Elements["/I"] = new PdfArray(campo.Owner, new PdfInteger(indice));
            else
                campo.Elements.Remove("/I");
        }

        // En una lista el valor elegido ocupa la primera fila, no el centro de la caja.
        foreach (var widget in FuentesDeWidget(campo))
            FormAppearance.Escribir(
                form, widget, valor, desdeArriba: tipo == PdfFormFieldKind.ListBox);
    }

    /// <summary>Codifica el valor de un campo de forma que sobreviva a los acentos.</summary>
    /// <remarks>
    /// PDFDocEncoding no cubre todo Unicode, así que en cuanto el texto se sale del ASCII se
    /// guarda en UTF-16. Es lo que hace cualquier visor y evita que un nombre con tilde se
    /// relea convertido en otra cosa.
    /// </remarks>
    private static PdfString CadenaPdf(string valor) =>
        valor.All(char.IsAscii)
            ? new PdfString(valor, PdfStringEncoding.PDFDocEncoding)
            : new PdfString(valor, PdfStringEncoding.Unicode);

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
        PdfAcroForm form,
        IReadOnlyList<PageGeometry> geometrias,
        IReadOnlyDictionary<PdfObjectID, int> paginaDeAnotacion)
    {
        var widgets = new List<PdfFormWidget>();

        foreach (var fuente in FuentesDeWidget(campo))
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

            widgets.Add(new PdfFormWidget(pagina, bounds, opcion, Math.Abs(y2 - y1)));
        }

        return widgets;
    }

    /// <summary>Diccionarios donde el campo se dibuja realmente.</summary>
    /// <remarks>
    /// Con <c>/Kids</c> el campo aparece en varios sitios: un grupo de radio tiene una zona
    /// por opción. Sin <c>/Kids</c>, el propio diccionario del campo es también su widget.
    /// </remarks>
    private static IReadOnlyList<PdfDictionary> FuentesDeWidget(PdfAcroField campo)
    {
        var hijos = campo.Elements.GetArray("/Kids");
        if (hijos is null)
            return [campo];

        var fuentes = new List<PdfDictionary>();
        for (var i = 0; i < hijos.Elements.Count; i++)
        {
            if (Resolver(hijos.Elements[i]) is { } hijo)
                fuentes.Add(hijo);
        }

        return fuentes;
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

    /// <summary>Tamaño de letra del campo, preguntándoselo a quien lo va a dibujar.</summary>
    private static double LeerTamanoDeLetra(
        PdfAcroField campo, PdfAcroForm form, IReadOnlyList<PdfFormWidget> widgets)
    {
        if (widgets.Count == 0)
            return 0;

        var zona = FuentesDeWidget(campo).FirstOrDefault() ?? campo;
        return FormAppearance.TamanoDeLetra(zona, form, widgets[0].HeightPt);
    }

    // --------------------------------------------------------- valor y opciones

    private static string? LeerValor(PdfAcroField campo, PdfFormFieldKind tipo)
    {
        // Un campo sin rellenar se devuelve como null y no como cadena vacía: PdfTextField.Text
        // no distingue entre "no tiene /V" y "tiene /V vacío", y la diferencia importa para
        // saber si al usuario le queda algo por escribir.
        var valor = tipo == PdfFormFieldKind.Text && campo is PdfTextField t
            ? t.Text
            : ComoTexto(campo.Elements["/V"]);

        return string.IsNullOrEmpty(valor) ? null : valor;
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
