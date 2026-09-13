using System.Text.Json;
using PdfSharp.Pdf;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Segunda tanda de QA sobre contenido: texto, marcadores, color, integridad del documento
/// y el modelo de firma favorita.
/// </summary>
public class QaContenidoTests
{
    private static readonly DateTime Momento = new(2026, 9, 13, 14, 30, 0);
    private readonly PdfSignatureService _service = new();

    private MemoryStream Firmar(MemoryStream origen, params PlacedElement[] elementos)
    {
        var destino = new MemoryStream();
        _service.Sign(origen, destino, elementos, Momento, compress: false);
        return destino;
    }

    private static TextElement Texto(string contenido) => new()
    {
        PageIndex = 0,
        Bounds = new NormalizedRect(0.1, 0.4, 0.6, 0.3),
        Text = contenido,
    };

    // ================================================================ I. Texto

    [Fact]
    public void I53_El_interlineado_separa_mas_las_lineas()
    {
        using var origen1 = TestAssets.CreatePdf(0);
        using var origen2 = TestAssets.CreatePdf(0);

        var apretado = Texto("una\ndos\ntres");
        apretado.LineSpacing = 1.0;

        var suelto = Texto("una\ndos\ntres");
        suelto.LineSpacing = 3.0;

        using var a = Firmar(origen1, apretado);
        using var b = Firmar(origen2, suelto);

        // Mismo número de líneas, pero el bloque suelto debe ocupar más alto.
        var inspA = new PdfContentInspector(a);
        var inspB = new PdfContentInspector(b);

        Assert.Equal(3, inspA.TextOperators);
        Assert.Equal(3, inspB.TextOperators);
        Assert.NotEqual(inspA.TextPositions, inspB.TextPositions);
    }

    [Fact]
    public void I54_La_negrita_usa_una_fuente_distinta_de_la_normal()
    {
        using var origen1 = TestAssets.CreatePdf(0);
        using var origen2 = TestAssets.CreatePdf(0);

        var normal = Texto("Firma");
        var negrita = Texto("Firma");
        negrita.Bold = true;

        using var a = Firmar(origen1, normal);
        using var b = Firmar(origen2, negrita);

        // Ambas deben embeber fuente, y el archivo en negrita no puede ser idéntico.
        Assert.True(new PdfContentInspector(a).HasEmbeddedFont);
        Assert.True(new PdfContentInspector(b).HasEmbeddedFont);
        Assert.NotEqual(a.Length, b.Length);
    }

    [Fact]
    public void I55_Varios_bloques_de_texto_conviven_en_la_misma_pagina()
    {
        using var origen = TestAssets.CreatePdf(0);

        using var firmado = Firmar(origen,
            new TextElement { PageIndex = 0, Bounds = new NormalizedRect(0.1, 0.1, 0.3, 0.1), Text = "Primero" },
            new TextElement { PageIndex = 0, Bounds = new NormalizedRect(0.1, 0.4, 0.3, 0.1), Text = "Segundo" },
            new TextElement { PageIndex = 0, Bounds = new NormalizedRect(0.1, 0.7, 0.3, 0.1), Text = "Tercero" });

        Assert.Equal(3, new PdfContentInspector(firmado).TextOperators);
    }

