namespace PdfSigner.Core;

/// <summary>Error que la aplicación puede mostrar tal cual al usuario.</summary>
/// <remarks>
/// Las excepciones de PDFsharp llegan en inglés y algunas son incomprensibles fuera de su
/// contexto: un PDF dañado produce "value ('-16') must be a non-negative value", que no le
/// dice nada a quien solo quiere firmar un contrato. La interfaz vuelca el mensaje en la barra
/// de estado, así que el texto de estas excepciones es literalmente parte de la interfaz.
///
/// La excepción original se conserva en InnerException para poder diagnosticar.
/// </remarks>
public sealed class PdfSignerException : Exception
{
    public PdfSignerException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
