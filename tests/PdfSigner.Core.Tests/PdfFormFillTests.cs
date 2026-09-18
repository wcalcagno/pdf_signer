using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfSigner.Core.Tests;

/// <summary>
/// Relleno de los cinco tipos de campo.
/// </summary>
/// <remarks>
/// Estas pruebas miran DOS cosas por cada campo: el valor (<c>/V</c>) y lo que el visor
/// dibuja (<c>/AS</c> en los botones, <c>/AP /N</c> en el resto). Están separadas a propósito
/// porque el fallo típico es escribir bien el valor y dejar el campo visualmente vacío, que
/// es justo lo que hacen los asignadores de PDFsharp y lo que ninguna prueba que solo lea el
/// valor llegaría a detectar.
/// </remarks>
public class PdfFormFillTests
{
    private readonly PdfFormService _servicio = new();

    private MemoryStream Rellenar(params (string Campo, string? Valor)[] valores) =>
        Rellenar(FormAssets.CrearFormulario(), valores);

    private MemoryStream Rellenar(
        MemoryStream origen, params (string Campo, string? Valor)[] valores)
    {
        using (origen)
        {
            var salida = new MemoryStream();
            _servicio.Fill(
                origen, salida,
                valores.ToDictionary(v => v.Campo, v => v.Valor),
                compress: false);
            salida.Position = 0;
            return salida;
        }
    }

    // ================================================================ texto

    [Fact]
    public void Escribe_el_texto_de_un_campo()
    {
        using var pdf = Rellenar(("nombre", "Walter Calcagno"));

        Assert.Equal("Walter Calcagno", Campo(pdf, "nombre").Value);
        Assert.Contains("(Walter Calcagno) Tj", Apariencia(pdf, "nombre"));
    }

    [Fact]
    public void Conserva_los_acentos()
    {
        using var pdf = Rellenar(("nombre", "Mañana temprano"));

        Assert.Equal("Mañana temprano", Campo(pdf, "nombre").Value);
        Assert.Contains("(Mañana temprano) Tj", Apariencia(pdf, "nombre"));
    }

    [Fact]
    public void Recorta_el_texto_al_maximo_que_admite_el_campo()
    {
        // El campo declara /MaxLen 60: un visor no dejaría teclear más.
        using var pdf = Rellenar(("nombre", new string('a', 100)));

        Assert.Equal(60, Campo(pdf, "nombre").Value!.Length);
    }

    [Fact]
    public void Un_campo_multilinea_dibuja_una_linea_por_renglon()
    {
        using var pdf = Rellenar(("notas", "Primera\nSegunda"));
        var ap = Apariencia(pdf, "notas");

        Assert.Contains("(Primera) Tj", ap);
        Assert.Contains("T* (Segunda) Tj", ap);
    }

    [Fact]
    public void Un_campo_de_una_sola_linea_no_se_parte()
    {
        using var pdf = Rellenar(("nombre", "Primera\nSegunda"));

        Assert.DoesNotContain("T*", Apariencia(pdf, "nombre"));
        Assert.Contains("(Primera Segunda) Tj", Apariencia(pdf, "nombre"));
    }

    [Fact]
    public void Vaciar_un_campo_borra_el_valor_y_lo_que_se_veia()
    {
        using var lleno = Rellenar(("nombre", "Walter"));
        using var vacio = Rellenar(lleno, ("nombre", ""));

        Assert.Null(Campo(vacio, "nombre").Value);
        Assert.DoesNotContain("Tj", Apariencia(vacio, "nombre"));
    }

    // ============================================================= casillas

