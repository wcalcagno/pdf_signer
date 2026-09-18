using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Aplanado: convertir los campos en contenido fijo de la página.
/// </summary>
/// <remarks>
/// Lo que hay que demostrar no es que el formulario desaparezca —eso es fácil y es también lo
/// que pasa si se borran los campos sin más— sino que lo que se veía SIGUE viéndose. Por eso
/// las pruebas recorren la cadena entera: el contenido de la página nombra un XObject, ese
/// XObject está en los recursos de la página, y su flujo contiene el texto del campo.
/// </remarks>
public class PdfFormFlattenTests
{
    private readonly PdfFormService _servicio = new();

    private MemoryStream Aplanar(MemoryStream origen, params (string, string?)[] valores)
    {
        using (origen)
        {
            var salida = new MemoryStream();
            _servicio.Fill(
                origen, salida,
                valores.ToDictionary(v => v.Item1, v => v.Item2),
                flatten: true, compress: false);
            salida.Position = 0;
            return salida;
        }
    }

    // ================================================ ya no hay formulario

    [Fact]
    public void Despues_de_aplanar_no_queda_formulario()
    {
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));

        Assert.False(_servicio.HasForm(pdf));
        Assert.Empty(_servicio.Read(pdf));
    }

    [Fact]
    public void No_quedan_widgets_en_la_pagina()
    {
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));

        Assert.DoesNotContain(Anotaciones(pdf), a => a.Elements.GetName("/Subtype") == "/Widget");
    }

    [Fact]
    public void Las_anotaciones_que_no_son_campos_se_respetan()
    {
        // Aplanar un formulario no es aplanar el documento: un enlace sigue siendo un enlace.
        using var pdf = Aplanar(FormAssets.CrearFormulario(conEnlace: true), ("nombre", "Walter"));

        Assert.Single(Anotaciones(pdf), a => a.Elements.GetName("/Subtype") == "/Link");
    }

    // ================================================== lo que se ve sigue

    [Fact]
    public void El_texto_del_campo_pasa_a_ser_contenido_de_la_pagina()
    {
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter Calcagno"));

        Assert.Contains("(Walter Calcagno) Tj", string.Join("\n", Estampados(pdf)));
    }

    [Fact]
    public void Se_estampa_un_campo_de_cada_tipo()
    {
        using var pdf = Aplanar(
            FormAssets.CrearFormulario(),
            ("nombre", "Walter"), ("acepto", "true"), ("respuesta", "no"),
            ("color", "Verde"), ("turno", "Tarde"));

        var todo = string.Join("\n", Estampados(pdf));

        Assert.Contains("(Walter) Tj", todo);
        Assert.Contains("(Verde) Tj", todo);
        Assert.Contains("(Tarde) Tj", todo);
        // La casilla marcada dibuja su palomita; el radio elegido, su punto relleno.
        Assert.Contains("4 9 m 8 5 l 14 14 l S", todo);
        Assert.Contains("5 5 8 8 re f", todo);
    }

    [Fact]
    public void De_un_grupo_de_radio_se_estampa_el_estado_de_cada_opcion()
    {
        // Las dos zonas se dibujan: la elegida con su punto y la otra con el círculo vacío.
        // Que se estampe la apariencia correcta depende del /AS que se escribió al rellenar.
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("respuesta", "si"));
        var estampados = Estampados(pdf);

        Assert.Equal(1, estampados.Count(e => e.Contains("5 5 8 8 re f")));
        Assert.True(estampados.Count(e => e.Contains("re S") && !e.Contains("re f")) >= 1);
    }

    [Fact]
    public void Un_campo_marcado_como_oculto_no_se_dibuja()
    {
        using var pdf = Aplanar(
            FormAssets.CrearFormulario(conCampoOculto: true), ("nombre", "Walter"));

        Assert.DoesNotContain("no deberia verse", string.Join("\n", Estampados(pdf)));
    }

    [Fact]
    public void El_contenido_que_ya_tenia_la_pagina_se_conserva()
    {
        using var original = FormAssets.CrearSinFormulario();
        var salida = new MemoryStream();
        _servicio.Flatten(original, salida, compress: false);
        salida.Position = 0;

        Assert.Contains("Sin formulario", Texto(Contenido(salida)));
    }

    // =========================================================== posición

    [Fact]
    public void El_estampado_cae_justo_donde_estaba_el_campo()
    {
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));

        // El campo ocupa [100 700 300 720] y su apariencia mide exactamente eso, así que la
        // matriz es una traslación limpia a la esquina inferior izquierda del /Rect.
        Assert.Contains(Matrices(pdf), m => m is [1, 0, 0, 1, 100, 700]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void La_rotacion_de_la_pagina_no_mueve_el_estampado(int rotacion)
    {
        // Al revés que al firmar, aquí NO hay que compensar el /Rotate: el /Rect del campo y
        // el flujo de contenido viven en el mismo espacio, y el visor gira la página entera
        // después. Compensar aquí descolocaría el estampado en los documentos girados.
        using var pdf = Aplanar(FormAssets.CrearFormulario(rotacion), ("nombre", "Walter"));

        Assert.Contains(Matrices(pdf), m => m is [1, 0, 0, 1, 100, 700]);
    }

    [Fact]
    public void Un_MediaBox_desplazado_se_respeta_tal_cual()
    {
        // Las coordenadas del campo ya vienen desplazadas con el MediaBox, así que se copian
        // sin tocar: restar el origen dejaría el estampado fuera de sitio.
        using var pdf = Aplanar(
            FormAssets.CrearFormulario(origenX: 20, origenY: 30), ("nombre", "Walter"));

        Assert.Contains(Matrices(pdf), m => m is [1, 0, 0, 1, 120, 730]);
    }

    // =========================================================== robustez

    [Fact]
    public void Los_recursos_heredados_del_padre_no_se_pierden()
    {
        // /Resources es heredable. Crear unos vacíos en la página los escondería y el
        // contenido original se quedaría sin sus fuentes, sin ningún error que lo avisara.
        using var pdf = Aplanar(
            FormAssets.CrearFormulario(recursosEnElPadre: true), ("nombre", "Walter"));

        pdf.Position = 0;
        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
        var fuentes = doc.Pages[0].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");

        Assert.NotNull(fuentes);
        Assert.True(fuentes!.Elements.ContainsKey("/Marca"));
    }

    [Fact]
    public void El_contenido_original_queda_envuelto_para_que_el_estampado_no_lo_herede()
    {
        // Si el documento acabara con una transformación abierta, el estampado la heredaría y
        // saldría desplazado. El q inicial y el Q antes de dibujar lo impiden.
        using var pdf = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));
        var contenido = Contenido(pdf);

        Assert.StartsWith("q", contenido.TrimStart());
        Assert.Matches(@"Q\s+q\s+[\d.\s]+cm", contenido);
    }

    [Fact]
    public void Aplanar_un_pdf_sin_formulario_lo_deja_como_estaba()
    {
        using var original = FormAssets.CrearSinFormulario();
        var antes = original.ToArray();

        var salida = new MemoryStream();
        _servicio.Flatten(original, salida, compress: false);
        salida.Position = 0;

        Assert.False(_servicio.HasForm(salida));
        Assert.Contains("Sin formulario", Texto(Contenido(salida)));
        Assert.NotEmpty(antes);
    }

    [Fact]
    public void Aplanar_dos_veces_no_duplica_nada()
    {
        using var una = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));

        var otra = new MemoryStream();
        _servicio.Flatten(una, otra, compress: false);
        otra.Position = 0;

        Assert.Single(Estampados(otra), e => e.Contains("(Walter) Tj"));
    }

    [Fact]
    public void Se_puede_firmar_un_documento_ya_aplanado()
    {
        using var aplanado = Aplanar(FormAssets.CrearFormulario(), ("nombre", "Walter"));

        var firmado = new MemoryStream();
        new PdfSignatureService().Sign(aplanado, firmado, [
            new TextElement
            {
                PageIndex = 0,
                Bounds = new NormalizedRect(0.1, 0.1, 0.4, 0.05),
                Text = "Firmado",
            },
        ], compress: false);

        firmado.Position = 0;
        Assert.False(_servicio.HasForm(firmado));
        Assert.Contains("(Walter) Tj", string.Join("\n", Estampados(firmado)));
    }

    [Fact]
    public void Rellenar_sin_aplanar_deja_los_campos_donde_estaban()
    {
        using var origen = FormAssets.CrearFormulario();
        var salida = new MemoryStream();
        _servicio.Fill(origen, salida, new Dictionary<string, string?> { ["nombre"] = "Walter" },
            flatten: false, compress: false);
        salida.Position = 0;

        Assert.True(_servicio.HasForm(salida));
        Assert.Contains(Anotaciones(salida), a => a.Elements.GetName("/Subtype") == "/Widget");
    }

    // --------------------------------------------------------- utilidades

    private static PdfDocument Abrir(MemoryStream pdf)
    {
        pdf.Position = 0;
        return PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
    }

    private static IReadOnlyList<PdfDictionary> Anotaciones(MemoryStream pdf)
    {
        var doc = Abrir(pdf);
        var annots = doc.Pages[0].Elements.GetArray("/Annots");
        if (annots is null)
            return [];

        return [.. Enumerable.Range(0, annots.Elements.Count)
            .Select(i => annots.Elements.GetDictionary(i))
            .Where(d => d is not null)!];
    }

    /// <summary>Todos los flujos de contenido de la primera página, concatenados.</summary>
    private static string Contenido(MemoryStream pdf)
    {
        var doc = Abrir(pdf);
        var contenidos = doc.Pages[0].Contents;
        var sb = new StringBuilder();

        for (var i = 0; i < contenidos.Elements.Count; i++)
        {
            if (contenidos.Elements.GetDictionary(i)?.Stream is { } s)
                sb.AppendLine(Encoding.Latin1.GetString(s.UnfilteredValue));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Flujos de los XObjects que el contenido de la página dibuja con <c>Do</c>.
    /// </summary>
    /// <remarks>
    /// Recorre la cadena completa a propósito: no basta con que el XObject exista en el
    /// documento, tiene que estar nombrado en los recursos de la página Y dibujado por su
    /// contenido. Si se rompe cualquiera de los tres eslabones, el campo desaparece de la
    /// vista y el documento sigue abriendo sin quejarse.
    /// </remarks>
    private static IReadOnlyList<string> Estampados(MemoryStream pdf)
    {
        var doc = Abrir(pdf);
        var page = doc.Pages[0];

        var xobjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        if (xobjects is null)
            return [];

        var contenido = new StringBuilder();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
        {
            if (page.Contents.Elements.GetDictionary(i)?.Stream is { } s)
                contenido.AppendLine(Encoding.Latin1.GetString(s.UnfilteredValue));
        }

        var flujos = new List<string>();
        foreach (Match m in Regex.Matches(contenido.ToString(), @"(/[A-Za-z0-9]+)\s+Do"))
        {
            if (xobjects.Elements.GetDictionary(m.Groups[1].Value)?.Stream is { } s)
                flujos.Add(Encoding.Latin1.GetString(s.UnfilteredValue));
        }

        return flujos;
    }

    /// <summary>Matrices de los <c>cm</c> que preceden a cada <c>Do</c>.</summary>
    private static IReadOnlyList<double[]> Matrices(MemoryStream pdf)
    {
        var contenido = Contenido(pdf);
        var matrices = new List<double[]>();

        foreach (Match m in Regex.Matches(
            contenido, @"([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) cm\s+/[A-Za-z0-9]+\s+Do"))
        {
            matrices.Add([.. m.Groups.Cast<Group>().Skip(1)
                .Select(g => double.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture))]);
        }

        return matrices;
    }

    /// <summary>Texto suelto de un flujo de contenido, para poder buscar en él.</summary>
    private static string Texto(string contenido) =>
        string.Join(" ", Regex.Matches(contenido, @"\(([^)]*)\)").Select(m => m.Groups[1].Value));
}
