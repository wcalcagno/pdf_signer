using System.Text.Json;
using PdfSigner.Core;

namespace PdfSigner.App.Services;

/// <summary>Guarda y recupera la firma favorita en el dispositivo.</summary>
/// <remarks>
/// Todo es local, sin backend: los metadatos van en Preferences (que en cada plataforma acaba
/// en su almacén nativo) y la imagen como archivo en el directorio de datos de la app. Separar
/// ambos evita meter un PNG en base64 dentro de las preferencias.
/// </remarks>
public sealed class FavoriteSignatureStore
{
    private const string PreferenceKey = "firma_favorita";
    private const string ImageFileName = "firma-favorita.png";

    public FavoriteSignature? Load()
    {
        var json = Preferences.Default.Get<string?>(PreferenceKey, null);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var favorite = JsonSerializer.Deserialize<FavoriteSignature>(json);

            // Si el archivo de imagen desapareció (limpieza de caché, restauración de copia),
            // se devuelve la firma sin imagen en vez de fallar al usarla más adelante.
            if (favorite?.HasImage == true && !File.Exists(ImagePath))
                favorite.ImageFileName = null;

            return favorite;
        }
        catch (JsonException)
        {
            // Preferencia corrupta o de una versión anterior: se descarta en silencio, porque
            // impedir que la app arranque por una firma guardada sería desproporcionado.
            return null;
        }
    }

    public async Task SaveAsync(FavoriteSignature favorite, byte[]? imageData)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        if (imageData is { Length: > 0 })
        {
            await File.WriteAllBytesAsync(ImagePath, imageData).ConfigureAwait(false);
            favorite.ImageFileName = ImageFileName;
        }

        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(favorite));
    }

    public async Task<byte[]?> LoadImageAsync()
    {
        if (!File.Exists(ImagePath))
            return null;

        return await File.ReadAllBytesAsync(ImagePath).ConfigureAwait(false);
    }

    public void Clear()
    {
        Preferences.Default.Remove(PreferenceKey);

        try
        {
            if (File.Exists(ImagePath))
                File.Delete(ImagePath);
        }
        catch (IOException)
        {
            // Que no se pueda borrar la imagen no debe impedir olvidar la firma.
        }
    }

    private static string ImagePath => Path.Combine(FileSystem.AppDataDirectory, ImageFileName);
}
