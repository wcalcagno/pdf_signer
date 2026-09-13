using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;

namespace PdfSigner.Core.Tests;

/// <summary>Genera los PDFs e imágenes de prueba en memoria, sin archivos en el repo.</summary>
internal static class TestAssets
{
    public const double A4WidthPt = 595;
    public const double A4HeightPt = 842;

    /// <param name="rotations">Rotación /Rotate de cada página a crear.</param>
    public static MemoryStream CreatePdf(params int[] rotations)
    {
        using var doc = new PdfDocument();
        foreach (var rotation in rotations.Length == 0 ? [0] : rotations)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            page.Rotate = rotation;
        }

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    /// <summary>PNG RGBA mínimo con canal alfa, para comprobar que la transparencia sobrevive.</summary>
    public static byte[] CreatePng(int width = 120, int height = 60)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filtro None
            for (var x = 0; x < width; x++)
            {
                var ink = Math.Abs(y - height / 2) < 4;
                raw.WriteByte(0);
                raw.WriteByte(0);
                raw.WriteByte(0);
                raw.WriteByte((byte)(ink ? 255 : 0)); // alfa
            }
        }

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var ihdr = new MemoryStream();
        ihdr.Write(BigEndian(width));
        ihdr.Write(BigEndian(height));
        ihdr.Write([8, 6, 0, 0, 0]); // 8 bits por canal, RGBA, sin entrelazado
        WriteChunk(png, "IHDR", ihdr.ToArray());
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        s.Write(BigEndian(data.Length));
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(body);
        s.Write(BigEndian(unchecked((int)Crc32(body))));
    }

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
