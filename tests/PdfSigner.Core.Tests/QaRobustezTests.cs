using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Pruebas de robustez: documentos malformados, imágenes atípicas, texto real en español
/// y uso concurrente.
/// </summary>
/// <remarks>
/// La suite de PdfSignatureServiceTests comprueba que el camino feliz produce el PDF correcto.
/// Esta comprueba lo contrario: que lo inesperado falle de forma limpia y comprensible en vez
/// de generar un PDF silenciosamente roto, que es el peor desenlace posible aquí, porque el
/// usuario no se entera hasta que abre el archivo delante de quien se lo pidió.
/// </remarks>
public class QaRobustezTests
{
    private static readonly DateTime Momento = new(2026, 9, 13, 14, 30, 0);
    private readonly PdfSignatureService _service = new();

    private MemoryStream Firmar(MemoryStream origen, params PlacedElement[] elementos)
    {
        var destino = new MemoryStream();
        _service.Sign(origen, destino, elementos, Momento, compress: false);
        return destino;
    }

    private static TextElement Texto(string contenido, double tamano = 11) => new()
    {
        PageIndex = 0,
        Bounds = new NormalizedRect(0.1, 0.5, 0.6, 0.2),
        Text = contenido,
        FontSizePt = tamano,
    };

    private static ImageElement Imagen(byte[] datos) => new()
    {
        PageIndex = 0,
        Bounds = new NormalizedRect(0.1, 0.5, 0.3, 0.1),
        Data = datos,
    };

    // ============================================================ A. Documentos

