using CotizadorInterno.Web.Models.CopiersMtoV2;
using SkiaSharp;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

internal static class CopiersPortableImageSanitizer
{
    internal static byte[] Reencode(byte[] content, string extension, CopiersMaintenanceV2Options options, bool isSignature)
    {
        try
        {
            using var data = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)
                || (isSignature && codec.EncodedFormat != SKEncodedImageFormat.Jpeg) || codec.FrameCount > 1)
                throw new CopiersMaintenanceV2ValidationException("image_content_invalid", "La imagen no tiene un formato JPG o PNG válido.");
            var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var width = swap ? codec.Info.Height : codec.Info.Width;
            var height = swap ? codec.Info.Width : codec.Info.Height;
            var maxDimension = isSignature ? options.MaxSignatureDimensionPixels : options.MaxAttachmentImageDimensionPixels;
            var maxPixels = isSignature ? checked((long)maxDimension * maxDimension) : options.MaxAttachmentImagePixels;
            // Check dimensions before allocating or decoding untrusted image pixels.
            if (width <= 0 || height <= 0 || width > maxDimension || height > maxDimension || (long)width * height > maxPixels)
                throw new CopiersMaintenanceV2ValidationException("image_dimensions_invalid", "Las dimensiones de la imagen no son seguras.");
            if (isSignature && (width < options.MinSignatureWidthPixels || height < options.MinSignatureHeightPixels))
                throw new CopiersMaintenanceV2ValidationException("signature_dimensions_invalid", "Las dimensiones de la firma no son válidas.");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var source = new SKBitmap(info);
            if (codec.GetPixels(info, source.GetPixels()) != SKCodecResult.Success)
                throw new CopiersMaintenanceV2ValidationException("image_decode_failed", "La imagen está dañada o no puede sanearse de forma segura.");
            using var oriented = Orient(source, codec.EncodedOrigin);
            using var clean = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(clean))
            {
                canvas.Clear(extension == ".png" ? SKColors.Transparent : SKColors.White);
                canvas.DrawBitmap(oriented, 0, 0);
            }
            using var image = SKImage.FromBitmap(clean);
            using var encoded = image.Encode(extension == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 90);
            if (encoded is null) throw new InvalidOperationException("Image encoding failed.");
            // Encoding pixels into a new image removes EXIF and other untrusted metadata.
            return encoded.ToArray();
        }
        catch (CopiersMaintenanceV2ValidationException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            throw new CopiersMaintenanceV2ValidationException("image_decode_failed", "La imagen está dañada o no puede sanearse de forma segura.");
        }
    }

    internal static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var inputPixels = source.GetPixelSpan();
        var outputPixels = output.GetPixelSpan();
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var (dx, dy) = origin switch
            {
                SKEncodedOrigin.TopRight => (source.Width - 1 - x, y),
                SKEncodedOrigin.BottomRight => (source.Width - 1 - x, source.Height - 1 - y),
                SKEncodedOrigin.BottomLeft => (x, source.Height - 1 - y),
                SKEncodedOrigin.LeftTop => (y, x),
                SKEncodedOrigin.RightTop => (source.Height - 1 - y, x),
                SKEncodedOrigin.RightBottom => (source.Height - 1 - y, source.Width - 1 - x),
                SKEncodedOrigin.LeftBottom => (y, source.Width - 1 - x),
                _ => (x, y)
            };
            inputPixels.Slice(y * source.RowBytes + x * 4, 4).CopyTo(outputPixels.Slice(dy * output.RowBytes + dx * 4, 4));
        }
        return output;
    }

    internal static void ValidateInk(byte[] content, CopiersMaintenanceV2Options options)
    {
        using var image = SKBitmap.Decode(content);
        if (image is null) throw new CopiersMaintenanceV2ValidationException("image_decode_failed", "La imagen está dañada.");
        var sampleStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(((long)image.Width * image.Height) / 1_000_000d)));
        var count = 0; var minX = image.Width; var minY = image.Height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.Height; y += sampleStep)
        for (var x = 0; x < image.Width; x += sampleStep)
        {
            var pixel = image.GetPixel(x, y);
            if (pixel.Red + pixel.Green + pixel.Blue >= 720) continue;
            count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (count < options.MinSignatureInkPixels || maxX - minX < options.MinSignatureWidthPixels / 2 || maxY - minY < options.MinSignatureHeightPixels / 2)
            throw new CopiersMaintenanceV2ValidationException("signature_ink_required", "La imagen de firma está vacía o no contiene trazos suficientes.");
    }
}
