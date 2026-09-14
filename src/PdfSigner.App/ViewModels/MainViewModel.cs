using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfSigner.App.Services;
using PdfSigner.Core;

namespace PdfSigner.App.ViewModels;

/// <summary>Estado y acciones de la pantalla principal.</summary>
/// <remarks>
/// Toda la lógica de firma vive en PdfSigner.Core; esta clase solo orquesta: elegir archivos,
/// pedir el rasterizado de la página y entregar el resultado. Es también lo que permite que la
/// misma interfaz sirva en escritorio y en móvil, porque ninguna decisión depende del gesto
/// concreto con el que se haya originado.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Ancho al que se rasteriza la página principal. Compromiso entre nitidez y memoria.</summary>
    private const int MainRenderWidth = 1200;

    private const int ThumbnailWidth = 110;

    private readonly IPdfRasterizer _rasterizer;
    private readonly IFileExporter _exporter;
    private readonly FavoriteSignatureStore _favorites;
    private readonly PdfSignatureService _signer = new();

    private byte[]? _pdfBytes;
    private string _sourceFileName = "documento.pdf";

    [ObservableProperty]
    public partial ImageSource? CurrentPageImage { get; set; }

    [ObservableProperty]
    public partial double CanvasWidth { get; set; }

    [ObservableProperty]
    public partial double CanvasHeight { get; set; }

    [ObservableProperty]
    public partial int CurrentPageIndex { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial double Zoom { get; set; }

    [ObservableProperty]
    public partial ElementViewModel? Selected { get; set; }

    public MainViewModel(
        IPdfRasterizer rasterizer, IFileExporter exporter, FavoriteSignatureStore favorites)
    {
        _rasterizer = rasterizer;
        _exporter = exporter;
        _favorites = favorites;

        // Las partial properties no aceptan inicializador; los valores de partida van aquí.
        Status = "Abre un PDF para empezar.";
        Zoom = 1;
    }

    public ObservableCollection<PageViewModel> Pages { get; } = [];

    /// <summary>Todos los elementos del documento, de todas las páginas.</summary>
    public ObservableCollection<ElementViewModel> Elements { get; } = [];

    /// <summary>Solo los elementos anclados a la página que se está viendo.</summary>
    public ObservableCollection<ElementViewModel> VisibleElements { get; } = [];

    public bool HasDocument => _pdfBytes is { Length: > 0 };

    public bool HasFavorite => _favorites.Load() is { IsEmpty: false };

    // ---------------------------------------------------------------- abrir

    [RelayCommand]
    private async Task OpenPdfAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Elige un PDF",
            FileTypes = PdfFileType,
        });

        if (file is null)
            return;

        await RunBusyAsync("Abriendo el documento...", async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _pdfBytes = ms.ToArray();
            _sourceFileName = file.FileName;

            Elements.Clear();
            VisibleElements.Clear();
            Pages.Clear();

            // El historial apunta a elementos del documento anterior: deshacer sobre el nuevo
            // haría reaparecer firmas que no son de aquí.
            _historial.Clear();
            NotificarHistorial();

            using var probe = new MemoryStream(_pdfBytes);
            var geometry = _signer.Inspect(probe);
            for (var i = 0; i < geometry.Count; i++)
                Pages.Add(new PageViewModel(i));

            CurrentPageIndex = 0;
            await ShowPageAsync(0);
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(HasNoDocument));

            Status = $"{Pages.Count} página(s). Añade una firma o un texto.";
        });

        // Las miniaturas se generan después de mostrar la página, para que la app responda
        // enseguida aunque el documento tenga muchas páginas.
        _ = GenerateThumbnailsAsync();
    }

