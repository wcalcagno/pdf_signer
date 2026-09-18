using CommunityToolkit.Mvvm.ComponentModel;
using PdfSigner.Core;

namespace PdfSigner.App.ViewModels;

/// <summary>Una zona rellenable del formulario, colocada sobre la página.</summary>
/// <remarks>
/// Representa un WIDGET, no un campo: un grupo de radio se dibuja en varios sitios y cada
/// opción necesita su propia caja en pantalla. Todas las zonas de un mismo grupo comparten el
/// objeto <see cref="PdfFormField"/>, que es donde vive el valor; por eso marcar una apaga las
/// demás sin necesidad de coordinarlas entre sí.
///
/// Igual que con las firmas, el modelo manda: las coordenadas son las normalizadas que leyó el
/// núcleo y aquí solo se traducen a píxeles. Lo que se exporta no depende del zoom.
/// </remarks>
public sealed partial class FormFieldViewModel : ObservableObject
{
    private readonly PdfFormField _campo;
    private readonly PdfFormWidget _zona;
    private readonly Action<FormFieldViewModel>? _alCambiar;

    private double _anchoLienzo;
    private double _altoLienzo;

    public FormFieldViewModel(
        PdfFormField campo, PdfFormWidget zona, Action<FormFieldViewModel>? alCambiar = null)
    {
        _campo = campo;
        _zona = zona;
        _alCambiar = alCambiar;
    }

    public string Name => _campo.Name;

    public PdfFormFieldKind Kind => _campo.Kind;

    public int PageIndex => _zona.PageIndex;

    public bool IsMultiLine => _campo.MultiLine;

    public IReadOnlyList<string> Options => _campo.Options;

    /// <summary>Opción que representa esta zona, en un grupo de radio. Null en el resto.</summary>
    public string? OptionName => _zona.OptionName;

    /// <summary>Nombre del campo, como pista de qué va en él.</summary>
    /// <remarks>
    /// Un formulario en papel lleva su rótulo impreso al lado, así que la caja vacía basta.
    /// Aquí se aprovecha el nombre interno del campo porque es lo único que el PDF garantiza:
    /// no siempre es bonito, pero es mejor que una caja sin ninguna indicación.
    /// </remarks>
    public string Placeholder => _campo.Name;

    // -------------------------------------------------------------- posición

    public double X => _zona.Bounds.X * _anchoLienzo;

    public double Y => _zona.Bounds.Y * _altoLienzo;

    public double Width => _zona.Bounds.Width * _anchoLienzo;

    public double Height => _zona.Bounds.Height * _altoLienzo;

    public Thickness Position => new(X, Y, 0, 0);

    /// <summary>Tamaño de letra de la vista previa, en píxeles de pantalla.</summary>
    /// <remarks>
    /// Sale del tamaño en PUNTOS que el núcleo va a usar al escribir el campo, convertido a
    /// píxeles con la escala de la propia zona: su alto en píxeles dividido por su alto en
    /// puntos. Así no hace falta conocer el tamaño de la página y, sobre todo, lo que se ve en
    /// pantalla es lo que va a quedar en el PDF.
    ///
    /// Deducirlo del alto de la caja, que es lo primero que uno intenta, funciona en un campo
    /// de una línea y falla justo en los de varias: la caja mide lo que ocupan todas, así que
    /// el texto salía del tamaño del párrafo entero.
    ///
    /// Se acota por abajo porque con la página muy reducida el texto se volvería ilegible, y
    /// un campo que no se puede leer no se puede comprobar.
    /// </remarks>
    public double FontSize
    {
        get
        {
            var escala = _zona.HeightPt > 0 ? Height / _zona.HeightPt : 1;
            var puntos = _campo.FontSizePt > 0 ? _campo.FontSizePt : 10;
            return Math.Clamp(puntos * escala, 8, 40);
        }
    }

