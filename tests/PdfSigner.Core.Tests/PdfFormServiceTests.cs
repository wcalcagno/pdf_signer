namespace PdfSigner.Core.Tests;

/// <summary>
/// Lectura de campos de formulario y, sobre todo, su posición en la página.
/// </summary>
/// <remarks>
/// Traducir el <c>/Rect</c> de un campo al espacio visible arrastra las mismas dos trampas
/// que firmar: la rotación de la página y un MediaBox que no empiece en el origen. Si la
/// conversión falla, los campos se dibujan desplazados sobre el documento y no hay ningún
/// error que lo delate.
/// </remarks>
public class PdfFormServiceTests
{
    private readonly PdfFormService _servicio = new();

    // ============================================== detección y enumeración

    [Fact]
    public void Un_pdf_sin_formulario_no_devuelve_campos()
    {
        using var pdf = FormAssets.CrearSinFormulario();

        Assert.False(_servicio.HasForm(pdf));
        Assert.Empty(_servicio.Read(pdf));
    }

    [Fact]
    public void Detecta_el_formulario()
    {
        using var pdf = FormAssets.CrearFormulario();
        Assert.True(_servicio.HasForm(pdf));
    }

    [Fact]
    public void Lee_los_cinco_tipos_de_campo()
    {
        using var pdf = FormAssets.CrearFormulario();
        var campos = _servicio.Read(pdf).ToDictionary(c => c.Name);

        Assert.Equal(PdfFormFieldKind.Text, campos["nombre"].Kind);
        Assert.Equal(PdfFormFieldKind.CheckBox, campos["acepto"].Kind);
        Assert.Equal(PdfFormFieldKind.RadioGroup, campos["respuesta"].Kind);
        Assert.Equal(PdfFormFieldKind.ComboBox, campos["color"].Kind);
        Assert.Equal(PdfFormFieldKind.ListBox, campos["turno"].Kind);
    }

    [Fact]
    public void Lee_los_atributos_de_los_campos_de_texto()
    {
        using var pdf = FormAssets.CrearFormulario();
        var campos = _servicio.Read(pdf).ToDictionary(c => c.Name);

        Assert.Equal(60, campos["nombre"].MaxLength);
        Assert.True(campos["notas"].MultiLine);
        Assert.False(campos["nombre"].MultiLine);
        Assert.True(campos["bloqueado"].ReadOnly);
        Assert.False(campos["bloqueado"].IsEditable);
        Assert.True(campos["nombre"].IsEditable);
    }

    [Fact]
    public void Lee_las_opciones_de_desplegables_y_listas()
    {
        using var pdf = FormAssets.CrearFormulario();
        var campos = _servicio.Read(pdf).ToDictionary(c => c.Name);

        Assert.Equal(["Azul", "Verde", "Rojo"], campos["color"].Options);
        Assert.Equal(["Mañana", "Tarde"], campos["turno"].Options);
    }

    [Fact]
    public void Un_grupo_de_radio_tiene_una_zona_por_opcion()
    {
        using var pdf = FormAssets.CrearFormulario();
        var respuesta = _servicio.Read(pdf).Single(c => c.Name == "respuesta");

        Assert.Equal(2, respuesta.Widgets.Count);
        Assert.Equal(["si", "no"], respuesta.Widgets.Select(w => w.OptionName));
        Assert.Equal(["si", "no"], respuesta.Options);
    }

    [Fact]
    public void Los_campos_normales_tienen_una_sola_zona()
    {
        using var pdf = FormAssets.CrearFormulario();

        foreach (var campo in _servicio.Read(pdf).Where(c => c.Kind != PdfFormFieldKind.RadioGroup))
            Assert.Single(campo.Widgets);
    }

    // ================================================ posición en la página

    [Fact]
    public void Situa_el_campo_en_su_posicion_real()
    {
        using var pdf = FormAssets.CrearFormulario();
        var nombre = _servicio.Read(pdf).Single(c => c.Name == "nombre");
        var b = nombre.Widgets[0].Bounds;

        // El rectángulo PDF es [100 700 300 720] sobre un A4 de 595x842. En espacio visible,
        // con Y hacia abajo: x=100, y=842-720=122, ancho 200, alto 20.
        Assert.Equal(100 / 595.0, b.X, 4);
        Assert.Equal(122 / 842.0, b.Y, 4);
        Assert.Equal(200 / 595.0, b.Width, 4);
        Assert.Equal(20 / 842.0, b.Height, 4);
    }

