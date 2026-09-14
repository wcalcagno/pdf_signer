using CommunityToolkit.Mvvm.ComponentModel;
using PdfSigner.Core;

namespace PdfSigner.App.ViewModels;

/// <summary>Envuelve un elemento colocado y lo traduce a píxeles para poder dibujarlo.</summary>
/// <remarks>
/// El modelo guarda coordenadas normalizadas 0..1 y la interfaz necesita píxeles. Toda esa
/// conversión vive aquí y en un único sentido: el modelo manda, la vista se deriva. Así el PDF
/// exportado nunca depende del tamaño de la ventana ni del nivel de zoom.
/// </remarks>
public sealed partial class ElementViewModel : ObservableObject
{
    /// <summary>Tamaño mínimo en pantalla, para que un elemento no pueda perderse de vista.</summary>
    private const double MinimumNormalizedSize = 0.02;

    private double _canvasWidth;
    private double _canvasHeight;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ElementViewModel(PlacedElement element, byte[]? imageData = null)
    {
        Element = element;
        ImageData = imageData;
    }

    public PlacedElement Element { get; }

    /// <summary>Bytes de la imagen, solo para los elementos de tipo imagen.</summary>
    public byte[]? ImageData { get; }

    public bool IsImage => Element is ImageElement;

    public bool IsText => Element is TextElement;

    public TextElement? AsText => Element as TextElement;

    public int PageIndex => Element.PageIndex;

    public double X => Element.Bounds.X * _canvasWidth;

    public double Y => Element.Bounds.Y * _canvasHeight;

    public double Width => Element.Bounds.Width * _canvasWidth;

    public double Height => Element.Bounds.Height * _canvasHeight;

    /// <summary>
    /// Posición como margen, que es la forma fiable de colocar hijos en un Grid de MAUI.
    /// </summary>
    /// <remarks>
    /// Se descartó AbsoluteLayout con posicionamiento proporcional: su interpolación tiene en
    /// cuenta el tamaño del propio elemento, de modo que 0.5 no significa "a la mitad de la
    /// página". Eso desalinearía la vista previa respecto del PDF exportado.
    /// </remarks>
    public Thickness Position => new(X, Y, 0, 0);

    // ---- Contenedor ampliado para las asas ----
    //
    // Las asas se dibujan centradas sobre las esquinas, o sea que la mitad de cada una queda
    // fuera del elemento. Dibujarlas fuera del contenedor funciona, pero los CLICS en esa
    // zona no: en XAML el hit-testing no suele salir de los límites del contenedor padre.
    // El resultado era que solo respondía el cuarto interior de cada asa, que además compite
    // con el gesto de mover del propio elemento, y hacían falta varios intentos para agarrar
    // una esquina.
    //
    // La solución es que el contenedor abarque también las asas: se agranda un radio de asa
    // por cada lado y el elemento se dibuja centrado dentro, con ese mismo margen.

    /// <summary>Radio del área táctil del asa. La mitad de los 44 px recomendados.</summary>
    public const double HandleRadius = 22;

    /// <summary>Margen interior del elemento dentro del contenedor ampliado.</summary>
    public Thickness HitPadding => new(HandleRadius);

    public Thickness HitPosition => new(X - HandleRadius, Y - HandleRadius, 0, 0);

    public double HitWidth => Width + HandleRadius * 2;

    public double HitHeight => Height + HandleRadius * 2;

    /// <summary>Fuente de imagen lista para enlazar, o null si es un bloque de texto.</summary>
    public ImageSource? Preview =>
        ImageData is { Length: > 0 } data ? ImageSource.FromStream(() => new MemoryStream(data)) : null;

    /// <summary>Informa del tamaño en píxeles de la página mostrada.</summary>
    public void SetCanvasSize(double width, double height)
    {
        if (Math.Abs(_canvasWidth - width) < 0.5 && Math.Abs(_canvasHeight - height) < 0.5)
            return;

        _canvasWidth = width;
        _canvasHeight = height;
        NotifyGeometryChanged();
    }

    /// <summary>Desplaza el elemento. Los incrementos llegan en píxeles de pantalla.</summary>
    public void Move(double deltaX, double deltaY)
    {
        if (_canvasWidth <= 0 || _canvasHeight <= 0)
            return;

        Element.Bounds = Element.Bounds.Moved(deltaX / _canvasWidth, deltaY / _canvasHeight);
        NotifyGeometryChanged();
    }