    [Fact]
    public void A01_Pdf_corrupto_falla_con_excepcion()
    {
        var basura = new MemoryStream("%PDF-1.4 esto no es un PDF de verdad"u8.ToArray());
        var destino = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            _service.Sign(basura, destino, [Texto("x")], Momento));
    }

    [Fact]
    public void A02_Archivo_vacio_falla_con_excepcion()
    {
        var vacio = new MemoryStream([]);
        var destino = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            _service.Sign(vacio, destino, [Texto("x")], Momento));
    }

    [Fact]
    public void A03_Archivo_que_no_es_pdf_falla_con_excepcion()
    {
        var texto = new MemoryStream("Hola, soy un archivo de texto plano."u8.ToArray());
        var destino = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            _service.Sign(texto, destino, [Texto("x")], Momento));
    }

    [Fact]
    public void A04_Pagina_diminuta_se_firma_sin_reventar()
    {
        using var origen = CreatePdfConTamano(147, 210); // A8
        using var firmado = Firmar(origen, Texto("Firma", tamano: 4));

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    [Fact]
    public void A05_Pagina_enorme_se_firma_sin_reventar()
    {
        using var origen = CreatePdfConTamano(2384, 3370); // A0
        using var firmado = Firmar(origen, Texto("Firma"));

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    [Fact]
    public void A06_Documento_de_cincuenta_paginas_se_firma_entero()
    {
        var rotaciones = Enumerable.Repeat(0, 50).ToArray();
        using var origen = TestAssets.CreatePdf(rotaciones);

        var firma = TestAssets.CreatePng();
        var elementos = Enumerable.Range(0, 50)
            .Select(i => (PlacedElement)new ImageElement
            {
                PageIndex = i,
                Bounds = new NormalizedRect(0.1, 0.8, 0.2, 0.08),
                Data = firma,
            })
            .ToArray();

        using var firmado = Firmar(origen, elementos);
        var pdf = new PdfContentInspector(firmado);

        Assert.Equal(50, pdf.DrawnXObjects);
        Assert.Equal(1, pdf.EmbeddedImages); // la caché debe evitar 50 copias
    }

    [Fact]
    public void A07_MediaBox_desplazado_coloca_la_firma_dentro_de_la_pagina()
    {
        // Hay PDFs, sobre todo de imprenta, cuyo MediaBox no empieza en (0,0).
        using var origen = CreatePdfConMediaBoxDesplazado(20, 30, 595, 842);

        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0, 0, 0.2, 0.1), // esquina superior izquierda
            Data = TestAssets.CreatePng(),
        });

        // Hay que componer TODAS las matrices, no mirar solo la local de la imagen:
        // el desplazamiento del MediaBox se emite como un "cm" aparte.
        var (x, y) = new PdfContentInspector(firmado).ComposedImageOrigin();

        // La esquina superior izquierda visible está en (X1, Y2) = (20, 872).
        // La imagen se ancla por su esquina inferior izquierda, así que debe caer
        // en x=20 e y=872-84.2=787.8.
        Assert.Equal(20, x, 1);
        Assert.Equal(787.8, y, 1);
    }

    // ============================================================== B. Imágenes

    [Fact]
    public void B08_Jpeg_se_embebe_correctamente()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Imagen(MinimalJpeg));

        var pdf = new PdfContentInspector(firmado);
        Assert.Equal(1, pdf.EmbeddedImages);
        Assert.Equal(1, pdf.DrawnXObjects);
    }

    [Fact]
    public void B09_Png_sin_canal_alfa_no_genera_mascara()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Imagen(CreatePngOpaco(40, 20)));

        // Sin transparencia no debe crearse SMask: sería peso muerto en el archivo.
        Assert.False(new PdfContentInspector(firmado).HasAlphaMask);
    }

    [Fact]
    public void B10_Imagen_de_un_pixel_se_embebe()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Imagen(TestAssets.CreatePng(1, 1)));

        Assert.Equal(1, new PdfContentInspector(firmado).DrawnXObjects);
    }

    [Fact]
    public void B11_Bytes_que_no_son_imagen_fallan_con_excepcion()
    {
        using var origen = TestAssets.CreatePdf(0);
        var destino = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            _service.Sign(origen, destino, [Imagen("no soy una imagen"u8.ToArray())], Momento));
    }

    [Fact]
    public void B12_Imagen_vacia_falla_con_excepcion()
    {
        using var origen = TestAssets.CreatePdf(0);
        var destino = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            _service.Sign(origen, destino, [Imagen([])], Momento));
    }

    [Fact]
    public void B13_La_imagen_se_estira_al_rectangulo_indicado_sin_recortar()
    {
        using var origen = TestAssets.CreatePdf(0);

        // Imagen muy apaisada dentro de un destino casi cuadrado.
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.1, 0.1, 0.4, 0.4),
            Data = TestAssets.CreatePng(400, 20),
        });

        var m = Assert.Single(new PdfContentInspector(firmado).ImageMatrices);

        // La matriz debe reflejar el destino pedido (0.4*595 x 0.4*842), no la
        // proporción de la imagen original.
        Assert.Equal(238, m[0], 1);
        Assert.Equal(336.8, m[3], 1);
    }

    // ================================================================= C. Texto

    [Fact]
    public void C14_Acentos_y_enie_sobreviven_al_pdf()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("Peña, Muñoz, Íñigo, Ángel, ¿Sí? ¡Ojalá!"));

        // Con codificación Unicode el texto va en hexadecimal, así que no se puede buscar
        // como cadena literal; lo que se comprueba es que se generó texto y fuente embebida.
        var pdf = new PdfContentInspector(firmado);
        Assert.True(pdf.TextOperators > 0);
        Assert.True(pdf.HasEmbeddedFont);
    }

    [Fact]
    public void C15_Parentesis_y_barras_no_rompen_el_documento()
    {
        using var origen = TestAssets.CreatePdf(0);

        // En PDF las cadenas van entre paréntesis: sin escapado, esto corrompe el archivo.
        using var firmado = Firmar(origen, Texto(@"Firmado (con permiso) \ ref: C:\docs\(2026)"));

        firmado.Position = 0;
        Assert.Single(_service.Inspect(firmado)); // debe seguir siendo un PDF legible
    }

    [Fact]
    public void C16_Texto_muy_largo_no_revienta()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto(new string('A', 5000)));

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    [Fact]
    public void C17_Texto_en_blanco_no_dibuja_nada()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("   \n  \n "));

        Assert.Equal(0, new PdfContentInspector(firmado).TextOperators);
    }

    [Fact]
    public void C18_Saltos_de_linea_de_windows_equivalen_a_los_de_unix()
    {
        using var origen1 = TestAssets.CreatePdf(0);
        using var origen2 = TestAssets.CreatePdf(0);

        using var conWindows = Firmar(origen1, Texto("uno\r\ndos\r\ntres"));
        using var conUnix = Firmar(origen2, Texto("uno\ndos\ntres"));

        Assert.Equal(
            new PdfContentInspector(conUnix).TextOperators,
            new PdfContentInspector(conWindows).TextOperators);
    }

    [Fact]
    public void C19_Caracteres_sin_glifo_en_la_fuente_no_tumban_la_exportacion()
    {
        using var origen = TestAssets.CreatePdf(0);

        // Open Sans no tiene emoji ni ideogramas; debe degradar, no lanzar.
        using var firmado = Firmar(origen, Texto("Firmado 🖋️ 日本語 ✓"));

        firmado.Position = 0;
        Assert.Single(_service.Inspect(firmado));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(400)]
    public void C20_Tamanos_de_fuente_extremos_no_revientan(double tamano)
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("Firma", tamano));

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    [Fact]
    public void C21_Color_con_canal_alfa_se_interpreta()
    {
        var color = PdfSignatureService.ParseColor("#80FF0000");

        Assert.Equal(255, (int)color.R);
        Assert.Equal(0, (int)color.G);
        Assert.InRange(color.A, 0.4, 0.6);
    }

    // ============================================================= D. Geometría

    [Fact]
    public void D22_Coordenadas_negativas_se_recortan_a_la_pagina()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(-0.5, -0.5, 0.2, 0.1),
            Data = TestAssets.CreatePng(),
        });

        var m = Assert.Single(new PdfContentInspector(firmado).ImageMatrices);

        Assert.True(m[4] >= -0.01, $"La imagen se salió por la izquierda: x={m[4]}");
        Assert.True(m[5] >= -0.01, $"La imagen se salió por abajo: y={m[5]}");
    }

    [Fact]
    public void D23_Tamanos_mayores_que_la_pagina_se_recortan()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.5, 0.5, 5, 5),
            Data = TestAssets.CreatePng(),
        });

        var m = Assert.Single(new PdfContentInspector(firmado).ImageMatrices);

        Assert.True(m[0] <= 595.01, $"Ancho fuera de página: {m[0]}");
        Assert.True(m[3] <= 842.01, $"Alto fuera de página: {m[3]}");
    }

    [Fact]
    public void D24_Elemento_de_tamano_cero_no_produce_un_pdf_invalido()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.5, 0.5, 0, 0),
            Data = TestAssets.CreatePng(),
        });

        firmado.Position = 0;
        Assert.Single(_service.Inspect(firmado));
    }

    [Fact]
    public void D25_Documento_con_las_cuatro_rotaciones_mezcladas()
    {
        using var origen = TestAssets.CreatePdf(0, 90, 180, 270);
        var firma = TestAssets.CreatePng();

        var elementos = Enumerable.Range(0, 4)
            .Select(i => (PlacedElement)new ImageElement
            {
                PageIndex = i,
                Bounds = new NormalizedRect(0.1, 0.7, 0.3, 0.1),
                Data = firma,
            })
            .ToArray();

        using var firmado = Firmar(origen, elementos);

        Assert.Equal(4, new PdfContentInspector(firmado).DrawnXObjects);
    }

    [Fact]
    public void D26_La_ultima_pagina_acepta_elementos()
    {
        using var origen = TestAssets.CreatePdf(0, 0, 0);
        using var firmado = Firmar(origen, new TextElement
        {
            PageIndex = 2,
            Bounds = new NormalizedRect(0.1, 0.1, 0.5, 0.1),
            Text = "Última",
        });

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    // ====================================================== E. Estado e integridad

    [Fact]
    public void E27_El_documento_de_entrada_no_se_modifica()
    {
        using var origen = TestAssets.CreatePdf(0);
        var antes = origen.ToArray();

        using var _ = Firmar(origen, Texto("Firma"));

        Assert.Equal(antes, origen.ToArray());
    }

    [Fact]
    public void E28_Un_pdf_ya_firmado_se_puede_volver_a_firmar()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var primera = Firmar(origen, Texto("Primera firma"));

        primera.Position = 0;
        using var segunda = Firmar(primera, new TextElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.1, 0.2, 0.5, 0.1),
            Text = "Segunda firma",
        });

        // Deben convivir las dos, no sustituirse.
        Assert.True(new PdfContentInspector(segunda).TextOperators >= 2);
    }

    [Fact]
    public void E29_Un_stream_sin_seek_se_procesa_igual()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var sinSeek = new StreamSinSeek(origen.ToArray());
        var destino = new MemoryStream();

        _service.Sign(sinSeek, destino, [Texto("Firma")], Momento, compress: false);

        Assert.True(new PdfContentInspector(destino).TextOperators > 0);
    }

    [Fact]
    public void E30_Firmar_en_paralelo_desde_varios_hilos_es_seguro()
    {
        // El resolver de fuentes se registra en un estado global de PDFsharp; si esa
        // inicialización no fuese segura, aquí aparecerían fallos intermitentes.
        var excepciones = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 16, i =>
        {
            try
            {
                var servicio = new PdfSignatureService();
                using var origen = TestAssets.CreatePdf(0);
                var destino = new MemoryStream();
                servicio.Sign(origen, destino, [Texto($"Firma {i}")], Momento);

                Assert.True(destino.Length > 0);
            }
            catch (Exception ex)
            {
                excepciones.Add(ex);
            }
        });

        Assert.Empty(excepciones);
    }

    // =============================================================== utilidades

    private static MemoryStream CreatePdfConTamano(double anchoPt, double altoPt)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(anchoPt);
        page.Height = XUnit.FromPoint(altoPt);

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreatePdfConMediaBoxDesplazado(
        double x, double y, double ancho, double alto)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(x, y), new XPoint(x + ancho, y + alto));

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    /// <summary>PNG RGB sin canal alfa.</summary>
    private static byte[] CreatePngOpaco(int w, int h)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < w; x++)
            {
                raw.WriteByte(30);
                raw.WriteByte(30);
                raw.WriteByte(120);
            }
        }

        var comp = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(
                   comp, System.IO.Compression.CompressionLevel.Optimal, true))
            z.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var ihdr = new MemoryStream();
        ihdr.Write(Be(w));
        ihdr.Write(Be(h));
        ihdr.Write([8, 2, 0, 0, 0]); // tipo de color 2 = RGB sin alfa
        Chunk(png, "IHDR", ihdr.ToArray());
        Chunk(png, "IDAT", comp.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();

        static byte[] Be(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

        static void Chunk(Stream s, string type, byte[] data)
        {
            s.Write(Be(data.Length));
            var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            s.Write(body);
            s.Write(Be(unchecked((int)Crc(body))));
        }

        static uint Crc(byte[] d)
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

    /// <summary>JPEG válido mínimo de 1x1, para comprobar que no solo se acepta PNG.</summary>
    private static byte[] MinimalJpeg => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==");

    /// <summary>Stream de solo lectura sin seek, como los que a veces da un file picker.</summary>
    private sealed class StreamSinSeek(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
