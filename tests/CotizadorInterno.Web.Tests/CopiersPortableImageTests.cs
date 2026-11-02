using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using SkiaSharp;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersPortableImageTests
{
    private static byte[] Image(bool ink, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(600, 240);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            if (ink)
            {
                using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 9, IsAntialias = true };
                canvas.DrawLine(50, 180, 260, 45, paint);
                canvas.DrawLine(260, 45, 540, 185, paint);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }
    private static FormFile File(byte[] bytes, string name) => new(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    [Fact]
    public async Task SignedJpegIsSanitizedAndRetainsVisibleInkOnEveryPlatform()
    {
        var result = await CopiersMaintenanceV2Validation.ReadSignatureAsync(File(Image(true), "firma.jpg"), new(), default);
        using var decoded = SKBitmap.Decode(result.Content);
        Assert.Equal(600, decoded.Width); Assert.Equal(240, decoded.Height);
        Assert.Equal("image/jpeg", result.ContentType);
        CopiersPortableImageSanitizer.ValidateInk(result.Content, new());
    }
    [Fact]
    public async Task BlankSignatureStillFailsClosed()
    {
        var error = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadSignatureAsync(File(Image(false), "firma.jpg"), new(), default));
        Assert.Equal("signature_ink_required", error.Code);
    }
    [Fact]
    public async Task PngAttachmentIsDecodedAndEncodedWithoutAcceptingTruncatedData()
    {
        var png = Image(true, SKEncodedImageFormat.Png);
        var result = await CopiersMaintenanceV2Validation.ReadAttachmentsAsync([File(png, "evidencia.png")], new(), default);
        using var decoded = SKBitmap.Decode(Assert.Single(result).Content);
        Assert.Equal(600, decoded.Width);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadAttachmentsAsync([File(png[..40], "evidencia.png")], new(), default));
    }
    [Fact]
    public void DimensionLimitIsCheckedBeforeDecode()
    {
        var error = Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersPortableImageSanitizer.Reencode(
            Image(true), ".jpg", new() { MaxAttachmentImageDimensionPixels = 100 }, false));
        Assert.Equal("image_dimensions_invalid", error.Code);
    }
    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 0, 0)]
    [InlineData(SKEncodedOrigin.TopRight, 2, 0)]
    [InlineData(SKEncodedOrigin.BottomRight, 2, 1)]
    [InlineData(SKEncodedOrigin.BottomLeft, 0, 1)]
    [InlineData(SKEncodedOrigin.LeftTop, 0, 0)]
    [InlineData(SKEncodedOrigin.RightTop, 1, 0)]
    [InlineData(SKEncodedOrigin.RightBottom, 1, 2)]
    [InlineData(SKEncodedOrigin.LeftBottom, 0, 2)]
    public void AllExifOrientationsKeepTheReferencePixel(SKEncodedOrigin origin, int x, int y)
    {
        using var input = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        input.Erase(SKColors.White); input.SetPixel(0, 0, SKColors.Red);
        using var output = CopiersPortableImageSanitizer.Orient(input, origin);
        Assert.Equal(SKColors.Red, output.GetPixel(x, y));
    }
}
