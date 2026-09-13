using PdfSharp.Drawing;

namespace PdfSigner.Core.Tests;

public class PdfSignatureServiceTests
{
    private static readonly DateTime Momento = new(2026, 9, 13, 14, 30, 0);
    private readonly PdfSignatureService _service = new();

    private MemoryStream Firmar(MemoryStream origen, params PlacedElement[] elementos)
    {
        var destino = new MemoryStream();
        // compress:false deja los operadores legibles para poder inspeccionarlos.
        _service.Sign(origen, destino, elementos, Momento, compress: false);
        return destino;
    }

    [Fact]
    public void Inspect_devuelve_la_geometria_de_cada_pagina()
    {
        using var pdf = TestAssets.CreatePdf(0, 90, 180);
        var paginas = _service.Inspect(pdf);

        Assert.Equal(3, paginas.Count);
        Assert.Equal(0, paginas[0].Rotation);
        Assert.Equal(90, paginas[1].Rotation);
        Assert.Equal(180, paginas[2].Rotation);

        // La página rotada 90° se ve apaisada aunque su MediaBox siga siendo vertical.
        Assert.Equal(TestAssets.A4HeightPt, paginas[1].VisibleWidthPt, 3);
        Assert.Equal(TestAssets.A4WidthPt, paginas[1].VisibleHeightPt, 3);
    }

    [Fact]
    public void El_texto_se_inserta_como_texto_real_no_como_imagen()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new TextElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.1, 0.8, 0.5, 0.1),
            Text = "Walter E. Calcagno Lucares\nDirector\nFirmado el {fecha}",
        });

        var pdf = new PdfContentInspector(firmado);

        Assert.Equal(3, pdf.TextOperators);                    // una por línea
        Assert.True(pdf.HasEmbeddedFont);                      // la fuente viaja en el archivo
        Assert.True(pdf.ContainsText("Walter E. Calcagno Lucares"));
        Assert.Equal(0, pdf.EmbeddedImages);                   // nada rasterizado
    }

    [Fact]
    public void El_placeholder_de_fecha_se_resuelve_al_exportar()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new TextElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.1, 0.8, 0.5, 0.05),
            Text = "Firmado el {fecha}",
        });

        var pdf = new PdfContentInspector(firmado);

        Assert.True(pdf.ContainsText("Firmado el 13/09/2026"));
        Assert.False(pdf.ContainsText("{fecha}"));
    }

    [Fact]
    public void La_imagen_se_embebe_conservando_el_canal_alfa()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.1, 0.7, 0.35, 0.09),
            Data = TestAssets.CreatePng(),
        });

        var pdf = new PdfContentInspector(firmado);

        Assert.Equal(1, pdf.EmbeddedImages);
        Assert.Equal(1, pdf.DrawnXObjects);
        Assert.True(pdf.HasAlphaMask); // sin esto, la firma saldría sobre un recuadro opaco
    }

    [Fact]
    public void Una_imagen_usada_en_varias_paginas_se_embebe_una_sola_vez()
    {
        using var origen = TestAssets.CreatePdf(0, 0, 0);
        var firma = TestAssets.CreatePng();

        using var firmado = Firmar(origen,
            new ImageElement { PageIndex = 0, Bounds = new NormalizedRect(0.1, 0.1, 0.2, 0.1), Data = firma },
            new ImageElement { PageIndex = 1, Bounds = new NormalizedRect(0.1, 0.1, 0.2, 0.1), Data = firma },
            new ImageElement { PageIndex = 2, Bounds = new NormalizedRect(0.1, 0.1, 0.2, 0.1), Data = firma });

        var pdf = new PdfContentInspector(firmado);

        Assert.Equal(3, pdf.DrawnXObjects);   // se dibuja tres veces
        Assert.Equal(1, pdf.EmbeddedImages);  // pero los bytes viajan una sola vez
    }

    [Fact]
    public void En_una_pagina_sin_rotar_la_imagen_se_coloca_sin_giro()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.10, 0.70, 0.35, 0.09),
            Data = TestAssets.CreatePng(),
        });

        var pdf = new PdfContentInspector(firmado);
        var m = Assert.Single(pdf.ImageMatrices);

        Assert.False(pdf.HasRotationTransform);

        // PDFsharp voltea Y por nosotros: dibujar a 0.70*842 desde arriba con alto 75.78
        // debe aterrizar en 842 - 589.4 - 75.78 = 176.82 medido desde abajo.
        Assert.Equal(59.5, m[4], 2);
        Assert.Equal(176.82, m[5], 2);
    }

    [Fact]
    public void En_una_pagina_rotada_la_imagen_se_gira_para_verse_derecha()
    {
        using var origen = TestAssets.CreatePdf(90);
        using var firmado = Firmar(origen, new ImageElement
        {
            PageIndex = 0,
            Bounds = new NormalizedRect(0.10, 0.70, 0.35, 0.09),
            Data = TestAssets.CreatePng(),
        });

        var pdf = new PdfContentInspector(firmado);

        // Esta es la regresión que protege todo el proyecto: si alguien quita la compensación
        // de /Rotate, la firma sale tumbada en cualquier PDF escaneado o apaisado.
        Assert.True(pdf.HasRotationTransform,
            "No se emitió ninguna matriz de giro: no se compensó /Rotate.");

        // Comprobación geométrica completa. La página se ve como 842x595, así que el
        // rectángulo visible es x=84.2 y=416.5 w=294.7 h=53.55. Su esquina inferior
        // izquierda vista (84.2, 470.05) debe caer en (470.05, 84.2) del MediaBox.
        var (x, y) = pdf.ComposedImageOrigin();
        Assert.Equal(470.05, x, 1);
        Assert.Equal(84.2, y, 1);
    }

    [Fact]
    public void El_contenido_original_del_pdf_se_conserva()
    {
        using var origen = TestAssets.CreatePdf(0, 0);
        using var firmado = Firmar(origen, new TextElement
        {
            PageIndex = 1,
            Bounds = new NormalizedRect(0.1, 0.1, 0.5, 0.1),
            Text = "Firma",
        });

        // Firmar no debe perder páginas.
        firmado.Position = 0;
        Assert.Equal(2, _service.Inspect(firmado).Count);
    }

    [Fact]
    public void Una_pagina_inexistente_falla_con_un_mensaje_claro()
    {
        using var origen = TestAssets.CreatePdf(0);
        var destino = new MemoryStream();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Sign(origen, destino,
                [new TextElement { PageIndex = 7, Text = "x" }], Momento));

        Assert.Contains("página 7", ex.Message);
    }

    [Fact]
    public void Firmar_sin_elementos_produce_un_pdf_valido()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen);

        firmado.Position = 0;
        Assert.Single(_service.Inspect(firmado));
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("00FF00", 0, 255, 0)]
    [InlineData("#0000FF", 0, 0, 255)]
    [InlineData("no-es-color", 0, 0, 0)]
    [InlineData(null, 0, 0, 0)]
    public void Interpreta_colores_hexadecimales(string? hex, int r, int g, int b)
    {
        var color = PdfSignatureService.ParseColor(hex);

        Assert.Equal(XColor.FromArgb(r, g, b), color);
    }
}