    [Fact]
    public void I56_Las_tabulaciones_no_rompen_el_documento()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("Nombre:\tWalter\nCargo:\tDirector"));

        firmado.Position = 0;
        Assert.Single(_service.Inspect(firmado));
    }

    [Fact]
    public void I57_Un_texto_de_solo_saltos_de_linea_no_dibuja_nada()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("\n\n\n"));

        Assert.Equal(0, new PdfContentInspector(firmado).TextOperators);
    }

    [Fact]
    public void I58_Una_palabra_larguisima_sin_espacios_no_revienta()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto(new string('W', 800)));

        Assert.True(new PdfContentInspector(firmado).TextOperators > 0);
    }

    [Fact]
    public void I59_Las_lineas_vacias_intercaladas_se_respetan()
    {
        using var origen = TestAssets.CreatePdf(0);
        using var firmado = Firmar(origen, Texto("Walter\n\nDirector"));

        // La línea en blanco del medio no se dibuja, pero sí desplaza a la siguiente.
        Assert.Equal(2, new PdfContentInspector(firmado).TextOperators);
    }

    // ========================================================= J. Marcadores

    [Fact]
    public void J60_Un_marcador_desconocido_se_conserva_tal_cual()
        => Assert.Equal("Hola {inventado}", PlaceholderResolver.Resolve("Hola {inventado}", Momento));

    [Fact]
    public void J61_Marcadores_adyacentes_se_resuelven_los_dos()
        => Assert.Equal("13/09/2026 14:30", PlaceholderResolver.Resolve("{fecha} {hora}", Momento));

    [Fact]
    public void J62_Primero_de_enero()
        => Assert.Equal("01/01/2026", PlaceholderResolver.Resolve("{fecha}", new DateTime(2026, 1, 1)));

    [Fact]
    public void J63_Ultimo_dia_del_ano()
        => Assert.Equal("31/12/2026", PlaceholderResolver.Resolve("{fecha}", new DateTime(2026, 12, 31)));

    [Fact]
    public void J64_Veintinueve_de_febrero_en_ano_bisiesto()
        => Assert.Equal("29/02/2028", PlaceholderResolver.Resolve("{fecha}", new DateTime(2028, 2, 29)));

    [Fact]
    public void J65_Medianoche_se_formatea_en_veinticuatro_horas()
        => Assert.Equal("00:00", PlaceholderResolver.Resolve("{hora}", new DateTime(2026, 9, 13)));

    [Fact]
    public void J66_La_fecha_larga_usa_el_mes_en_espanol()
    {
        var resultado = PlaceholderResolver.Resolve("{fechalarga}", new DateTime(2026, 1, 5));

        Assert.Contains("enero", resultado, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", resultado);
    }

    // ============================================================== K. Color

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#14145A", 20, 20, 90)]
    [InlineData("14145A", 20, 20, 90)]
    [InlineData("#ffffff", 255, 255, 255)]
    public void K67_Interpreta_hexadecimales_validos(string hex, int r, int g, int b)
    {
        var color = PdfSignatureService.ParseColor(hex);

        Assert.Equal(r, (int)color.R);
        Assert.Equal(g, (int)color.G);
        Assert.Equal(b, (int)color.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("azul")]
    [InlineData("#1234567890")]
    public void K68_Los_valores_invalidos_caen_a_negro(string hex)
    {
        var color = PdfSignatureService.ParseColor(hex);

        Assert.Equal(0, (int)color.R);
        Assert.Equal(0, (int)color.G);
        Assert.Equal(0, (int)color.B);
    }

    [Fact]
    public void K69_Mayusculas_y_minusculas_dan_el_mismo_color()
        => Assert.Equal(
            PdfSignatureService.ParseColor("#AABBCC"),
            PdfSignatureService.ParseColor("#aabbcc"));

    // ======================================================== L. Documento

    [Fact]
    public void L70_Un_pdf_protegido_con_contrasena_falla_de_forma_comprensible()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.SecuritySettings.UserPassword = "secreto";

        var protegido = new MemoryStream();
        doc.Save(protegido, closeStream: false);
        protegido.Position = 0;

        var destino = new MemoryStream();
        var ex = Assert.ThrowsAny<Exception>(() =>
            _service.Sign(protegido, destino, [Texto("Firma")], Momento));

        // No basta con que falle: el mensaje tiene que servirle a alguien.
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void L71_Firmar_conserva_el_numero_de_paginas()
    {
        using var origen = TestAssets.CreatePdf(0, 90, 180, 270, 0);
        using var firmado = Firmar(origen, Texto("Firma"));

        firmado.Position = 0;
        Assert.Equal(5, _service.Inspect(firmado).Count);
    }

    [Fact]
    public void L72_Inspect_no_altera_el_documento()
    {
        using var origen = TestAssets.CreatePdf(0, 0);
        var antes = origen.ToArray();

        _service.Inspect(origen);
        _service.Inspect(origen); // dos veces, para descartar consumo del stream

        Assert.Equal(antes, origen.ToArray());
    }

    [Fact]
    public void L73_Firmar_sin_elementos_conserva_todas_las_paginas()
    {
        using var origen = TestAssets.CreatePdf(0, 0, 0);
        using var firmado = Firmar(origen);

        firmado.Position = 0;
        Assert.Equal(3, _service.Inspect(firmado).Count);
    }

    [Fact]
    public void L74_El_orden_en_que_llegan_los_elementos_no_importa()
    {
        using var origen1 = TestAssets.CreatePdf(0, 0, 0);
        using var origen2 = TestAssets.CreatePdf(0, 0, 0);

        PlacedElement EnPagina(int i) =>
            new TextElement { PageIndex = i, Bounds = new NormalizedRect(0.1, 0.1, 0.3, 0.1), Text = $"P{i}" };

        using var ascendente = Firmar(origen1, EnPagina(0), EnPagina(1), EnPagina(2));
        using var descendente = Firmar(origen2, EnPagina(2), EnPagina(1), EnPagina(0));

        Assert.Equal(
            new PdfContentInspector(ascendente).TextOperators,
            new PdfContentInspector(descendente).TextOperators);
    }

    [Fact]
    public void L75_Cien_elementos_en_una_pagina_se_dibujan_todos()
    {
        using var origen = TestAssets.CreatePdf(0);

        var elementos = Enumerable.Range(0, 100)
            .Select(i => (PlacedElement)new TextElement
            {
                PageIndex = 0,
                Bounds = new NormalizedRect(0.01 * (i % 10), 0.01 * (i / 10), 0.08, 0.02),
                Text = $"{i}",
                FontSizePt = 6,
            })
            .ToArray();

        using var firmado = Firmar(origen, elementos);

        Assert.Equal(100, new PdfContentInspector(firmado).TextOperators);
    }

    // ================================================== M. Firma favorita

    [Fact]
    public void M76_Una_firma_recien_creada_esta_vacia()
        => Assert.True(new FavoriteSignature().IsEmpty);

    [Fact]
    public void M77_Detecta_si_tiene_imagen_o_texto()
    {
        Assert.True(new FavoriteSignature { ImageFileName = "firma.png" }.HasImage);
        Assert.True(new FavoriteSignature { Text = "Walter" }.HasText);
        Assert.False(new FavoriteSignature { Text = "   " }.HasText);
        Assert.False(new FavoriteSignature { ImageFileName = "" }.HasImage);
    }

    [Fact]
    public void M78_Los_valores_por_defecto_son_razonables()
    {
        var favorita = new FavoriteSignature();

        Assert.Equal(11, favorita.FontSizePt);
        Assert.Equal("#000000", favorita.ColorHex);
        Assert.False(favorita.Bold);
    }

    [Fact]
    public void M79_Sobrevive_a_una_ida_y_vuelta_por_json()
    {
        // Es como se persiste en Preferences, así que romper esto perdería la firma
        // guardada de todos los usuarios al actualizar.
        var original = new FavoriteSignature
        {
            ImageFileName = "firma-favorita.png",
            Text = "Walter E. Calcagno Lucares\nDirector\nFirmado el {fecha}",
            FontSizePt = 13.5,
            ColorHex = "#14145A",
            Bold = true,
        };

        var copia = JsonSerializer.Deserialize<FavoriteSignature>(JsonSerializer.Serialize(original))!;

        Assert.Equal(original.ImageFileName, copia.ImageFileName);
        Assert.Equal(original.Text, copia.Text);
        Assert.Equal(original.FontSizePt, copia.FontSizePt);
        Assert.Equal(original.ColorHex, copia.ColorHex);
        Assert.Equal(original.Bold, copia.Bold);
    }

    [Fact]
    public void M80_El_marcador_de_fecha_se_guarda_sin_resolver()
    {
        var favorita = new FavoriteSignature { Text = "Firmado el {fecha}" };
        var copia = JsonSerializer.Deserialize<FavoriteSignature>(JsonSerializer.Serialize(favorita))!;

        // Si se resolviera al guardar, una firma favorita estamparía siempre la fecha
        // del día en que se creó.
        Assert.Contains("{fecha}", copia.Text);
    }

    // ============================================ N. Mensajes de error visibles

    // El MainViewModel vuelca ex.Message en la barra de estado, así que estos textos son
    // interfaz de usuario y no detalles internos. Sin estas pruebas, un PDF dañado volvería
    // a mostrar "value ('-16') must be a non-negative value" en mitad de la pantalla.

    private static string MensajeAlFirmar(Func<MemoryStream> documento, PlacedElement? elemento = null)
    {
        var destino = new MemoryStream();
        var ex = Assert.Throws<PdfSignerException>(() => new PdfSignatureService().Sign(
            documento(), destino, [elemento ?? Texto("Firma")], Momento));
        return ex.Message;
    }

    [Fact]
    public void N81_Un_pdf_con_contrasena_avisa_de_la_contrasena_en_espanol()
    {
        var mensaje = MensajeAlFirmar(() =>
        {
            using var doc = new PdfDocument();
            doc.AddPage();
            doc.SecuritySettings.UserPassword = "secreto";
            var ms = new MemoryStream();
            doc.Save(ms, closeStream: false);
            ms.Position = 0;
            return ms;
        });

        Assert.Contains("contraseña", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void N82_Un_pdf_danado_no_muestra_el_error_criptico_de_la_libreria()
    {
        var mensaje = MensajeAlFirmar(() => new MemoryStream("%PDF-1.4 basura"u8.ToArray()));

        Assert.Contains("no parece un PDF válido", mensaje);
        Assert.DoesNotContain("non-negative", mensaje);
        Assert.DoesNotContain("Parameter", mensaje);
    }

    [Fact]
    public void N83_Un_archivo_vacio_avisa_en_espanol()
        => Assert.Contains("no parece un PDF válido", MensajeAlFirmar(() => new MemoryStream([])));

    [Fact]
    public void N84_Un_archivo_que_no_es_pdf_avisa_en_espanol()
        => Assert.Contains("no parece un PDF válido",
            MensajeAlFirmar(() => new MemoryStream("Soy un .txt"u8.ToArray())));

    [Fact]
    public void N85_Una_imagen_no_admitida_sugiere_los_formatos_validos()
    {
        var mensaje = MensajeAlFirmar(
            () => TestAssets.CreatePdf(0),
            new ImageElement
            {
                PageIndex = 0,
                Bounds = new NormalizedRect(0.1, 0.1, 0.3, 0.1),
                Data = "no soy una imagen"u8.ToArray(),
            });

        Assert.Contains("PNG", mensaje);
        Assert.Contains("JPG", mensaje);
    }

    [Fact]
    public void N86_La_excepcion_original_se_conserva_para_diagnostico()
    {
        var destino = new MemoryStream();
        var ex = Assert.Throws<PdfSignerException>(() => _service.Sign(
            new MemoryStream("%PDF-1.4 basura"u8.ToArray()), destino, [Texto("x")], Momento));

        // Traducir el mensaje no debe perder la causa real.
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void N87_Inspect_tambien_traduce_los_errores()
    {
        var ex = Assert.Throws<PdfSignerException>(() =>
            _service.Inspect(new MemoryStream("no es un pdf"u8.ToArray())));

        Assert.Contains("no parece un PDF válido", ex.Message);
    }
}