    /// <summary>Redimensiona arrastrando una esquina; la opuesta se queda donde está.</summary>
    /// <remarks>
    /// La aritmética está en NormalizedRect, dentro del núcleo, donde sí hay pruebas. Aquí
    /// solo se convierten píxeles de pantalla a proporción del lienzo.
    ///
    /// El mismo código sirve para ratón y para dedo: un arrastre es un arrastre, sin ninguna
    /// rama por plataforma.
    /// </remarks>
    public void Resize(ResizeCorner corner, double deltaX, double deltaY)
    {
        if (_canvasWidth <= 0 || _canvasHeight <= 0)
            return;

        Element.Bounds = Element.Bounds.Resized(
            corner, deltaX / _canvasWidth, deltaY / _canvasHeight, MinimumNormalizedSize);

        NotifyGeometryChanged();
    }

    public void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(HitPosition));
        OnPropertyChanged(nameof(HitWidth));
        OnPropertyChanged(nameof(HitHeight));
    }

    public void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(TextColor));
    }

    // ---- Propiedades editables del bloque de texto ----
    // Escriben directo en el modelo, que es la única fuente de verdad de cara a la exportación.

    public string Text
    {
        get => AsText?.Text ?? string.Empty;
        set
        {
            if (AsText is not { } t || t.Text == value)
                return;

            t.Text = value;
            OnPropertyChanged();
            NotifyTextChanged();
        }
    }

    public double FontSizePt
    {
        get => AsText?.FontSizePt ?? 11;
        set
        {
            if (AsText is not { } t || Math.Abs(t.FontSizePt - value) < 0.01)
                return;

            t.FontSizePt = value;
            OnPropertyChanged();
            NotifyTextChanged();
        }
    }

    public string ColorHex
    {
        get => AsText?.ColorHex ?? "#000000";
        set
        {
            if (AsText is not { } t || t.ColorHex == value)
                return;

            t.ColorHex = value;
            OnPropertyChanged();
            NotifyTextChanged();
        }
    }

    /// <summary>
    /// Texto con los marcadores ya resueltos, solo para la vista previa.
    /// </summary>
    /// <remarks>
    /// El elemento conserva el <c>{fecha}</c> sin resolver; la sustitución de verdad ocurre al
    /// exportar. Aquí se resuelve únicamente para que el usuario vea cómo va a quedar.
    /// </remarks>
    public string DisplayText =>
        AsText is { } t ? PlaceholderResolver.Resolve(t.Text, DateTime.Now) : string.Empty;

    public double FontSize => AsText?.FontSizePt ?? 11;

    public Color TextColor =>
        AsText is { } t && Color.TryParse(t.ColorHex, out var color) ? color : Colors.Black;

    // ---- Apariencia de la selección y del arrastre ----
    //
    // Se exponen como propiedades y no como convertidores en XAML para que la plantilla quede
    // legible y no haya que registrar convertidores solo para pintar un borde.

    /// <summary>El elemento se está moviendo o redimensionando ahora mismo.</summary>
    /// <remarks>
    /// Sirve únicamente para dar respuesta visual inmediata. El usuario necesita saber que ha
    /// agarrado algo en el instante en que lo agarra, sobre todo con el dedo, donde además
    /// tiene el propio dedo tapando el elemento. Ponerlo en la barra de estado llegaría tarde
    /// y al sitio equivocado: el ojo está en la página, no en el pie de la ventana.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    public Color OutlineColor => IsSelected ? Colors.DodgerBlue : Colors.Transparent;

    public double OutlineThickness => IsDragging ? 3.5 : IsSelected ? 2 : 0;

    /// <summary>Ampliación sutil mientras se arrastra: da sensación de "levantado".</summary>
    /// <remarks>
    /// Se aplica solo al contenido, no a las asas: escalarlas las movería respecto de las
    /// esquinas justo mientras se está apuntando a ellas.
    ///
    /// Es un 2%: suficiente para notarlo, insuficiente para que el elemento parezca cambiar
    /// de tamaño, que es precisamente lo que se está a punto de hacer con las asas.
    /// </remarks>
    public double ContentScale => IsDragging ? 1.02 : 1.0;

    public double ContentOpacity => IsDragging ? 0.9 : 1.0;

    partial void OnIsSelectedChanged(bool value)
    {
        // Soltar la selección cancela cualquier arrastre en curso.
        if (!value)
            IsDragging = false;

        OnPropertyChanged(nameof(OutlineColor));
        OnPropertyChanged(nameof(OutlineThickness));
    }

    partial void OnIsDraggingChanged(bool value)
    {
        OnPropertyChanged(nameof(OutlineThickness));
        OnPropertyChanged(nameof(ContentScale));
        OnPropertyChanged(nameof(ContentOpacity));
    }
}
