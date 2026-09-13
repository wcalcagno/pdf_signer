using PdfSigner.App.ViewModels;

namespace PdfSigner.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    // Los gestos de arrastre informan del desplazamiento ACUMULADO desde que empezó el gesto,
    // no del incremento desde el último aviso. Guardamos el último valor para trabajar con
    // incrementos, que es lo que necesita el modelo.
    private double _lastPanX;
    private double _lastPanY;

    private double _zoomAtPinchStart = 1;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        if (DeviceInfo.Current.Idiom != DeviceIdiom.Desktop)
            AplicarDisposicionMovil();
    }

#if DEBUG
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Andamio de desarrollo: con la variable PDFSIGNER_DEMO la aplicación arranca con un
        // documento y una firma ya puestos, para poder revisar los estados que dependen de
        // tener algo seleccionado sin abrir un archivo a mano cada vez.
        if (!Services.DemoContent.Activo)
            return;

        try
        {
            await _vm.LoadDemoAsync();
        }
        catch (Exception ex)
        {
            // El detalle completo va a un archivo: una excepción de WinRT llega con un
            // mensaje inútil y su causa real solo aparece en la traza.
            var destino = Path.Combine(Path.GetTempPath(), "pdfsigner-demo-error.txt");
            File.WriteAllText(destino, ex.ToString());
            _vm.Status = $"[demo falló: {ex.GetType().Name} · detalle en {destino}]";
        }
    }
#endif

    /// <summary>
    /// Reorganiza las cuatro regiones para pantallas de móvil: el inspector deja de ser una
    /// columna lateral y pasa a ser un panel inferior.
    /// </summary>
    /// <remarks>
    /// Se hace en código y no con OnIdiom en el XAML por una razón práctica: OnIdiom sobre
    /// propiedades que requieren conversor de tipo, como RowDefinitions, es terreno poco
    /// fiable, y en esta máquina no hay forma de comprobarlo porque solo se puede ejecutar
    /// Windows. Un método explícito se puede revisar leyéndolo, sin depender de un emulador.
    ///
    /// No es lógica de negocio sino colocación de vistas, así que la vista es su sitio.
    ///
    /// PENDIENTE: nadie ha ejecutado esto todavía en un teléfono. Está escrito contra la
    /// API documentada, igual que el resto del código específico de móvil.
    /// </remarks>
    private void AplicarDisposicionMovil()
    {
        // Una sola columna: el canvas ocupa todo el ancho.
        RaizGrid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];

        // Cuatro filas: barra, canvas, inspector y navegador.
        RaizGrid.RowDefinitions =
        [
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Star),
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto),
        ];

        Grid.SetColumnSpan(BarraSuperior, 1);

        // El inspector baja a su propia fila, a lo ancho, con altura fija: la fase 4 anima
        // su aparición con TranslationY, que sí es interpolable (una fila Auto no lo es).
        Grid.SetRow(Inspector, 2);
        Grid.SetColumn(Inspector, 0);
        // La medida se lee del diccionario de estilos para no tenerla escrita en dos sitios.
        if (Application.Current?.Resources.TryGetValue("AltoBottomSheet", out var alto) == true
            && alto is double altoSheet)
        {
            _altoSheet = altoSheet;
        }

        Inspector.HeightRequest = _altoSheet;

        Grid.SetRow(RegionNavegador, 3);
        Grid.SetColumnSpan(RegionNavegador, 1);

        // Parte oculto, fuera de pantalla, y entra deslizándose al seleccionar algo.
        Inspector.TranslationY = _altoSheet;
        _vm.PropertyChanged += AnimarPanelInferior;
    }

    private double _altoSheet = 200;

    /// <summary>Desliza el panel inferior al aparecer y desaparecer la selección.</summary>
    /// <remarks>
    /// Se anima TranslationY y no la altura de la fila: GridLength.Auto no es un valor
    /// numérico, así que no se puede interpolar entre 0 y Auto. Desplazar el panel logra el
    /// mismo efecto visual con una propiedad que sí admite animación.
    ///
    /// PENDIENTE: sin ejecutar en un teléfono, como todo lo específico de móvil.
    /// </remarks>
    private async void AnimarPanelInferior(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.HasSelection))
            return;

        var destino = _vm.HasSelection ? 0 : _altoSheet;
        await Inspector.TranslateTo(0, destino, 180, Easing.CubicOut);
    }

    /// <summary>
    /// Comunica al ViewModel el tamaño visible de la página para poder ajustar la ampliación.
    /// </summary>
    /// <remarks>
    /// Hace falta un evento porque el ViewModel no puede conocer por su cuenta el tamaño de un
    /// control, y ese dato es imprescindible para calcular el zoom que hace entrar la página
    /// entera en pantalla.
    /// </remarks>
    private void OnCanvasResized(object? sender, EventArgs e)
    {
        if (RegionCanvas.Width <= 0 || RegionCanvas.Height <= 0)
            return;

        var primeraVez = !_viewportConocido;
        _viewportConocido = true;
        _vm.SetViewport(RegionCanvas.Width, RegionCanvas.Height);

        // Solo se reajusta la primera vez. Después el zoom es del usuario, y recolocárselo
        // cada vez que redimensiona la ventana sería exasperante.
        if (primeraVez)
            _vm.ZoomToFit();
    }

    private bool _viewportConocido;

    private void OnElementTapped(object? sender, TappedEventArgs e)
    {
        if (Resolve(sender) is { } vm)
            _vm.Select(vm);
    }

    private void OnElementPan(object? sender, PanUpdatedEventArgs e)
    {
        if (Resolve(sender) is not { } vm)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanX = 0;
                _lastPanY = 0;
                _vm.Select(vm);
                break;

            case GestureStatus.Running:
                var dx = e.TotalX - _lastPanX;
                var dy = e.TotalY - _lastPanY;
                _lastPanX = e.TotalX;
                _lastPanY = e.TotalY;

                // No hace falta dividir por el zoom: desde que la ampliación cambia el tamaño
                // real de la página, el lienzo de referencia de los elementos ya está en
                // píxeles de pantalla.
                vm.Move(dx, dy);
                break;
        }
    }

    private void OnHandlePan(object? sender, PanUpdatedEventArgs e)
    {
        if (Resolve(sender) is not { } vm)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanX = 0;
                _lastPanY = 0;
                break;

            case GestureStatus.Running:
                var dx = e.TotalX - _lastPanX;
                var dy = e.TotalY - _lastPanY;
                _lastPanX = e.TotalX;
                _lastPanY = e.TotalY;
                vm.Resize(dx, dy);
                break;
        }
    }

    private void OnPagePinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _zoomAtPinchStart = _vm.Zoom;
                break;

            case GestureStatus.Running:
                // Se limita el rango para que no se pueda dejar la página en un tamaño
                // desde el que sea imposible volver.
                _vm.Zoom = Math.Clamp(_zoomAtPinchStart * e.Scale, 0.2, 5);
                break;
        }
    }

    private async void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: PageViewModel page })
            await _vm.GoToPageCommand.ExecuteAsync(page);
    }

    /// <summary>
    /// Obtiene el elemento al que pertenece el gesto. Dentro de una plantilla, el
    /// BindingContext del control es siempre su propio ViewModel.
    /// </summary>
    private static ElementViewModel? Resolve(object? sender) =>
        (sender as BindableObject)?.BindingContext as ElementViewModel;
}