#if DEBUG
    /// <summary>
    /// Carga un documento de ejemplo con una firma y un texto ya colocados.
    /// </summary>
    /// <remarks>
    /// Andamio de desarrollo, solo en depuración y solo si está la variable PDFSIGNER_DEMO.
    /// Permite revisar con la vista los estados que dependen de tener algo seleccionado, que
    /// de otro modo obligan a abrir un archivo a mano en cada comprobación.
    /// </remarks>
    public async Task LoadDemoAsync()
    {
        _pdfBytes = Services.DemoContent.CrearPdf();
        _sourceFileName = "contrato-ejemplo.pdf";

        Elements.Clear();
        VisibleElements.Clear();
        Pages.Clear();

        using (var probe = new MemoryStream(_pdfBytes))
        {
            for (var i = 0; i < _signer.Inspect(probe).Count; i++)
                Pages.Add(new PageViewModel(i));
        }

        CurrentPageIndex = 0;
        await ShowPageAsync(0);
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(HasNoDocument));

        AddImageElement(Services.DemoContent.CrearFirma());
        Register(new ElementViewModel(new TextElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.12, 0.74, 0.42, 0.10),
            Text = "Walter E. Calcagno Lucares\nDirector\nFirmado el {fecha}",
            FontSizePt = 10,
            ColorHex = "#14145A",
        }));

        if (Services.DemoContent.SimularArrastre && Selected is not null)
            Selected.IsDragging = true;

        Status = "Documento de ejemplo cargado (modo demo).";

        // Modos que ejercitan el deshacer sin necesidad de un gesto real. Una captura es
        // estática, así que esta es la única forma de comprobar que el mecanismo funciona.
        if (Services.DemoContent.Modo is "moved" or "undo" && Selected is not null)
        {
            PushSnapshot(Selected, "mover el elemento");
            Selected.Move(140, -110);
            Status = $"Elemento desplazado. Deshacer disponible: {CanUndo}.";

            if (Services.DemoContent.Modo == "undo")
                UndoCommand.Execute(null);
        }
        _ = GenerateThumbnailsAsync();
    }
