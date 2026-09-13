namespace PdfSigner.Core.Tests;

/// <summary>
/// El mapper es la pieza donde se rompen estas aplicaciones: PDFsharp ignora /Rotate, así que
/// sin estas pruebas una firma sobre un escaneado apaisado sale girada sin que nadie lo note
/// hasta tener el documento delante.
/// </summary>
public class PageGeometryTests
{
    private const double W = TestAssets.A4WidthPt;   // 595
    private const double H = TestAssets.A4HeightPt;  // 842

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-270, 90)]
    public void Normaliza_rotaciones_fuera_de_rango(int entrada, int esperada)
    {
        var g = new PageGeometry(W, H, entrada);
        Assert.Equal(esperada, g.Rotation);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, true)]
    [InlineData(180, false)]
    [InlineData(270, true)]
    public void Intercambia_ancho_y_alto_solo_en_cuartos_de_giro(int rotacion, bool intercambia)
    {
        var g = new PageGeometry(W, H, rotacion);

        Assert.Equal(intercambia, g.IsQuarterTurned);
        Assert.Equal(intercambia ? H : W, g.VisibleWidthPt);
        Assert.Equal(intercambia ? W : H, g.VisibleHeightPt);
    }

    [Fact]
    public void Rectangulo_normalizado_se_escala_al_espacio_visible()
    {
        var g = new PageGeometry(W, H, 0);
        var rect = g.ToVisibleRect(new NormalizedRect(0.10, 0.70, 0.35, 0.09));

        Assert.Equal(59.5, rect.X, 3);
        Assert.Equal(589.4, rect.Y, 3);
        Assert.Equal(208.25, rect.Width, 3);
        Assert.Equal(75.78, rect.Height, 3);
    }

    [Fact]
    public void Rectangulo_se_escala_contra_las_dimensiones_visibles_si_la_pagina_esta_rotada()
    {
        var g = new PageGeometry(W, H, 90);
        var rect = g.ToVisibleRect(new NormalizedRect(0.5, 0.5, 0.25, 0.25));

        // En una página rotada 90°, el usuario ve 842x595: el ancho debe salir de 842, no de 595.
        Assert.Equal(H * 0.5, rect.X, 3);
        Assert.Equal(W * 0.5, rect.Y, 3);
        Assert.Equal(H * 0.25, rect.Width, 3);
        Assert.Equal(W * 0.25, rect.Height, 3);
    }

    [Fact]
    public void Recorta_lo_que_se_sale_de_la_pagina()
    {
        var g = new PageGeometry(W, H, 0);
        var rect = g.ToVisibleRect(new NormalizedRect(0.9, 0.9, 0.5, 0.5));

        Assert.True(rect.X + rect.Width <= W + 0.001);
        Assert.True(rect.Y + rect.Height <= H + 0.001);
    }

    [Theory]
    // La esquina superior izquierda de lo VISIBLE cae en una esquina distinta del MediaBox
    // según cómo el visor haya rotado la página.
    [InlineData(0, 0, 0)]        // se queda donde está
    [InlineData(90, 0, H)]       // esquina inferior izquierda
    [InlineData(180, W, H)]      // esquina inferior derecha
    [InlineData(270, W, 0)]      // esquina superior derecha
    public void El_origen_visible_cae_en_la_esquina_correcta_del_MediaBox(
        int rotacion, double rawX, double rawY)
    {
        var g = new PageGeometry(W, H, rotacion);
        var (x, y) = g.VisibleToRaw(0, 0);

        Assert.Equal(rawX, x, 3);
        Assert.Equal(rawY, y, 3);
    }

    [Fact]
    public void Todo_punto_visible_cae_dentro_del_MediaBox()
    {
        foreach (var rotacion in new[] { 0, 90, 180, 270 })
        {
            var g = new PageGeometry(W, H, rotacion);

            foreach (var (vx, vy) in new[]
                     {
                         (0d, 0d),
                         (g.VisibleWidthPt, 0d),
                         (0d, g.VisibleHeightPt),
                         (g.VisibleWidthPt, g.VisibleHeightPt),
                     })
            {
                var (x, y) = g.VisibleToRaw(vx, vy);

                Assert.InRange(x, -0.001, W + 0.001);
                Assert.InRange(y, -0.001, H + 0.001);
            }
        }
    }

    [Fact]
    public void Rechaza_dimensiones_invalidas()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PageGeometry(0, H, 0));
}
