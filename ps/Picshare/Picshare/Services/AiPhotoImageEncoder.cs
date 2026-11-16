using SkiaSharp;

namespace Picshare.Services;

public static class AiPhotoImageEncoder
{
    /// <summary>Re-encodes pixels only, preserving aspect ratio and displayed orientation, without metadata.</summary>
    public static byte[] Encode(Stream source, int maxDimension, int rotationDegrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new SKManagedStream(source, disposeManagedStream: false);
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("An AI input image could not be decoded.");
        var scale = Math.Min(1f, (float)maxDimension / Math.Max(codec.Info.Width, codec.Info.Height));
        var dimensions = codec.GetScaledDimensions(scale);
        using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(dimensions.Width, dimensions.Height))
            ?? throw new InvalidDataException("An AI input image could not be decoded.");
        cancellationToken.ThrowIfCancellationRequested();
        var width = bitmap.Width;
        var height = bitmap.Height;
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = swap ? height : width;
        var orientedHeight = swap ? width : height;
        var rotation = ((rotationDegrees % 360) + 360) % 360;
        var outputWidth = rotation is 90 or 270 ? orientedHeight : orientedWidth;
        var outputHeight = rotation is 90 or 270 ? orientedWidth : orientedHeight;
        var outputScale = Math.Min(1d, (double)maxDimension / Math.Max(outputWidth, outputHeight));
        var targetWidth = Math.Max(1, (int)Math.Round(outputWidth * outputScale));
        var targetHeight = Math.Max(1, (int)Math.Round(outputHeight * outputScale));
        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight))
            ?? throw new InvalidDataException("An AI input image could not be resized.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale((float)targetWidth / outputWidth, (float)targetHeight / outputHeight);
        switch (rotation)
        {
            case 90: canvas.Translate(orientedHeight, 0); canvas.RotateDegrees(90); break;
            case 180: canvas.Translate(orientedWidth, orientedHeight); canvas.RotateDegrees(180); break;
            case 270: canvas.Translate(0, orientedWidth); canvas.RotateDegrees(270); break;
        }
        var orientation = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
            _ => SKMatrix.Identity
        };
        canvas.Concat(ref orientation);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        canvas.DrawBitmap(bitmap, 0, 0, paint);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85)
            ?? throw new InvalidDataException("An AI input image could not be encoded.");
        cancellationToken.ThrowIfCancellationRequested();
        return encoded.ToArray();
    }

    private static SKMatrix Matrix(float xx, float xy, float x, float yx, float yy, float y) => new()
    {
        ScaleX = xx, SkewX = xy, TransX = x, SkewY = yx, ScaleY = yy, TransY = y, Persp2 = 1
    };
}