#endif

    // ------------------------------------------------------------- elementos

    [RelayCommand]
    private async Task AddImageAsync()
    {
        if (!HasDocument)
            return;

        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Elige la imagen de tu firma",
            FileTypes = FilePickerFileType.Images,
        });

        if (file is null)
            return;

        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        AddImageElement(ms.ToArray());
        Status = "Arrastra la firma a su sitio y ajusta el tamaño con la esquina.";
    }

    [RelayCommand]
    private void AddText()
    {
        if (!HasDocument)
            return;

        var element = new TextElement
        {
            PageIndex = CurrentPageIndex,
            Bounds = new NormalizedRect(0.12, 0.78, 0.45, 0.10),
            Text = "Nombre y apellidos\nCargo\nFirmado el {fecha}",
        };

        Register(new ElementViewModel(element));
        Status = "Toca el bloque para editarlo. {fecha} se sustituye al exportar.";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is null)
            return;

        var elemento = Selected;

        // La instantánea se toma ANTES de quitarlo de la lista: necesita su posición para
        // devolverlo al mismo sitio.
        PushSnapshot(
            elemento,
            elemento.IsText ? "eliminar el bloque de texto" : "eliminar la firma",
            eliminado: true);

        Elements.Remove(elemento);
        VisibleElements.Remove(elemento);
        Selected = null;
    }

    // ------------------------------------------------------------- deshacer

    /// <summary>Tope de la pila de deshacer.</summary>
    /// <remarks>
    /// Un límite evita que una sesión larga acumule instantáneas sin fin. Cincuenta pasos son
    /// muchos más de los que nadie va a retroceder firmando un contrato.
    /// </remarks>
    private const int MaximoHistorial = 50;

    private readonly LinkedList<ElementSnapshot> _historial = new();

    public bool CanUndo => _historial.Count > 0;

    /// <summary>
    /// Guarda el estado de un elemento ANTES de modificarlo.
    /// </summary>
    /// <remarks>
    /// Se llama una sola vez al empezar un gesto, no en cada aviso de movimiento. Un arrastre
    /// genera decenas de avisos, y guardar uno por cada uno haría falta pulsar deshacer
    /// cuarenta veces para retroceder un único movimiento.
    /// </remarks>
    public void PushSnapshot(ElementViewModel element, string descripcion, bool eliminado = false)
    {
        _historial.AddLast(new ElementSnapshot
        {
            Element = element,
            Bounds = element.Element.Bounds,
            WasDeleted = eliminado,
            Index = Elements.IndexOf(element),
            Descripcion = descripcion,
        });

        // Se descarta por el extremo antiguo: lo que se pierde es lo más lejano en el tiempo.
        while (_historial.Count > MaximoHistorial)
            _historial.RemoveFirst();

        NotificarHistorial();
    }

    /// <summary>
    /// Cierra un gesto y descarta su instantánea si no cambió nada.
    /// </summary>
    /// <remarks>
    /// Un clic sobre un elemento inicia y termina un arrastre sin moverlo. Sin esto, cada clic
    /// dejaría un paso de deshacer que aparentemente no hace nada, y el usuario tendría que
    /// pulsar el botón varias veces antes de ver algún efecto.
    /// </remarks>
    public void FinishGesture(ElementViewModel element)
    {
        if (_historial.Last is not { Value: var ultimo } || ultimo.Element != element || ultimo.WasDeleted)
            return;

        if (ultimo.Bounds.Equals(element.Element.Bounds))
        {
            _historial.RemoveLast();
            NotificarHistorial();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_historial.Last is not { Value: var snapshot })
            return;

        _historial.RemoveLast();

        if (snapshot.WasDeleted)
        {
            // Se reinserta donde estaba, no al final: si el usuario tenía varios elementos,
            // que reaparezca en otro orden resulta desconcertante.
            var posicion = Math.Clamp(snapshot.Index, 0, Elements.Count);
            Elements.Insert(posicion, snapshot.Element);
            RefreshVisibleElements();
            Select(snapshot.Element);
        }
        else
        {
            snapshot.Element.Element.Bounds = snapshot.Bounds;
            snapshot.Element.NotifyGeometryChanged();
            Select(snapshot.Element);
        }

        Status = $"Deshecho: {snapshot.Descripcion}.";
        NotificarHistorial();
    }

    private void NotificarHistorial()
    {
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
    }

    public void Select(ElementViewModel? element)
    {
        if (Selected is not null)
            Selected.IsSelected = false;

        Selected = element;

        if (element is not null)
            element.IsSelected = true;
    }

    // ---- Vista plana del elemento seleccionado ----
    //
    // La interfaz no enlaza contra "Selected.Texto" sino contra estas propiedades. El motivo
    // es técnico: Selected es nullable, así que en un enlace compilado "Selected.IsText" tiene
    // tipo bool? y el generador de XAML de MAUI no sabe convertirlo, fallando la compilación
    // en Android con errores dentro de código generado, que son costosos de rastrear.
    // Aplanarlo aquí deja además el XAML más legible.

    /// <summary>Hay un elemento seleccionado. Gobierna el inspector y la barra contextual.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>
    /// Negación de <see cref="HasSelection"/>, para el estado vacío del inspector.
    /// </summary>
    /// <remarks>
    /// Se expone como propiedad en lugar de registrar un convertidor de booleano invertido:
    /// MAUI no trae ninguno de serie y añadir uno solo para esto no compensa.
    /// </remarks>
    public bool HasNoSelection => Selected is null;

    public bool HasNoDocument => !HasDocument;

    /// <summary>Si el inspector debe verse ahora mismo.</summary>
    /// <remarks>
    /// El idioma del dispositivo se resuelve aquí y no con OnIdiom en el XAML porque OnIdiom
    /// produce un VALOR en tiempo de análisis: anidarle un Binding compila, pero el enlace
    /// nunca llega a evaluarse y la propiedad se queda con el objeto Binding. Es un fallo
    /// silencioso, de los que no dan error y solo se notan probando la aplicación.
    ///
    /// En escritorio el inspector está siempre presente, con su estado vacío, para que el
    /// canvas no cambie de ancho al seleccionar. En móvil solo aparece si hay selección.
    /// </remarks>
    public bool InspectorVisible =>
        DeviceInfo.Current.Idiom == DeviceIdiom.Desktop || HasSelection;

    public bool SelectedIsText => Selected?.IsText == true;

    public bool SelectedIsImage => Selected?.IsImage == true;

    public string SelectedKindLabel => Selected switch
    {
        { IsText: true } => "Bloque de texto",
        { IsImage: true } => "Imagen de firma",
        _ => string.Empty,
    };

    public string SelectedPageLabel =>
        Selected is null ? string.Empty : $"En la página {Selected.PageIndex + 1}";

    public string SelectedText
    {
        get => Selected?.Text ?? string.Empty;
        set
        {
            if (Selected is not { IsText: true } s || s.Text == value)
                return;

            s.Text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTextSummary));
            OnPropertyChanged(nameof(SelectedTextDetail));
        }
    }

    public double SelectedFontSizePt
    {
        get => Selected?.FontSizePt ?? 11;
        set
        {
            if (Selected is not { IsText: true } s || Math.Abs(s.FontSizePt - value) < 0.01)
                return;

            s.FontSizePt = value;
            OnPropertyChanged();
        }
    }

    public string SelectedColorHex
    {
        get => Selected?.ColorHex ?? "#000000";
        set
        {
            if (Selected is not { IsText: true } s || s.ColorHex == value)
                return;

            s.ColorHex = value;
            OnPropertyChanged();
            NotificarColor();
        }
    }

    // ---- Edición del texto en dos pasos ----
    //
    // El inspector muestra por defecto un resumen de una línea. El editor multilínea solo
    // aparece al pedirlo. Un Editor siempre desplegado se come la mitad del panel para algo
    // que se toca una vez y luego se deja quieto.

    [ObservableProperty]
    public partial bool IsEditingText { get; set; }

    /// <summary>Primera línea con contenido, recortada, para el resumen.</summary>
    public string SelectedTextSummary
    {
        get
        {
            var texto = SelectedText;
            if (string.IsNullOrWhiteSpace(texto))
                return "(vacío)";

            var primera = texto
                .Replace("\r\n", "\n")
                .Split('\n')
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "(vacío)";

            return primera.Length > 34 ? primera[..33] + "…" : primera;
        }
    }

    /// <summary>Número de líneas, para dar idea del bloque sin desplegarlo.</summary>
    public string SelectedTextDetail
    {
        get
        {
            var lineas = SelectedText.Replace("\r\n", "\n").Split('\n')
                .Count(l => !string.IsNullOrWhiteSpace(l));
            return lineas <= 1 ? "1 línea" : $"{lineas} líneas";
        }
    }

    public bool IsNotEditingText => !IsEditingText;

    partial void OnIsEditingTextChanged(bool value) => OnPropertyChanged(nameof(IsNotEditingText));

    [RelayCommand]
    private void ToggleTextEdit() => IsEditingText = !IsEditingText;

    // ---- Paleta de color ----

    public ObservableCollection<ColorSwatchViewModel> ColorSwatches { get; } =
    [
        new("#000000", "Negro"),
        new("#14145A", "Azul marino"),
        new("#0A84FF", "Azul"),
        new("#D70015", "Rojo"),
        new("#248A3D", "Verde"),
        new("#5E5CE6", "Morado"),
        new("#8B5A2B", "Marrón"),
        new("#636366", "Gris"),
    ];

    [RelayCommand]
    private void PickColor(ColorSwatchViewModel? swatch)
    {
        if (swatch is null)
            return;

        SelectedColorHex = swatch.Hex;
    }

    /// <summary>Panel de color a medida, alternativa a la paleta.</summary>
    /// <remarks>
    /// Son tres deslizadores RGB y no un selector nativo porque MAUI no trae ninguno: no
    /// existe una API multiplataforma de selección de color, y montarla exigiría código
    /// específico en las cuatro plataformas para algo marginal en esta aplicación.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsCustomColorOpen { get; set; }

    [RelayCommand]
    private void ToggleCustomColor() => IsCustomColorOpen = !IsCustomColorOpen;

    public double ColorR
    {
        get => Componente(0);
        set => FijarComponente(0, value);
    }

    public double ColorG
    {
        get => Componente(1);
        set => FijarComponente(1, value);
    }

    public double ColorB
    {
        get => Componente(2);
        set => FijarComponente(2, value);
    }

    public Color SelectedColorPreview =>
        Color.TryParse(SelectedColorHex, out var c) ? c : Colors.Black;

    private double Componente(int indice)
    {
        var hex = SelectedColorHex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var valor))
        {
            return 0;
        }

        return indice switch
        {
            0 => (valor >> 16) & 0xFF,
            1 => (valor >> 8) & 0xFF,
            _ => valor & 0xFF,
        };
    }

    private void FijarComponente(int indice, double valor)
    {
        var v = (int)Math.Clamp(Math.Round(valor), 0, 255);
        var r = indice == 0 ? v : (int)Componente(0);
        var g = indice == 1 ? v : (int)Componente(1);
        var b = indice == 2 ? v : (int)Componente(2);

        SelectedColorHex = $"#{r:X2}{g:X2}{b:X2}";
    }

    private void NotificarColor()
    {
        OnPropertyChanged(nameof(ColorR));
        OnPropertyChanged(nameof(ColorG));
        OnPropertyChanged(nameof(ColorB));
        OnPropertyChanged(nameof(SelectedColorPreview));

        foreach (var swatch in ColorSwatches)
            swatch.IsSelected = string.Equals(swatch.Hex, SelectedColorHex, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedChanged(ElementViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(InspectorVisible));
        OnPropertyChanged(nameof(SelectedIsText));
        OnPropertyChanged(nameof(SelectedIsImage));
        OnPropertyChanged(nameof(SelectedKindLabel));
        OnPropertyChanged(nameof(SelectedPageLabel));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectedTextSummary));
        OnPropertyChanged(nameof(SelectedTextDetail));
        OnPropertyChanged(nameof(SelectedFontSizePt));
        OnPropertyChanged(nameof(SelectedColorHex));

        // Cambiar de elemento cierra el editor y el panel de color a medida: dejarlos
        // abiertos daría la impresión de estar editando lo anterior.
        IsEditingText = false;
        IsCustomColorOpen = false;
        NotificarColor();
    }

    // ------------------------------------------------------------- favoritas

    [RelayCommand]
    private async Task SaveFavoriteAsync()
    {
        var image = Elements.FirstOrDefault(e => e.IsImage && e.PageIndex == CurrentPageIndex);
        var text = Elements.FirstOrDefault(e => e.IsText && e.PageIndex == CurrentPageIndex)?.AsText;

        if (image is null && text is null)
        {
            Status = "No hay nada en esta página que guardar como favorita.";
            return;
        }

        var favorite = new FavoriteSignature
        {
            Text = text?.Text ?? string.Empty,
            FontSizePt = text?.FontSizePt ?? 11,
            ColorHex = text?.ColorHex ?? "#000000",
            Bold = text?.Bold ?? false,
        };

        await _favorites.SaveAsync(favorite, image?.ImageData);
        OnPropertyChanged(nameof(HasFavorite));

        // Guardar la favorita cierra el elemento: la acción termina ahí. Sin esto la barra
        // contextual se quedaba flotando sobre el documento tapando texto, mientras que
        // Eliminar sí la cerraba. Dos botones de la misma barra comportándose distinto es
        // más desconcertante que el estorbo en sí.
        Select(null);

        Status = "Firma favorita guardada en este dispositivo.";
    }

    [RelayCommand]
    private async Task ApplyFavoriteAsync()
    {
        if (!HasDocument)
            return;

        var favorite = _favorites.Load();
        if (favorite is null || favorite.IsEmpty)
        {
            Status = "Todavía no has guardado ninguna firma favorita.";
            return;
        }

        if (favorite.HasImage && await _favorites.LoadImageAsync() is { Length: > 0 } data)
            AddImageElement(data);

        if (favorite.HasText)
        {
            Register(new ElementViewModel(new TextElement
            {
                PageIndex = CurrentPageIndex,
                Bounds = new NormalizedRect(0.12, 0.78, 0.45, 0.10),
                Text = favorite.Text,
                FontSizePt = favorite.FontSizePt,
                ColorHex = favorite.ColorHex,
                Bold = favorite.Bold,
            }));
        }

        Status = "Firma favorita colocada.";
    }

    // -------------------------------------------------------------- exportar

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_pdfBytes is null || Elements.Count == 0)
        {
            Status = "Añade al menos una firma antes de exportar.";
            return;
        }

        await RunBusyAsync("Generando el PDF firmado...", async () =>
        {
            using var input = new MemoryStream(_pdfBytes);
            using var output = new MemoryStream();

            // El trabajo pesado va a un hilo de fondo: en un PDF grande bloquearía la interfaz.
            await Task.Run(() =>
                _signer.Sign(input, output, Elements.Select(e => e.Element)));

            var name = $"{Path.GetFileNameWithoutExtension(_sourceFileName)}-firmado.pdf";
            var delivered = await _exporter.ExportAsync(name, output.ToArray());

            Status = delivered ? $"Listo: {name}" : "Exportación cancelada.";
        });
    }

    // --------------------------------------------------------------- páginas

    [RelayCommand]
    private async Task GoToPageAsync(PageViewModel page)
    {
        if (page.Index == CurrentPageIndex)
            return;

        CurrentPageIndex = page.Index;
        await ShowPageAsync(page.Index);
    }

    private async Task ShowPageAsync(int index)
    {
        if (_pdfBytes is null)
            return;

        using var stream = new MemoryStream(_pdfBytes);
        var rendered = await _rasterizer.RenderPageAsync(stream, index, MainRenderWidth);

        CurrentPageImage = ImageSource.FromStream(() => new MemoryStream(rendered.PngData));
        CanvasWidth = rendered.PixelWidth;
        CanvasHeight = rendered.PixelHeight;

        // La página se rasteriza a 1200 px de ancho, bastante más que la ventana típica.
        // Sin ajustar, el usuario abre su documento y ve un trozo de la esquina superior.
        ZoomToFit();

        foreach (var page in Pages)
            page.IsCurrent = page.Index == index;

        RefreshVisibleElements();
    }

    private async Task GenerateThumbnailsAsync()
    {
        if (_pdfBytes is null)
            return;

        foreach (var page in Pages.ToList())
        {
            try
            {
                using var stream = new MemoryStream(_pdfBytes);
                var rendered = await _rasterizer.RenderPageAsync(stream, page.Index, ThumbnailWidth);
                page.Thumbnail = ImageSource.FromStream(() => new MemoryStream(rendered.PngData));
            }
            catch (Exception)
            {
                // Una miniatura que falla no debe impedir trabajar con el documento; la página
                // sigue siendo accesible por su número.
            }
        }
    }

    // ----------------------------------------------------------------- apoyo

    private void AddImageElement(byte[] data)
    {
        var element = new ImageElement
        {
            PageIndex = CurrentPageIndex,
            Bounds = new NormalizedRect(0.12, 0.62, 0.30, 0.10),
            Data = data,
        };

        Register(new ElementViewModel(element, data));
    }

    private void Register(ElementViewModel vm)
    {
        vm.SetCanvasSize(DisplayWidth, DisplayHeight);
        Elements.Add(vm);
        RefreshVisibleElements();
        Select(vm);
    }

    private void RefreshVisibleElements()
    {
        VisibleElements.Clear();

        foreach (var element in Elements.Where(e => e.PageIndex == CurrentPageIndex))
        {
            element.SetCanvasSize(DisplayWidth, DisplayHeight);
            VisibleElements.Add(element);
        }
    }

    // ---- Tamaño con el que se DIBUJA la página ----
    //
    // La página no se amplía con un Scale sino cambiando su tamaño real. Scale no afecta al
    // layout: el hueco seguiría midiendo el original, el ScrollView calcularía mal su
    // recorrido y la página se dibujaría estirada sobre un contenedor que no le corresponde.
    //
    // Con tamaño real hay además un efecto secundario valioso: los bordes de selección y las
    // asas de redimensionado NO se escalan, así que conservan su tamaño en pantalla a
    // cualquier ampliación. Con Scale habría que compensarlos uno a uno.

    public double DisplayWidth => CanvasWidth * Zoom;

    public double DisplayHeight => CanvasHeight * Zoom;

    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>Informa del tamaño visible del área de la página.</summary>
    public void SetViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>Ajusta la ampliación para que la página entre entera en pantalla.</summary>
    /// <remarks>
    /// Sin esto, un A4 rasterizado a 1200 px se abre más grande que la ventana y el usuario
    /// ve la esquina superior izquierda de su documento sin entender por qué.
    /// </remarks>
    public void ZoomToFit()
    {
        if (CanvasWidth <= 0 || CanvasHeight <= 0 || _viewportWidth <= 0 || _viewportHeight <= 0)
            return;

        const double margen = 48;
        var ajuste = Math.Min(
            (_viewportWidth - margen) / CanvasWidth,
            (_viewportHeight - margen) / CanvasHeight);

        Zoom = Math.Clamp(ajuste, 0.1, 2);
    }

    partial void OnCanvasWidthChanged(double value) => PropagateCanvasSize();

    partial void OnCanvasHeightChanged(double value) => PropagateCanvasSize();

    partial void OnZoomChanged(double value) => PropagateCanvasSize();

    private void PropagateCanvasSize()
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));

        // Los elementos trabajan en píxeles de pantalla, así que su lienzo de referencia es
        // el tamaño ya ampliado. Gracias a eso los gestos no necesitan dividir por el zoom.
        foreach (var element in Elements)
            element.SetCanvasSize(DisplayWidth, DisplayHeight);
    }

    private async Task RunBusyAsync(string message, Func<Task> work)
    {
        IsBusy = true;
        Status = message;

        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cada plataforma identifica los PDF de forma distinta, así que hay que enumerarlas todas.
    /// </summary>
    private static FilePickerFileType PdfFileType => new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".pdf"],
            [DevicePlatform.macOS] = ["pdf"],
            [DevicePlatform.MacCatalyst] = ["pdf"],
            [DevicePlatform.iOS] = ["com.adobe.pdf"],
            [DevicePlatform.Android] = ["application/pdf"],
        });
}
