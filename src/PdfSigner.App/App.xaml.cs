using Microsoft.Extensions.DependencyInjection;

namespace PdfSigner.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    // Se prescinde de Shell a propósito: la aplicación tiene una sola pantalla, y Shell obliga
    // a construir la página por plantilla, lo que complica pasarle el ViewModel por inyección.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<MainPage>());

        // En escritorio MAUI abre una ventana desmesurada, que en pantallas de portátil se
        // sale de los bordes y deja el inspector y el navegador de páginas fuera de vista.
        // Se fija un tamaño de partida razonable y un mínimo por debajo del cual la
        // disposición de cuatro regiones deja de tener sentido.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            window.Width = 1280;
            window.Height = 860;
            window.MinimumWidth = 900;
            window.MinimumHeight = 600;
        }

        return window;
    }
}
