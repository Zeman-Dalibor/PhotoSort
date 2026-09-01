using System.Buffers.Binary;
using PhotoSort.Services;

namespace PhotoSort.Tests;

public sealed class TiffPreviewExtractorTests
{
    private readonly TiffPreviewExtractor _extractor = new();

    [Fact]
    public void ExtractsTheJpegStripOfACr2StyleContainer()
    {
        var jpeg = FakeJpeg(64);
        var tiff = BuildTiff(jpeg);

        var extracted = _extractor.TryExtract(new MemoryStream(tiff));

        Assert.Equal(jpeg, extracted);
    }

    [Fact]
    public void PrefersTheLargestPreviewWhenSeveralArePresent()
    {
        var small = FakeJpeg(32);
        var large = FakeJpeg(512);
        var tiff = BuildTiff(small, large);

        var extracted = _extractor.TryExtract(new MemoryStream(tiff));

        Assert.Equal(large, extracted);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoJpegAtAll()
    {
        var noise = new byte[512];
        Random.Shared.NextBytes(noise);
        noise[0] = noise[1] = 0x00;

        Assert.Null(_extractor.TryExtract(new MemoryStream(noise)));
    }

    /// <summary>A JPEG-shaped blob: SOI, filler that never contains a marker, EOI.</summary>
    private static byte[] FakeJpeg(int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)0x5A);
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }

    /// <summary>
    /// Minimal little-endian TIFF with one IFD per preview, each declaring a JPEG-compressed strip.
    /// </summary>
    private static byte[] BuildTiff(params byte[][] previews)
    {
        const int headerSize = 8;
        const int ifdSize = 2 + 3 * 12 + 4;

        var ifdOffsets = new int[previews.Length];
        var dataOffsets = new int[previews.Length];
        var position = headerSize;

        for (var i = 0; i < previews.Length; i++)
        {
            ifdOffsets[i] = position;
            position += ifdSize;
        }

        for (var i = 0; i < previews.Length; i++)
        {
            dataOffsets[i] = position;
            position += previews[i].Length;
        }

        var buffer = new byte[position];
        buffer[0] = buffer[1] = 0x49;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)ifdOffsets[0]);

        for (var i = 0; i < previews.Length; i++)
        {
            var ifd = buffer.AsSpan(ifdOffsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(ifd, 3);
            WriteEntry(ifd[2..], 0x0103, type: 3, value: 6);
            WriteEntry(ifd[14..], 0x0111, type: 4, value: (uint)dataOffsets[i]);
            WriteEntry(ifd[26..], 0x0117, type: 4, value: (uint)previews[i].Length);

            var nextIfd = i + 1 < previews.Length ? (uint)ifdOffsets[i + 1] : 0u;
            BinaryPrimitives.WriteUInt32LittleEndian(ifd[38..], nextIfd);

            previews[i].CopyTo(buffer.AsSpan(dataOffsets[i]));
        }

        return buffer;
    }

    private static void WriteEntry(Span<byte> target, ushort tag, ushort type, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(target, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(target[2..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(target[4..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(target[8..], value);
    }
}
