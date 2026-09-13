namespace PdfSigner.App.Views;

/// <summary>
/// Panel de propiedades del elemento seleccionado.
/// </summary>
/// <remarks>
/// Sin código: toda la lógica vive en MainViewModel, de quien hereda el BindingContext.
/// La vista solo se coloca en un sitio distinto según la plataforma, y de eso se encarga
/// MainPage.
/// </remarks>
public partial class ElementInspector : ContentView
{
    public ElementInspector() => InitializeComponent();
}
