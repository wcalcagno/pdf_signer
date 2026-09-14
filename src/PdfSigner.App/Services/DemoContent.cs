#if DEBUG
using System.IO.Compression;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSigner.Core;

namespace PdfSigner.App.Services;

/// <summary>
/// Genera un documento y una firma de ejemplo para poder arrancar la aplicación con contenido
/// ya cargado.
/// </summary>
/// <remarks>
/// Solo existe en compilaciones de depuración y solo se activa con la variable de entorno
/// PDFSIGNER_DEMO. Sirve para revisar estados de la interfaz que de otro modo exigen abrir un
/// archivo a mano: elemento seleccionado, inspector con datos, asas de redimensionado.
///
/// No es una funcionalidad de la aplicación, es andamio de desarrollo.
/// </remarks>
internal static class DemoContent
{
    public const string VariableEntorno = "PDFSIGNER_DEMO";

    public static bool Activo =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VariableEntorno));

    /// <summary>
    /// Con PDFSIGNER_DEMO=drag el elemento arranca marcado como si se estuviera arrastrando.
    /// </summary>
    /// <remarks>
    /// Existe porque una captura de pantalla es estática y no puede simular un gesto: sin
    /// esto no habría forma de comprobar con la vista que el aviso de arrastre se dibuja.
    /// </remarks>
    public static bool SimularArrastre =>
        string.Equals(Environment.GetEnvironmentVariable(VariableEntorno), "drag",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Documento de dos páginas con aspecto de contrato.</summary>
    public static byte[] CrearPdf()
    {
        EmbeddedFontResolver.Register();

        using var doc = new PdfDocument();
        foreach (var titulo in new[] { "CONTRATO DE PRESTACION DE SERVICIOS", "ANEXO I - CONDICIONES" })
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;

            using var g = XGraphics.FromPdfPage(page);
            g.DrawString(titulo, new XFont("Open Sans", 14, XFontStyleEx.Bold),
                XBrushes.DimGray, new XPoint(60, 70));

            for (var i = 0; i < 20; i++)
                g.DrawRectangle(XBrushes.WhiteSmoke, 60, 100 + i * 22, 475, 12);
        }

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    /// <summary>PNG con canal alfa que simula una firma manuscrita.</summary>
    public static byte[] CrearFirma(int w = 360, int h = 130)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < w; x++)
            {
                var t = x / (double)w;
                var curva = h / 2 + Math.Sin(t * 9) * h * 0.3 - t * h * 0.15;
                var tinta = Math.Abs(y - curva) < 2.5 + Math.Sin(t * 20) * 1.2;
                raw.WriteByte(20);
                raw.WriteByte(20);
                raw.WriteByte(90);
                raw.WriteByte((byte)(tinta ? 255 : 0));
            }
        }

        var comprimido = new MemoryStream();
        using (var z = new ZLibStream(comprimido, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var ihdr = new MemoryStream();
        ihdr.Write(BigEndian(w));
        ihdr.Write(BigEndian(h));
        ihdr.Write([8, 6, 0, 0, 0]);
        Bloque(png, "IHDR", ihdr.ToArray());
        Bloque(png, "IDAT", comprimido.ToArray());
        Bloque(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static void Bloque(Stream s, string tipo, byte[] datos)
    {
        s.Write(BigEndian(datos.Length));
        var cuerpo = Encoding.ASCII.GetBytes(tipo).Concat(datos).ToArray();
        s.Write(cuerpo);
        s.Write(BigEndian(unchecked((int)Crc32(cuerpo))));
    }

    private static uint Crc32(byte[] d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d)
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
#endif
