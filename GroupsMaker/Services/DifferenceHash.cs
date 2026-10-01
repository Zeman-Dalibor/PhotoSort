using System.Numerics;
using SkiaSharp;

namespace GroupsMaker.Services;

/// <summary>
/// 64-bit difference hash: the image is reduced to a 9x8 grey grid and each bit records whether a
/// pixel is darker than its right-hand neighbour. Exposure, noise and JPEG artefacts barely move
/// the result, while a different scene changes roughly half the bits.
/// </summary>
public static class DifferenceHash
{
    public const int Bits = 64;

    private const int Columns = 9;
    private const int Rows = 8;

    /// <summary>Returns <c>null</c> when Skia refuses to resize the source.</summary>
    public static ulong? TryCompute(SKBitmap source)
    {
        var info = new SKImageInfo(Columns, Rows, source.ColorType, source.AlphaType);

        // Medium quality means mipmapped averaging, which survives the very large reduction
        // factor far better than a single cubic pass would.
        using var small = source.Resize(info, SKFilterQuality.Medium);
        if (small is null)
        {
            return null;
        }

        Span<float> luma = stackalloc float[Columns * Rows];
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                var pixel = small.GetPixel(x, y);
                luma[(y * Columns) + x] = (0.299f * pixel.Red) + (0.587f * pixel.Green) + (0.114f * pixel.Blue);
            }
        }

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns - 1; x++)
            {
                if (luma[(y * Columns) + x] < luma[(y * Columns) + x + 1])
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    /// <summary>Number of differing bits: 0 means identical, around 32 means unrelated.</summary>
    public static int Distance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);
}
