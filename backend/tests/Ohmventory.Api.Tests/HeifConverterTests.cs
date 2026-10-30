using ImageMagick;
using Ohmventory.Api.Storage;

namespace Ohmventory.Api.Tests;

public class HeifConverterTests
{
    // 64×48 px, links rot, rechts blau, mit libheif (heif-enc) erzeugt wie ein iPhone-Foto.
    private static readonly byte[] Sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));

    [Fact]
    public void RecognizesHeifByContent() => Assert.True(HeifConverter.IsHeif(Sample));

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1 })]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D })]
    [InlineData(new byte[] { 0, 0, 0, 0x1C, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0, (byte)'m', (byte)'i', (byte)'f', (byte)'1', (byte)'a', (byte)'v', (byte)'i', (byte)'f', (byte)'m', (byte)'i', (byte)'a', (byte)'f' })]
    [InlineData(new byte[] { 0, 0, 0 })]
    public void IgnoresJpegPngAvifAndShortData(byte[] data) => Assert.False(HeifConverter.IsHeif(data));

    [Fact]
    public void RecognizesMif1AmongCompatibleBrands()
    {
        byte[] data = [0, 0, 0, 0x18, .."ftyp"u8, .."xxxx"u8, 0, 0, 0, 0, .."mif1"u8, .."miaf"u8];

        Assert.True(HeifConverter.IsHeif(data));
    }

    [Fact]
    public void ConvertsToJpegKeepingSizeAndColours()
    {
        var jpeg = HeifConverter.ToJpeg(Sample);

        using var image = new MagickImage(jpeg);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal(64u, image.Width);
        Assert.Equal(48u, image.Height);
        using var pixels = image.GetPixels();
        var left = pixels.GetPixel(8, 24).ToColor()!;
        var right = pixels.GetPixel(56, 24).ToColor()!;
        Assert.True(left.R > 200 && left.B < 60, $"links rot erwartet, war {left}");
        Assert.True(right.B > 200 && right.R < 60, $"rechts blau erwartet, war {right}");
    }

    [Fact]
    public void ShrinksToMaxSize()
    {
        using var image = new MagickImage(HeifConverter.ToJpeg(Sample, maxSize: 32));

        Assert.Equal(32u, image.Width);
        Assert.Equal(24u, image.Height);
    }

    [Fact]
    public void BrokenHeifThrowsMagickException() =>
        Assert.ThrowsAny<MagickException>(() => HeifConverter.ToJpeg(Sample[..64]));
}