    [Theory]
    [InlineData("true")]
    [InlineData("Yes")]
    [InlineData("1")]
    public void Marca_una_casilla(string valor)
    {
        using var pdf = Rellenar(("acepto", valor));

        Assert.Equal("/Yes", Crudo(pdf, "acepto").Elements["/V"]?.ToString());
        Assert.Equal("/Yes", Crudo(pdf, "acepto").Elements["/AS"]?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Off")]
    [InlineData("false")]
    public void Desmarca_una_casilla(string valor)
    {
        using var marcado = Rellenar(("acepto", "true"));
        using var pdf = Rellenar(marcado, ("acepto", valor));

        Assert.Equal("/Off", Crudo(pdf, "acepto").Elements["/V"]?.ToString());
        Assert.Equal("/Off", Crudo(pdf, "acepto").Elements["/AS"]?.ToString());
    }

    // ================================================================ radio

    [Fact]
    public void Marca_la_opcion_elegida_de_un_grupo_de_radio()
    {
        using var pdf = Rellenar(("respuesta", "no"));

        Assert.Equal("/no", Crudo(pdf, "respuesta").Elements["/V"]?.ToString());
    }

    [Fact]
    public void El_widget_elegido_es_el_unico_que_se_dibuja_encendido()
    {
        // Esta es LA prueba del grupo de radio. SelectedIndex de PDFsharp deja el valor sin
        // tocar y todos los widgets en /Off sin lanzar ningún error: el formulario sale con
        // la pregunta en blanco y no hay nada que lo delate salvo mirar el /AS de cada zona.
        using var pdf = Rellenar(("respuesta", "no"));

        Assert.Equal(["/Off", "/no"], Kids(pdf, "respuesta").Select(k => k.Elements["/AS"]?.ToString()));
    }

    [Fact]
    public void Cambiar_de_opcion_apaga_la_anterior()
    {
        using var primero = Rellenar(("respuesta", "si"));
        Assert.Equal(["/si", "/Off"], Kids(primero, "respuesta").Select(k => k.Elements["/AS"]?.ToString()));

        using var segundo = Rellenar(primero, ("respuesta", "no"));
        Assert.Equal(["/Off", "/no"], Kids(segundo, "respuesta").Select(k => k.Elements["/AS"]?.ToString()));
    }

    [Fact]
    public void Se_puede_dejar_un_grupo_de_radio_sin_responder()
    {
        using var respondido = Rellenar(("respuesta", "si"));
        using var pdf = Rellenar(respondido, ("respuesta", null));

        Assert.Equal("/Off", Crudo(pdf, "respuesta").Elements["/V"]?.ToString());
        Assert.All(Kids(pdf, "respuesta"), k => Assert.Equal("/Off", k.Elements["/AS"]?.ToString()));
    }

    // ================================================ desplegables y listas

    [Fact]
    public void Un_desplegable_guarda_el_valor_limpio()
    {
        // SelectedIndex de PDFsharp guarda la representación del elemento del array /Opt,
        // paréntesis incluidos, y el campo acaba valiendo el texto «(Verde)».
        using var pdf = Rellenar(("color", "Verde"));

        Assert.Equal("Verde", Campo(pdf, "color").Value);
        Assert.Contains("(Verde) Tj", Apariencia(pdf, "color"));
    }

    [Fact]
    public void Una_lista_guarda_el_valor_limpio()
    {
        using var pdf = Rellenar(("turno", "Tarde"));

        Assert.Equal("Tarde", Campo(pdf, "turno").Value);
        Assert.Contains("(Tarde) Tj", Apariencia(pdf, "turno"));
    }

    [Fact]
    public void Apunta_el_indice_de_la_opcion_elegida()
    {
        using var pdf = Rellenar(("color", "Rojo"));
        var i = Crudo(pdf, "color").Elements.GetArray("/I");

        Assert.NotNull(i);
        Assert.Equal(2, i!.Elements.GetInteger(0));
    }

    [Fact]
    public void Una_opcion_que_no_existe_avisa_en_vez_de_colarse()
    {
        var ex = Assert.Throws<PdfSignerException>(() => Rellenar(("color", "Morado")));
        Assert.Contains("no es una de las opciones", ex.Message);
    }

    [Fact]
    public void Una_respuesta_de_radio_que_no_existe_avisa()
    {
        var ex = Assert.Throws<PdfSignerException>(() => Rellenar(("respuesta", "quizas")));
        Assert.Contains("no admite el valor", ex.Message);
    }

    // ============================================================= robustez

    [Fact]
    public void Un_campo_de_solo_lectura_se_deja_como_estaba()
    {
        // Se admite recibirlo para poder devolver el mapa completo que dio Read sin filtrar.
        using var pdf = Rellenar(("bloqueado", "deberia ignorarse"), ("nombre", "Walter"));

        Assert.Null(Campo(pdf, "bloqueado").Value);
        Assert.Equal("Walter", Campo(pdf, "nombre").Value);
    }

    [Fact]
    public void Un_campo_que_no_existe_avisa_en_espanol()
    {
        var ex = Assert.Throws<PdfSignerException>(() => Rellenar(("inventado", "x")));
        Assert.Contains("no tiene ningún campo llamado", ex.Message);
    }

    [Fact]
    public void Rellenar_un_pdf_sin_formulario_avisa()
    {
        var ex = Assert.Throws<PdfSignerException>(
            () => Rellenar(FormAssets.CrearSinFormulario(), ("nombre", "Walter")));

        Assert.Contains("no tiene campos de formulario", ex.Message);
    }

    [Fact]
    public void Sin_valores_que_escribir_el_documento_pasa_igual()
    {
        using var pdf = Rellenar(FormAssets.CrearSinFormulario());
        Assert.NotEmpty(pdf.ToArray());
    }

    [Fact]
    public void Rellenar_no_mueve_los_campos_de_sitio()
    {
        using var original = FormAssets.CrearFormulario();
        var antes = _servicio.Read(original).ToDictionary(c => c.Name, c => c.Widgets[0].Bounds);

        original.Position = 0;
        using var relleno = Rellenar(original, ("nombre", "Walter"), ("respuesta", "si"));
        var despues = _servicio.Read(relleno).ToDictionary(c => c.Name, c => c.Widgets[0].Bounds);

        Assert.Equal(antes.Keys.Order(), despues.Keys.Order());
        foreach (var (nombre, caja) in antes)
        {
            Assert.Equal(caja.X, despues[nombre].X, 4);
            Assert.Equal(caja.Y, despues[nombre].Y, 4);
        }
    }

    [Fact]
    public void Los_campos_siguen_siendo_editables_despues_de_rellenar()
    {
        // Rellenar no bloquea nada: fijar los valores es cosa del aplanado, que va aparte.
        using var pdf = Rellenar(("nombre", "Walter"));

        Assert.True(Campo(pdf, "nombre").IsEditable);
        Assert.True(new PdfFormService().HasForm(pdf));
    }

    [Fact]
    public void Se_puede_firmar_un_formulario_ya_relleno()
    {
        using var relleno = Rellenar(("nombre", "Walter"), ("respuesta", "si"));

        var firmado = new MemoryStream();
        new PdfSignatureService().Sign(relleno, firmado, [
            new TextElement
            {
                PageIndex = 0,
                Bounds = new NormalizedRect(0.1, 0.8, 0.4, 0.05),
                Text = "Firmado",
            },
        ], compress: false);

        firmado.Position = 0;
        Assert.Equal("Walter", Campo(firmado, "nombre").Value);
        Assert.Equal("/si", Crudo(firmado, "respuesta").Elements["/V"]?.ToString());
    }

    // ---------------------------------------------------------- utilidades

    private PdfFormField Campo(MemoryStream pdf, string nombre)
    {
        pdf.Position = 0;
        return _servicio.Read(pdf).Single(c => c.Name == nombre);
    }

    /// <summary>Diccionario crudo del campo, para mirar lo que ningún modelo expone.</summary>
    private static PdfDictionary Crudo(MemoryStream pdf, string nombre)
    {
        pdf.Position = 0;
        var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
        return doc.AcroForm.Fields[nombre]!;
    }

    private static IReadOnlyList<PdfDictionary> Kids(MemoryStream pdf, string nombre)
    {
        var kids = Crudo(pdf, nombre).Elements.GetArray("/Kids")!;
        return [.. Enumerable.Range(0, kids.Elements.Count)
            .Select(i => (PdfDictionary)((PdfReference)kids.Elements[i]).Value)];
    }

    /// <summary>Flujo de apariencia del campo: lo que el visor dibuja de verdad.</summary>
    private static string Apariencia(MemoryStream pdf, string nombre)
    {
        var n = Crudo(pdf, nombre).Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        Assert.NotNull(n);
        Assert.NotNull(n!.Stream);
        return Encoding.Latin1.GetString(n.Stream.UnfilteredValue);
    }
}
