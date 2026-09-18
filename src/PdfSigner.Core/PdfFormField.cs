namespace PdfSigner.Core;

/// <summary>Tipo de campo de formulario.</summary>
public enum PdfFormFieldKind
{
    /// <summary>Texto libre, de una o varias líneas.</summary>
    Text,

    /// <summary>Casilla que se marca o se desmarca.</summary>
    CheckBox,

    /// <summary>Grupo de opciones excluyentes.</summary>
    RadioGroup,

    /// <summary>Desplegable de opción única.</summary>
    ComboBox,

    /// <summary>Lista de opciones visibles a la vez.</summary>
    ListBox,

    /// <summary>Campo de firma criptográfica. Fuera del alcance de esta aplicación.</summary>
    Signature,

    /// <summary>Tipo no reconocido. Se muestra pero no se deja editar.</summary>
    Unsupported,
}

/// <summary>
/// Una zona dibujable de un campo, con su posición en la página.
/// </summary>
/// <remarks>
/// La mayoría de campos tienen una sola, pero un grupo de radio tiene una por opción, cada
/// una en su sitio. Por eso el campo guarda una lista y no un rectángulo suelto.
/// </remarks>
/// <param name="PageIndex">Página donde aparece, base 0.</param>
/// <param name="Bounds">Posición en coordenadas normalizadas del espacio visible.</param>
/// <param name="OptionName">
/// Para un grupo de radio, el valor que representa esta opción concreta. Null en el resto.
/// </param>
/// <param name="HeightPt">
/// Alto de la zona en puntos PDF. Permite a la interfaz saber cuántos píxeles de pantalla
/// vale un punto sin tener que conocer el tamaño de la página.
/// </param>
public sealed record PdfFormWidget(
    int PageIndex, NormalizedRect Bounds, string? OptionName = null, double HeightPt = 0);

/// <summary>Un campo de formulario leído de un PDF.</summary>
/// <remarks>
/// Es una vista propia y no un envoltorio de las clases de PDFsharp. La razón es práctica:
/// así la interfaz puede trabajar con el campo, guardar lo que el usuario escribe y
/// devolverlo para rellenar, sin mantener abierto el documento entero mientras tanto.
/// </remarks>
public sealed class PdfFormField
{
    /// <summary>Nombre interno del campo, que es como se identifica al rellenarlo.</summary>
    public required string Name { get; init; }

    public required PdfFormFieldKind Kind { get; init; }

    /// <summary>Zonas donde se dibuja. Una para casi todos; varias para un grupo de radio.</summary>
    public required IReadOnlyList<PdfFormWidget> Widgets { get; init; }

    /// <summary>
    /// Valor actual. Para texto es el contenido; para opciones, la elegida; para una casilla,
    /// el nombre del estado marcado o null.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>Opciones disponibles en desplegables, listas y grupos de radio.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>El PDF declara el campo como no modificable.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Longitud máxima en campos de texto, o 0 si no hay límite.</summary>
    public int MaxLength { get; init; }

    /// <summary>
    /// Tamaño de letra en puntos con el que se escribirá el valor.
    /// </summary>
    /// <remarks>
    /// Ya resuelto: si el campo pedía tamaño automático, aquí viene el que se va a usar de
    /// verdad. Así la vista previa puede dibujar el texto exactamente como va a quedar.
    /// </remarks>
    public double FontSizePt { get; init; }

    /// <summary>El campo de texto admite varias líneas.</summary>
    public bool MultiLine { get; init; }

    /// <summary>Se puede editar desde la aplicación.</summary>
    /// <remarks>
    /// Los campos de firma criptográfica quedan fuera: esta aplicación hace firma visual, y
    /// rellenar un campo de firma PAdES daría una falsa sensación de validez legal.
    /// </remarks>
    public bool IsEditable =>
        !ReadOnly && Kind is not (PdfFormFieldKind.Signature or PdfFormFieldKind.Unsupported);

    /// <summary>Primera página donde aparece, para poder agrupar y navegar.</summary>
    public int PageIndex => Widgets.Count > 0 ? Widgets[0].PageIndex : 0;

    public override string ToString() => $"{Name} ({Kind})";
}
