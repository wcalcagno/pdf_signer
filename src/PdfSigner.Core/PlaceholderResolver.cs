using System.Globalization;

namespace PdfSigner.Core;

/// <summary>Sustituye marcadores del tipo <c>{fecha}</c> en el momento de exportar.</summary>
/// <remarks>
/// La sustitución ocurre al exportar y no al escribir el texto, para que una "firma favorita"
/// guardada hace meses siga estampando la fecha del día en que realmente se firma.
/// </remarks>
public static class PlaceholderResolver
{
    /// <param name="now">Inyectable para que los tests no dependan del reloj del sistema.</param>
    public static string Resolve(string text, DateTime now)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var es = new CultureInfo("es-CL");

        // Las barras y los dos puntos van escapados a proposito: sin escapar, .NET los
        // sustituye por el separador de la cultura activa y en es-CL la fecha saldria
        // "13-09-2026". El formato debe ser identico en cualquier equipo.
        return text
            .Replace("{fecha}", now.ToString("dd'/'MM'/'yyyy", es), StringComparison.OrdinalIgnoreCase)
            .Replace("{hora}", now.ToString("HH':'mm", es), StringComparison.OrdinalIgnoreCase)
            .Replace("{fechalarga}", now.ToString("d 'de' MMMM 'de' yyyy", es), StringComparison.OrdinalIgnoreCase);
    }
}
