namespace PdfSigner.Core.Tests;

/// <summary>
/// Pruebas del arrastre de esquinas y del desplazamiento de elementos.
/// </summary>
/// <remarks>
/// Esta aritmética vivía en el proyecto de interfaz, donde no hay pruebas automáticas. Es el
/// tipo de cálculo que falla en silencio: el rectángulo se invierte y el elemento salta al
/// lado contrario del cursor, sin error ni aviso. Por eso se trajo al núcleo.
///
/// Los incrementos aquí van en proporción de página, no en píxeles: la conversión la hace la
/// interfaz, que sí sabe a qué tamaño se está dibujando.
/// </remarks>
public class RedimensionadoTests
{
    private static readonly NormalizedRect Centro = new(0.3, 0.3, 0.4, 0.4);

    // ============================================ arrastre de cada esquina

    [Fact]
    public void La_esquina_inferior_derecha_deja_fija_la_superior_izquierda()
    {
        var r = Centro.Resized(ResizeCorner.BottomRight, 0.1, 0.1);

        Assert.Equal(0.3, r.X, 6);
        Assert.Equal(0.3, r.Y, 6);
        Assert.Equal(0.5, r.Width, 6);
        Assert.Equal(0.5, r.Height, 6);
    }

    [Fact]
    public void La_esquina_superior_izquierda_deja_fija_la_inferior_derecha()
    {
        var r = Centro.Resized(ResizeCorner.TopLeft, 0.1, 0.1);

        // El borde derecho estaba en 0.7 y ahí debe seguir; lo mismo el inferior.
        Assert.Equal(0.4, r.X, 6);
        Assert.Equal(0.4, r.Y, 6);
        Assert.Equal(0.7, r.X + r.Width, 6);
        Assert.Equal(0.7, r.Y + r.Height, 6);
    }

    [Fact]
    public void La_esquina_superior_derecha_deja_fijos_el_borde_izquierdo_y_el_inferior()
    {
        var r = Centro.Resized(ResizeCorner.TopRight, 0.1, 0.1);

        Assert.Equal(0.3, r.X, 6);
        Assert.Equal(0.4, r.Y, 6);
        Assert.Equal(0.5, r.Width, 6);
        Assert.Equal(0.7, r.Y + r.Height, 6);
    }

    [Fact]
    public void La_esquina_inferior_izquierda_deja_fijos_el_borde_derecho_y_el_superior()
    {
        var r = Centro.Resized(ResizeCorner.BottomLeft, 0.1, 0.1);

        Assert.Equal(0.4, r.X, 6);
        Assert.Equal(0.3, r.Y, 6);
        Assert.Equal(0.7, r.X + r.Width, 6);
        Assert.Equal(0.5, r.Height, 6);
    }

    [Theory]
    [InlineData(ResizeCorner.TopLeft)]
    [InlineData(ResizeCorner.TopRight)]
    [InlineData(ResizeCorner.BottomLeft)]
    [InlineData(ResizeCorner.BottomRight)]
    public void Un_arrastre_nulo_no_cambia_nada(ResizeCorner esquina)
        => Assert.Equal(Centro, Centro.Resized(esquina, 0, 0));

    // ================================================ tamaño mínimo

    [Theory]
    [InlineData(ResizeCorner.TopLeft)]
    [InlineData(ResizeCorner.TopRight)]
    [InlineData(ResizeCorner.BottomLeft)]
    [InlineData(ResizeCorner.BottomRight)]
    public void El_rectangulo_nunca_se_invierte(ResizeCorner esquina)
    {
        // Arrastre desmedido en ambos sentidos: el rectángulo debe quedarse en el mínimo,
        // no darse la vuelta. Si se invirtiera, el elemento saltaría al lado contrario.
        foreach (var delta in new[] { 5.0, -5.0 })
        {
            var r = Centro.Resized(esquina, delta, delta, minimo: 0.02);

            Assert.True(r.Width >= 0.02 - 1e-9, $"Ancho degenerado: {r.Width}");
            Assert.True(r.Height >= 0.02 - 1e-9, $"Alto degenerado: {r.Height}");
        }
    }

    [Fact]
    public void Al_llegar_al_minimo_por_la_izquierda_el_borde_derecho_no_se_mueve()
    {
        var r = Centro.Resized(ResizeCorner.TopLeft, 0.9, 0, minimo: 0.05);

        // Se empuja el borde izquierdo muy por delante del derecho: debe detenerse a 0.05
        // del derecho, que sigue en 0.7.
        Assert.Equal(0.05, r.Width, 6);
        Assert.Equal(0.65, r.X, 6);
    }

    [Fact]
    public void Al_llegar_al_minimo_por_arriba_el_borde_inferior_no_se_mueve()
    {
        var r = Centro.Resized(ResizeCorner.TopRight, 0, 0.9, minimo: 0.05);

        Assert.Equal(0.05, r.Height, 6);
        Assert.Equal(0.65, r.Y, 6);
    }

    // ============================================ límites de la página

    [Theory]
    [InlineData(ResizeCorner.TopLeft)]
    [InlineData(ResizeCorner.TopRight)]
    [InlineData(ResizeCorner.BottomLeft)]
    [InlineData(ResizeCorner.BottomRight)]
    public void El_resultado_siempre_cabe_en_la_pagina(ResizeCorner esquina)
    {
        foreach (var delta in new[] { -3.0, -0.5, 0.5, 3.0 })
        {
            var r = Centro.Resized(esquina, delta, delta);

            Assert.InRange(r.X, 0, 1);
            Assert.InRange(r.Y, 0, 1);
            Assert.True(r.X + r.Width <= 1.000001, $"Se sale por la derecha: {r.X + r.Width}");
            Assert.True(r.Y + r.Height <= 1.000001, $"Se sale por abajo: {r.Y + r.Height}");
        }
    }

    // ==================================================== desplazamiento

    [Fact]
    public void Mover_desplaza_conservando_el_tamano()
    {
        var r = Centro.Moved(0.1, -0.1);

        Assert.Equal(0.4, r.X, 6);
        Assert.Equal(0.2, r.Y, 6);
        Assert.Equal(Centro.Width, r.Width, 6);
        Assert.Equal(Centro.Height, r.Height, 6);
    }

    [Fact]
    public void Mover_contra_un_borde_detiene_el_elemento_sin_encogerlo()
    {
        var r = Centro.Moved(-5, -5);

        Assert.Equal(0, r.X, 6);
        Assert.Equal(0, r.Y, 6);
        Assert.Equal(Centro.Width, r.Width, 6);
        Assert.Equal(Centro.Height, r.Height, 6);
    }

    [Fact]
    public void Mover_y_volver_deja_el_elemento_donde_estaba()
    {
        var ida = Centro.Moved(0.2, 0.15);
        var vuelta = ida.Moved(-0.2, -0.15);

        Assert.Equal(Centro.X, vuelta.X, 6);
        Assert.Equal(Centro.Y, vuelta.Y, 6);
    }
}
