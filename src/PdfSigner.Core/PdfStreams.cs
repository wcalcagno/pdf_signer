using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfSigner.Core;

/// <summary>Utilidades compartidas para abrir documentos PDF.</summary>
internal static class PdfStreams
{
    /// <summary>PdfReader necesita poder hacer seek; los flujos de red o de picker a veces no.</summary>
    public static Stream AsSeekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Abre el documento traduciendo los fallos a mensajes que el usuario entienda.</summary>
    /// <remarks>
    /// Compartido por el servicio de firma y el de formularios: los dos abren documentos que
    /// el usuario acaba de elegir, y los dos deben decirle algo comprensible si no se puede.
    /// </remarks>
    public static PdfDocument Open(Stream pdf, PdfDocumentOpenMode modo)
    {
        try
        {
            return PdfReader.Open(AsSeekable(pdf), modo);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfSignerException(
                "El PDF está protegido con contraseña. Quítale la protección antes de firmarlo.", ex);
        }
        catch (Exception ex) when (ex is not PdfSignerException)
        {
            // Se agrupa todo lo demás a propósito: PDFsharp lanza desde ArgumentOutOfRangeException
            // hasta InvalidOperationException según por dónde se rompa el archivo, y para quien
            // firma un contrato la causa concreta es irrelevante.
            throw new PdfSignerException(
                "No se pudo leer el archivo: no parece un PDF válido o está dañado.", ex);
        }
    }
}
