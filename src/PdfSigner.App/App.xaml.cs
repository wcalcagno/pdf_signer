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
        => new(_services.GetRequiredService<MainPage>());
}