    [Fact]
    public void En_una_pagina_rotada_el_campo_se_reubica_y_cambia_de_orientacion()
    {
        using var pdf = FormAssets.CrearFormulario(rotacion: 90);
        var nombre = _servicio.Read(pdf).Single(c => c.Name == "nombre");
        var b = nombre.Widgets[0].Bounds;

        // Con /Rotate 90 el usuario ve 842x595. Derivado a mano:
        //   esquinas en espacio local con Y abajo: (100,122) y (300,142)
        //   RawToVisible(rx,ry) = (842-ry, rx)  ->  (720,100) y (700,300)
        //   luego x=700, y=100, ancho 20, alto 200: el campo queda "de pie"
        Assert.Equal(700 / 842.0, b.X, 4);
        Assert.Equal(100 / 595.0, b.Y, 4);
        Assert.Equal(20 / 842.0, b.Width, 4);
        Assert.Equal(200 / 595.0, b.Height, 4);
    }

    [Fact]
    public void Un_MediaBox_desplazado_no_descoloca_los_campos()
    {
        // Las coordenadas del campo se desplazan igual que el MediaBox, así que su posición
        // RELATIVA a la página no cambia. Si la conversión ignorase el origen, saldría movida.
        using var normal = FormAssets.CrearFormulario();
        using var desplazado = FormAssets.CrearFormulario(origenX: 20, origenY: 30);

        var a = _servicio.Read(normal).Single(c => c.Name == "nombre").Widgets[0].Bounds;
        var b = _servicio.Read(desplazado).Single(c => c.Name == "nombre").Widgets[0].Bounds;

        Assert.Equal(a.X, b.X, 4);
        Assert.Equal(a.Y, b.Y, 4);
        Assert.Equal(a.Width, b.Width, 4);
        Assert.Equal(a.Height, b.Height, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Ningun_campo_queda_fuera_de_la_pagina(int rotacion)
    {
        using var pdf = FormAssets.CrearFormulario(rotacion);

        foreach (var campo in _servicio.Read(pdf))
        {
            foreach (var w in campo.Widgets)
            {
                Assert.InRange(w.Bounds.X, 0, 1);
                Assert.InRange(w.Bounds.Y, 0, 1);
                Assert.True(w.Bounds.X + w.Bounds.Width <= 1.0001,
                    $"{campo.Name} se sale por la derecha con rotación {rotacion}");
                Assert.True(w.Bounds.Y + w.Bounds.Height <= 1.0001,
                    $"{campo.Name} se sale por abajo con rotación {rotacion}");
                Assert.True(w.Bounds.Width > 0 && w.Bounds.Height > 0,
                    $"{campo.Name} quedó sin superficie con rotación {rotacion}");
            }
        }
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void Un_cuarto_de_giro_intercambia_ancho_y_alto_del_campo(int rotacion)
    {
        using var recto = FormAssets.CrearFormulario();
        using var girado = FormAssets.CrearFormulario(rotacion);

        var a = _servicio.Read(recto).Single(c => c.Name == "nombre").Widgets[0].Bounds;
        var b = _servicio.Read(girado).Single(c => c.Name == "nombre").Widgets[0].Bounds;

        // Lo que medía 200pt de ancho pasa a medir 200pt de alto. Comparado en puntos,
        // porque las proporciones normalizadas van contra dimensiones distintas.
        Assert.Equal(a.Width * 595, b.Height * 595, 1);
        Assert.Equal(a.Height * 842, b.Width * 842, 1);
    }

    [Fact]
    public void Todos_los_campos_apuntan_a_la_primera_pagina()
    {
        using var pdf = FormAssets.CrearFormulario();

        foreach (var campo in _servicio.Read(pdf))
            Assert.All(campo.Widgets, w => Assert.Equal(0, w.PageIndex));
    }

    // =========================================================== robustez

    [Fact]
    public void Un_archivo_que_no_es_pdf_avisa_en_espanol()
    {
        var ex = Assert.Throws<PdfSignerException>(() =>
            _servicio.Read(new MemoryStream("no soy un pdf"u8.ToArray())));

        Assert.Contains("no parece un PDF válido", ex.Message);
    }

    [Fact]
    public void Construir_el_servicio_deja_listas_las_fuentes()
    {
        // Leer un formulario necesita resolver de fuentes: PDFsharp crea un XFont dentro del
        // constructor de cada campo de texto. Sin esto, enumerar los campos de un documento
        // lanza «No appropriate font found» en cuanto la biblioteca se usa por su cuenta.
        _ = new PdfFormService();

        Assert.NotNull(PdfSharp.Fonts.GlobalFontSettings.FontResolver);
    }

    [Fact]
    public void Leer_no_modifica_el_documento()
    {
        using var pdf = FormAssets.CrearFormulario();
        var antes = pdf.ToArray();

        _servicio.Read(pdf);
        _servicio.Read(pdf);

        Assert.Equal(antes, pdf.ToArray());
    }
}
