using SkiaSharp;

namespace Picshare.Services;

public static class PhotoThumbnailGenerator
{
    public const int ThumbnailSize = 64;
    public const string ContentType = "image/jpeg";

    public static MemoryStream CreateJpegThumbnail(Stream source)
    {
        using var codec = SKCodec.Create(source)
            ?? throw new InvalidOperationException("The selected photo could not be decoded.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new InvalidOperationException("The selected photo has invalid dimensions.");
        }

        using var original = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("The selected photo could not be decoded.");

        using var surface = SKSurface.Create(new SKImageInfo(ThumbnailSize, ThumbnailSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var sourceRect = GetCenteredSquareCrop(original.Width, original.Height);
        var destinationRect = new SKRect(0, 0, ThumbnailSize, ThumbnailSize);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.Medium,
            IsAntialias = true
        };

        canvas.DrawBitmap(original, sourceRect, destinationRect, paint);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82)
            ?? throw new InvalidOperationException("The selected photo thumbnail could not be encoded.");

        var thumbnail = new MemoryStream();
        data.SaveTo(thumbnail);
        thumbnail.Position = 0;
        return thumbnail;
    }

    private static SKRect GetCenteredSquareCrop(int width, int height)
    {
        var size = Math.Min(width, height);
        var left = (width - size) / 2f;
        var top = (height - size) / 2f;
        return new SKRect(left, top, left + size, top + size);
    }
}
