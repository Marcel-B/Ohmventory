using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace Ohmventory.Api.Storage;

/// <summary>
/// iPhones fotografieren im HEIC-Format. Weder die meisten Browser noch das Vision-Modell können das lesen,
/// deshalb wandelt das Backend HEIC/HEIF in JPEG um. Magick.NET bringt libheif selbst mit, im Container ist nichts nachzuinstallieren.
/// </summary>
public static class HeifConverter
{
    public const int DefaultMaxSize = 1600;

    // Marken im ftyp-Kasten von HEIF-Dateien (ISO/IEC 23008-12). AVIF gehört nicht dazu.
    private static readonly HashSet<string> Brands = ["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs", "mif1", "msf1"];

    /// <summary>
    /// Erkennt HEIF am Inhalt. Browser schicken für .heic je nach System „image/heic“, „application/octet-stream“ oder nichts.
    /// </summary>
    public static bool IsHeif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data.Slice(4, 4).SequenceEqual("ftyp"u8))
            return false;
        var majorBrand = Encoding.ASCII.GetString(data.Slice(8, 4));
        if (Brands.Contains(majorBrand))
            return true;
        if (majorBrand.StartsWith("avi"))
            return false;
        // Manche Kameras tragen mif1 o. ä. nur unter den kompatiblen Marken ein (ab Byte 16, bis zum Ende des ftyp-Kastens).
        var boxEnd = (int)Math.Min(data.Length, BinaryPrimitives.ReadUInt32BigEndian(data));
        for (var i = 16; i + 4 <= boxEnd; i += 4)
            if (Brands.Contains(Encoding.ASCII.GetString(data.Slice(i, 4))))
                return true;
        return false;
    }

    /// <summary>
    /// Wandelt ein HEIF-Bild in JPEG um: richtig gedreht, auf <paramref name="maxSize"/> px Kantenlänge verkleinert,
    /// ohne Metadaten (also auch ohne GPS-Position aus dem Handy).
    /// </summary>
    public static byte[] ToJpeg(byte[] data, int maxSize = DefaultMaxSize, int quality = 88)
    {
        using var image = new MagickImage(data);
        image.AutoOrient();
        image.Resize(new MagickGeometry((uint)maxSize, (uint)maxSize) { Greater = true });
        image.Strip();
        image.Quality = (uint)quality;
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }
}
