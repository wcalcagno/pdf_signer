using PdfSigner.Core;

namespace PdfSigner.App.ViewModels;

/// <summary>Estado de un elemento antes de una operación, para poder volver a él.</summary>
/// <remarks>
/// Deliberadamente mínimo: posición, tamaño y si el elemento fue eliminado. No es un sistema
/// de comandos con rehacer ni transacciones anidadas. Para una herramienta que se usa un par
/// de veces al mes, lo que hace falta es poder retroceder de un movimiento accidental, y eso
/// se resuelve con una pila y cuatro números.
///
/// Guarda una referencia al ViewModel, no una copia: al deshacer hay que devolver a su sitio
/// el MISMO elemento que el usuario está viendo.
/// </remarks>
public sealed record ElementSnapshot
{
    public required ElementViewModel Element { get; init; }

    /// <summary>Posición y tamaño antes de la operación.</summary>
    public required NormalizedRect Bounds { get; init; }

    /// <summary>La operación fue una eliminación; al deshacer hay que reinsertar.</summary>
    public bool WasDeleted { get; init; }

    /// <summary>Posición que ocupaba en la lista, para reinsertarlo donde estaba.</summary>
    public int Index { get; init; }

    /// <summary>Qué se deshace, en palabras, para poder decírselo al usuario.</summary>
    public required string Descripcion { get; init; }
}
