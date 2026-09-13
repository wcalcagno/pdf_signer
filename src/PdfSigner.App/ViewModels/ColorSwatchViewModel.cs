using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfSigner.App.ViewModels;

/// <summary>Una muestra de color de la paleta del inspector.</summary>
/// <remarks>
/// La paleta sustituye al campo hexadecimal que había antes. Pedirle "#14145A" a alguien que
/// solo quiere firmar un contrato es pedirle que hable en un formato que no tiene por qué
/// conocer; elegir entre ocho cuadraditos no requiere explicación.
/// </remarks>
public sealed partial class ColorSwatchViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ColorSwatchViewModel(string hex, string nombre)
    {
        Hex = hex;
        Nombre = nombre;
        Color = Color.TryParse(hex, out var color) ? color : Colors.Black;
    }

    public string Hex { get; }

    /// <summary>Nombre legible, para lectores de pantalla.</summary>
    public string Nombre { get; }

    public Color Color { get; }

    /// <summary>Grosor del borde: así se distingue la muestra activa sin convertidores.</summary>
    public double BorderThickness => IsSelected ? 3 : 1;

    public Color BorderColor => IsSelected ? Colors.White : Color.FromArgb("#00000033");

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderThickness));
        OnPropertyChanged(nameof(BorderColor));
    }
}
