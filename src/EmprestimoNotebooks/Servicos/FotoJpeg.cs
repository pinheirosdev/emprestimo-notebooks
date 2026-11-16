using SkiaSharp;

namespace EmprestimoNotebooks.Servicos;

internal static class FotoJpeg
{
    internal const int LimiteBytes = 1024 * 1024;
    private const int LimiteDimensao = 960;

    internal static byte[]? Ler(IFormFile arquivo)
    {
        if (arquivo.Length is <= 0 or > LimiteBytes)
        {
            return null;
        }

        using var entrada = arquivo.OpenReadStream();
        using var imagem = SKCodec.Create(entrada);
        if (imagem is null || imagem.EncodedFormat != SKEncodedImageFormat.Jpeg
            || imagem.Info.Width is <= 0 or > LimiteDimensao
            || imagem.Info.Height is <= 0 or > LimiteDimensao)
        {
            return null;
        }

        using var pixels = new SKBitmap(new SKImageInfo(imagem.Info.Width, imagem.Info.Height));
        if (imagem.GetPixels(pixels.Info, pixels.GetPixels()) != SKCodecResult.Success)
        {
            return null;
        }

        using var jpeg = pixels.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg?.ToArray();
    }
}
