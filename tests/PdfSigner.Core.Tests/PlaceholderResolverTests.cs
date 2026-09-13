namespace PdfSigner.Core.Tests;

public class PlaceholderResolverTests
{
    private static readonly DateTime Momento = new(2026, 9, 13, 14, 30, 0);

    [Fact]
    public void Sustituye_fecha()
        => Assert.Equal("Firmado el 13/09/2026",
            PlaceholderResolver.Resolve("Firmado el {fecha}", Momento));

    [Fact]
    public void Sustituye_hora()
        => Assert.Equal("a las 14:30", PlaceholderResolver.Resolve("a las {hora}", Momento));

    [Fact]
    public void Sustituye_fecha_larga_en_espanol()
        => Assert.Equal("13 de septiembre de 2026",
            PlaceholderResolver.Resolve("{fechalarga}", Momento));

    [Fact]
    public void Ignora_mayusculas_y_minusculas()
        => Assert.Equal("13/09/2026", PlaceholderResolver.Resolve("{FECHA}", Momento));

    [Fact]
    public void Sustituye_todas_las_apariciones()
        => Assert.Equal("13/09/2026 y 13/09/2026",
            PlaceholderResolver.Resolve("{fecha} y {fecha}", Momento));

    [Fact]
    public void Deja_intacto_un_texto_sin_marcadores()
        => Assert.Equal("Walter E. Calcagno Lucares",
            PlaceholderResolver.Resolve("Walter E. Calcagno Lucares", Momento));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Tolera_texto_vacio(string? texto)
        => Assert.Equal(texto, PlaceholderResolver.Resolve(texto!, Momento));
}
