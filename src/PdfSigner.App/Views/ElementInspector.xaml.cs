using PdfSigner.App.ViewModels;

namespace PdfSigner.App.Views;

/// <summary>
/// Panel de propiedades del elemento seleccionado.
/// </summary>
/// <remarks>
/// Casi sin código: la lógica vive en MainViewModel, de quien hereda el BindingContext. Lo
/// único aquí es el gesto de las muestras de color, porque en MAUI los reconocedores de
/// gestos no se enlazan a comandos sin un behavior, y añadir uno para esto no compensa.
/// </remarks>
public partial class ElementInspector : ContentView
{
    public ElementInspector() => InitializeComponent();

    private void OnSwatchTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: ColorSwatchViewModel swatch }
            && BindingContext is MainViewModel vm)
        {
            vm.PickColorCommand.Execute(swatch);
        }
    }
}
