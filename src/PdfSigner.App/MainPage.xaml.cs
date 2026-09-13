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
    }

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

                // El desplazamiento llega en unidades de pantalla; si la página está ampliada
                // hay que dividir por el zoom o el elemento se movería más rápido que el dedo.
                vm.Move(dx / _vm.Zoom, dy / _vm.Zoom);
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
                vm.Resize(dx / _vm.Zoom, dy / _vm.Zoom);
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
