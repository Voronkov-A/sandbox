using Picshare.Services;
using SkiaSharp;

namespace Picshare.Tests;

public sealed class AiPhotoImageEncoderTests
{
    [Theory]
    [InlineData(2048, 1024, 0, 512, 256)]
    [InlineData(1024, 2048, 0, 256, 512)]
    [InlineData(2048, 1024, 90, 256, 512)]
    [InlineData(80, 40, 0, 80, 40)]
    [InlineData(80, 40, 270, 40, 80)]
    public void PreservesAspectRatio_ClampsLongestDimension_AndDoesNotEnlarge(int width, int height, int rotation, int expectedWidth, int expectedHeight)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var source = new MemoryStream(png.ToArray());
        var result = AiPhotoImageEncoder.Encode(source, 512, rotation, default);
        Assert.True(source.CanRead);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(expectedWidth, decoded.Width);
        Assert.Equal(expectedHeight, decoded.Height);
        Assert.Equal(0xff, result[0]);
        Assert.Equal(0xd8, result[1]);
    }

    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(2, false, true, false)]
    [InlineData(3, false, true, true)]
    [InlineData(4, false, false, true)]
    [InlineData(5, true, false, false)]
    [InlineData(6, true, true, false)]
    [InlineData(7, true, true, true)]
    [InlineData(8, true, false, true)]
    public void AppliesExifOrientation_AndRemovesOriginalExif(int orientation, bool swap, bool flipX, bool flipY)
    {
        using var bitmap = new SKBitmap(80, 40);
        bitmap.Erase(SKColors.Blue);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red })
            canvas.DrawRect(0, 0, 40, 20, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        // APP1: Exif\0\0, little-endian TIFF header, one SHORT orientation tag.
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        var original = jpeg.ToArray();
        using var source = new MemoryStream(original.Take(2).Concat(exif).Concat(original.Skip(2)).ToArray());
        var result = AiPhotoImageEncoder.Encode(source, 512, 0, default);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(swap ? 40 : 80, decoded.Width);
        Assert.Equal(swap ? 80 : 40, decoded.Height);
        var corner = decoded.GetPixel(flipX ? decoded.Width - 5 : 5, flipY ? decoded.Height - 5 : 5);
        Assert.True(corner.Red > 200 && corner.Blue < 60, $"Orientation {orientation}: {corner}");
        using var output = SKCodec.Create(new MemoryStream(result));
        Assert.Equal(SKEncodedOrigin.TopLeft, output.EncodedOrigin);
    }

    [Fact]
    public void CancellationAndInvalidImages_AreRejected()
    {
        using var source = new MemoryStream([1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => AiPhotoImageEncoder.Encode(source, 512, 0, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AiPhotoImageEncoder.Encode(source, 512, 0, cancellation.Token));
    }
}