    /// <summary>Alto que se le pide al control mientras se edita.</summary>
    /// <remarks>
    /// Un Entry mas bajo que esto no dibuja su texto en Windows: pinta el fondo y deja el
    /// contenido fuera de su recorte, asi que el usuario teclearia a ciegas. Con la pagina
    /// ajustada a la ventana, un campo de 20 puntos mide unos 15 pixeles, o sea que el caso
    /// es el normal y no el raro.
    ///
    /// Solo se aplica mientras se edita. Agrandar la caja siempre solaparia unos campos con
    /// otros, y un formulario apretado se volveria ilegible.
    /// </remarks>
    public double EditHeight => Math.Max(Height, 26);

    /// <summary>Lo que se ve en la caja cuando no se esta editando.</summary>
    /// <remarks>
    /// Se dibuja con una etiqueta propia en vez de dejar el control nativo siempre puesto,
    /// porque una etiqueta se puede encoger a la medida real del campo sin dejar de verse.
    /// </remarks>
    public string DisplayText => string.IsNullOrEmpty(_campo.Value) ? Placeholder : _campo.Value!;

    /// <summary>Gris cuando lo que se ve es la pista; tinta cuando es el valor.</summary>
    public Color DisplayColor => string.IsNullOrEmpty(_campo.Value)
        ? Color.FromArgb("#8E8E93")
        : Color.FromArgb("#1C1C1E");

    /// <summary>La caja esta abierta para escribir en ella.</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    public void SetCanvasSize(double width, double height)
    {
        if (Math.Abs(_anchoLienzo - width) < 0.5 && Math.Abs(_altoLienzo - height) < 0.5)
            return;

        _anchoLienzo = width;
        _altoLienzo = height;

        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(GlyphSize));
        OnPropertyChanged(nameof(EditHeight));
    }

    // ----------------------------------------------------------------- valor

    /// <summary>Valor del campo. Lo comparten todas las zonas del mismo campo.</summary>
    public string? Value
    {
        get => _campo.Value;
        set
        {
            if (_campo.Value == value)
                return;

            _campo.Value = value;
            NotifyValueChanged();
            _alCambiar?.Invoke(this);
        }
    }

    /// <summary>El valor como cadena no nula, que es lo que necesitan Entry y Editor.</summary>
    public string Text
    {
        get => _campo.Value ?? string.Empty;
        set => Value = string.IsNullOrEmpty(value) ? null : value;
    }

    public bool IsChecked => Kind switch
    {
        // En un grupo de radio gana la zona cuyo nombre de opción coincide con el valor.
        PdfFormFieldKind.RadioGroup => _zona.OptionName is { } o && o == _campo.Value,
        PdfFormFieldKind.CheckBox => _campo.Value is { Length: > 0 } v
            && !string.Equals(v, "Off", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Marca o desmarca esta zona.</summary>
    /// <remarks>
    /// Una casilla alterna. Un radio solo enciende: apagar el grupo entero tocando la opción
    /// ya marcada es un gesto que nadie busca y que además muchos formularios prohíben con el
    /// indicador NoToggleToOff.
    /// </remarks>
    public void Toggle()
    {
        Value = Kind switch
        {
            PdfFormFieldKind.RadioGroup => _zona.OptionName,
            PdfFormFieldKind.CheckBox => IsChecked ? null : "Yes",
            _ => Value,
        };
    }

    /// <summary>Marca que se dibuja dentro de la casilla o del radio.</summary>
    public string Glyph => IsChecked ? Kind == PdfFormFieldKind.RadioGroup ? "●" : "✓" : string.Empty;

    public double GlyphSize => Math.Clamp(Height * 0.7, 9, 30);

    public void NotifyValueChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DisplayColor));
    }

    /// <summary>Si la zona tiene algo escrito. Solo afecta al realce.</summary>
    public bool HasValue => !string.IsNullOrEmpty(_campo.Value);
}
