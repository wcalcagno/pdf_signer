using PdfSigner.App.ViewModels;
using PdfSigner.Core;

namespace PdfSigner.App.Views;

/// <summary>Elige la plantilla de cada zona del formulario según su tipo.</summary>
/// <remarks>
/// La alternativa sería una sola plantilla con los cuatro controles superpuestos y visibles
/// por turnos, como se hace con imagen y texto en la plantilla de elementos. Allí compensa
/// porque son dos controles y nunca hay muchos; aquí serían cuatro por campo, y un formulario
/// de cincuenta campos crearía doscientos controles de los que solo se ven cincuenta.
/// </remarks>
public sealed class FormFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Texto { get; set; }

    public DataTemplate? Multilinea { get; set; }

    public DataTemplate? Boton { get; set; }

    public DataTemplate? Eleccion { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not FormFieldViewModel campo)
            return Texto;

        return campo.Kind switch
        {
            PdfFormFieldKind.Text => campo.IsMultiLine ? Multilinea : Texto,
            PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioGroup => Boton,
            PdfFormFieldKind.ComboBox or PdfFormFieldKind.ListBox => Eleccion,
            _ => Texto,
        };
    }
}
