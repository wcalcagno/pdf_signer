using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Segunda tanda de QA, centrada en geometría: combinaciones de MediaBox desplazado con
/// rotación, recorte de coordenadas y proporciones de página poco habituales.
/// </summary>
/// <remarks>
/// El grupo F es el de mayor riesgo de toda la suite. La corrección del origen del MediaBox
/// y la compensación de /Rotate se escribieron por separado, y aquí se comprueba que no se
/// estorban entre sí: un PDF de imprenta rotado cae justo en la intersección de ambas.
/// </remarks>
public class QaGeometriaTests
{
    private static readonly DateTime Momento = new(2026, 9, 13, 14, 30, 0);
    private readonly PdfSignatureService _service = new();

    private MemoryStream Firmar(MemoryStream origen, params PlacedElement[] elementos)
    {
        var destino = new MemoryStream();
        _service.Sign(origen, destino, elementos, Momento, compress: false);
        return destino;
    }

    private static MemoryStream CreatePdf(
        double x, double y, double ancho, double alto, int rotacion = 0)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(x, y), new XPoint(x + ancho, y + alto));
        page.Rotate = rotacion;

        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    private static ImageElement Firma(NormalizedRect bounds) => new()
    {
        PageIndex = 0,
        Bounds = bounds,
        Data = TestAssets.CreatePng(),
    };

    // ============================ F. MediaBox desplazado combinado con rotación

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void F31_Con_MediaBox_desplazado_la_firma_cae_dentro_de_la_pagina(int rotacion)
    {
        const double x = 20, y = 30, w = 595, h = 842;
        using var origen = CreatePdf(x, y, w, h, rotacion);

        using var firmado = Firmar(origen, Firma(new NormalizedRect(0.1, 0.1, 0.2, 0.1)));
        var (px, py) = new PdfContentInspector(firmado).ComposedImageOrigin();

        // La invariante que de verdad importa: el contenido no puede salirse del MediaBox.
        // Si sale, el visor lo recorta y la firma desaparece del documento.
        Assert.InRange(px, x - 0.5, x + w + 0.5);
        Assert.InRange(py, y - 0.5, y + h + 0.5);
    }

    [Fact]
    public void F31b_MediaBox_desplazado_con_rotacion_90_cae_en_la_posicion_exacta()
    {
        // Derivado a mano, que es lo único que de verdad valida la combinación:
        //   visible = 842x595; rect visible = x 84.2, y 59.5, w 168.4, h 59.5
        //   esquina inferior izquierda vista = (84.2, 119)
        //   visible -> raw (giro 90) = (119, 842-84.2) = (119, 757.8) con Y hacia abajo
        //   a espacio PDF: (119, 842-757.8) = (119, 84.2)
        //   más el origen del MediaBox: (139, 114.2)
        using var origen = CreatePdf(20, 30, 595, 842, rotacion: 90);
        using var firmado = Firmar(origen, Firma(new NormalizedRect(0.1, 0.1, 0.2, 0.1)));

        var (px, py) = new PdfContentInspector(firmado).ComposedImageOrigin();

        Assert.Equal(139, px, 1);
        Assert.Equal(114.2, py, 1);
    }

    [Fact]
    public void F32_MediaBox_desplazado_sin_rotacion_cae_en_la_posicion_exacta()
    {
        using var origen = CreatePdf(20, 30, 595, 842);
        using var firmado = Firmar(origen, Firma(new NormalizedRect(0, 0, 0.2, 0.1)));

        var (px, py) = new PdfContentInspector(firmado).ComposedImageOrigin();

        Assert.Equal(20, px, 1);       // X1
        Assert.Equal(787.8, py, 1);    // Y2 - alto = 872 - 84.2
    }

    [Fact]
    public void F33_Un_MediaBox_en_el_origen_no_altera_la_posicion()
    {
        using var conOrigen = CreatePdf(0, 0, 595, 842);
        using var firmado = Firmar(conOrigen, Firma(new NormalizedRect(0.1, 0.7, 0.35, 0.09)));

        var (px, py) = new PdfContentInspector(firmado).ComposedImageOrigin();

        // Debe coincidir con el resultado histórico ya verificado en la suite original.
        Assert.Equal(59.5, px, 1);
        Assert.Equal(176.82, py, 1);
    }

    [Fact]
    public void F34_MediaBox_con_origen_negativo_se_respeta()
    {
        using var origen = CreatePdf(-30, -40, 595, 842);
        using var firmado = Firmar(origen, Firma(new NormalizedRect(0, 0, 0.2, 0.1)));

        var (px, _) = new PdfContentInspector(firmado).ComposedImageOrigin();

        Assert.Equal(-30, px, 1);
    }

    [Fact]
    public void F35_MediaBox_con_desplazamiento_grande_se_respeta()
    {
        using var origen = CreatePdf(500, 700, 595, 842);
        using var firmado = Firmar(origen, Firma(new NormalizedRect(0, 0, 0.2, 0.1)));

        var (px, _) = new PdfContentInspector(firmado).ComposedImageOrigin();

        Assert.Equal(500, px, 1);
    }

    [Fact]
    public void F36_PageGeometry_expone_el_origen_del_MediaBox()
    {
        var geometria = new PageGeometry(595, 842, 0, originX: 20, originY: 30);

        Assert.Equal(20, geometria.OriginX);
        Assert.Equal(30, geometria.OriginY);
        Assert.True(geometria.HasOffsetOrigin);
        Assert.False(new PageGeometry(595, 842, 0).HasOffsetOrigin);
    }

    // ========================================== G. Recorte de coordenadas

    [Theory]
    [InlineData(0, 0, 1, 1)]         // ocupa toda la página
    [InlineData(0, 0, 0.5, 0.5)]     // esquina superior izquierda
    [InlineData(0.5, 0.5, 0.5, 0.5)] // justo hasta el borde inferior derecho
    public void G37_Los_rectangulos_validos_no_se_alteran(double x, double y, double w, double h)
    {
        var original = new NormalizedRect(x, y, w, h);
        Assert.Equal(original, original.Clamped());
    }

    [Fact]
    public void G38_Un_rectangulo_que_desborda_por_la_derecha_se_reubica()
    {
        var recortado = new NormalizedRect(0.9, 0.1, 0.5, 0.1).Clamped();

        // Se conserva el tamaño y se desplaza el origen: encoger sorprendería más al usuario
        // que ver su firma pegada al margen.
        Assert.Equal(0.5, recortado.Width, 6);
        Assert.Equal(0.5, recortado.X, 6);
    }

    [Fact]
    public void G39_Un_rectangulo_mayor_que_la_pagina_se_limita_a_la_pagina()
    {
        var recortado = new NormalizedRect(0, 0, 3, 4).Clamped();

        Assert.Equal(1, recortado.Width, 6);
        Assert.Equal(1, recortado.Height, 6);
        Assert.Equal(0, recortado.X, 6);
        Assert.Equal(0, recortado.Y, 6);
    }

    [Fact]
    public void G40_Las_coordenadas_negativas_se_llevan_a_cero()
    {
        var recortado = new NormalizedRect(-2, -3, 0.2, 0.2).Clamped();

        Assert.Equal(0, recortado.X, 6);
        Assert.Equal(0, recortado.Y, 6);
    }

    [Fact]
    public void G41_El_tamano_negativo_se_anula()
    {
        var recortado = new NormalizedRect(0.5, 0.5, -1, -1).Clamped();

        Assert.Equal(0, recortado.Width, 6);
        Assert.Equal(0, recortado.Height, 6);
    }

    [Fact]
    public void G42_Recortar_es_idempotente()
    {
        var una = new NormalizedRect(1.5, -0.5, 2, 2).Clamped();
        var dos = una.Clamped();

        Assert.Equal(una, dos);
    }

    [Fact]
    public void G43_El_resultado_siempre_cabe_en_la_pagina()
    {
        double[] valores = [-5, -0.1, 0, 0.3, 0.999, 1, 1.5, 10];

        foreach (var x in valores)
        foreach (var w in valores)
        {
            var r = new NormalizedRect(x, 0.2, w, 0.2).Clamped();

            Assert.InRange(r.X, 0, 1);
            Assert.InRange(r.Width, 0, 1);
            Assert.True(r.X + r.Width <= 1.000001, $"Se sale: x={r.X} w={r.Width}");
        }
    }

    [Fact]
    public void G44_Dos_rectangulos_iguales_son_iguales()
    {
        // Es un record struct: la igualdad por valor permite comparar en los tests.
        Assert.Equal(
            new NormalizedRect(0.1, 0.2, 0.3, 0.4),
            new NormalizedRect(0.1, 0.2, 0.3, 0.4));
    }

    // ============================================ H. Proporciones de página

    [Theory]
    [InlineData(595, 842)]    // A4 vertical
    [InlineData(842, 595)]    // A4 apaisado
    [InlineData(612, 792)]    // Carta
    [InlineData(612, 1008)]   // Oficio
    [InlineData(500, 500)]    // cuadrada
    [InlineData(2000, 100)]   // panorámica extrema
    public void H45_Cualquier_proporcion_de_pagina_admite_firma(double ancho, double alto)
    {
        using var origen = CreatePdf(0, 0, ancho, alto);
        using var firmado = Firmar(origen, Firma(new NormalizedRect(0.1, 0.1, 0.3, 0.2)));

        var (px, py) = new PdfContentInspector(firmado).ComposedImageOrigin();

        Assert.InRange(px, -0.5, ancho + 0.5);
        Assert.InRange(py, -0.5, alto + 0.5);
    }

    [Fact]
    public void H46_En_una_pagina_cuadrada_rotar_no_cambia_las_dimensiones_visibles()
    {
        var sinRotar = new PageGeometry(500, 500, 0);
        var rotada = new PageGeometry(500, 500, 90);

        Assert.Equal(sinRotar.VisibleWidthPt, rotada.VisibleWidthPt);
        Assert.Equal(sinRotar.VisibleHeightPt, rotada.VisibleHeightPt);
    }

    [Fact]
    public void H47_VisibleToRaw_y_las_dimensiones_son_coherentes_en_las_cuatro_rotaciones()
    {
        foreach (var rotacion in new[] { 0, 90, 180, 270 })
        {
            var g = new PageGeometry(595, 842, rotacion);

            // La esquina opuesta de lo visible debe caer en una esquina del MediaBox.
            var (x, y) = g.VisibleToRaw(g.VisibleWidthPt, g.VisibleHeightPt);

            Assert.True(
                (Math.Abs(x) < 0.001 || Math.Abs(x - 595) < 0.001) &&
                (Math.Abs(y) < 0.001 || Math.Abs(y - 842) < 0.001),
                $"Rotación {rotacion}: la esquina cayó en ({x}, {y}), que no es una esquina.");
        }
    }

    [Theory]
    [InlineData(45)]
    [InlineData(100)]
    [InlineData(359)]
    public void H48_Las_rotaciones_no_multiplas_de_noventa_se_truncan(int rotacion)
    {
        var g = new PageGeometry(595, 842, rotacion);

        // El estándar PDF solo admite múltiplos de 90; cualquier otra cosa se normaliza
        // hacia abajo en lugar de propagar un valor que rompería la matriz de giro.
        Assert.Contains(g.Rotation, new[] { 0, 90, 180, 270 });
    }

    [Theory]
    [InlineData(720, 0)]
    [InlineData(-360, 0)]
    [InlineData(810, 90)]
    [InlineData(-450, 270)]
    public void H49_Las_vueltas_completas_se_reducen(int entrada, int esperada)
        => Assert.Equal(esperada, new PageGeometry(595, 842, entrada).Rotation);

    [Fact]
    public void H50_ToVisibleRect_nunca_devuelve_medidas_negativas()
    {
        var g = new PageGeometry(595, 842, 90);
        var r = g.ToVisibleRect(new NormalizedRect(-1, -1, -1, -1));

        Assert.True(r.Width >= 0);
        Assert.True(r.Height >= 0);
        Assert.True(r.X >= 0);
        Assert.True(r.Y >= 0);
    }

    [Fact]
    public void H51_ToVisibleRect_llena_la_pagina_con_el_rectangulo_unidad()
    {
        var g = new PageGeometry(595, 842, 0);
        var r = g.ToVisibleRect(new NormalizedRect(0, 0, 1, 1));

        Assert.Equal(595, r.Width, 3);
        Assert.Equal(842, r.Height, 3);
    }

    [Fact]
    public void H52_Rechaza_dimensiones_no_positivas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageGeometry(-1, 842, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageGeometry(595, 0, 0));
    }
}
