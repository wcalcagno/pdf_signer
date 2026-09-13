using Microsoft.Extensions.Logging;
using PdfSigner.App.Services;
using PdfSigner.App.ViewModels;
using PdfSigner.Core;

namespace PdfSigner.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // El rasterizador es lo único que cambia entre plataformas: cada sistema aporta su
        // propio motor PDF, así que no hay binarios nativos de terceros en el paquete.
#if WINDOWS
        builder.Services.AddSingleton<IPdfRasterizer, Platforms.Windows.WindowsPdfRasterizer>();
        builder.Services.AddSingleton<IFileExporter, Platforms.Windows.WindowsFileExporter>();
#elif ANDROID
        builder.Services.AddSingleton<IPdfRasterizer, Platforms.Android.AndroidPdfRasterizer>();
        builder.Services.AddSingleton<IFileExporter, ShareFileExporter>();
#elif IOS || MACCATALYST
        builder.Services.AddSingleton<IPdfRasterizer, Platforms.Apple.ApplePdfRasterizer>();
        builder.Services.AddSingleton<IFileExporter, ShareFileExporter>();
#endif

        builder.Services.AddSingleton<FavoriteSignatureStore>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
