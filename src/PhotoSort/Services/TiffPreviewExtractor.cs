using System.Buffers.Binary;

namespace PhotoSort.Services;

/// <summary>
/// Pulls the largest embedded JPEG preview out of a TIFF-based RAW file (CR2, NEF, ARW, DNG, ...).
/// Canon CR2 stores a full-resolution JPEG in IFD0, which is exactly what a culling tool needs.
/// </summary>
public sealed class TiffPreviewExtractor
{
    private const int MaxIfdCount = 32;
    private const int MaxEntriesPerIfd = 512;

    private const ushort TagCompression = 0x0103;
    private const ushort TagStripOffsets = 0x0111;
    private const ushort TagStripByteCounts = 0x0117;
    private const ushort TagJpegInterchangeFormat = 0x0201;
    private const ushort TagJpegInterchangeFormatLength = 0x0202;
    private const ushort TagSubIfds = 0x014A;

    private const ushort CompressionOldJpeg = 6;
    private const ushort CompressionJpeg = 7;

    /// <summary>Returns the preview bytes, or <c>null</c> when the file holds no usable JPEG.</summary>
    public byte[]? TryExtract(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return TryExtract(stream);
    }

    public byte[]? TryExtract(Stream stream)
    {
        var best = FindLargestJpegSegment(stream);
        if (best is { Length: > 0 })
        {
            var bytes = ReadBytes(stream, best.Value.Offset, best.Value.Length);
            if (IsJpeg(bytes))
            {
                return bytes;
            }
        }

        return ScanForJpeg(stream);
    }

    private static JpegSegment? FindLargestJpegSegment(Stream stream)
    {
        if (!TryReadHeader(stream, out var littleEndian, out var firstIfdOffset))
        {
            return null;
        }

        JpegSegment? best = null;
        var pending = new Queue<long>();
        pending.Enqueue(firstIfdOffset);
        var visited = new HashSet<long>();

        while (pending.Count > 0 && visited.Count < MaxIfdCount)
        {
            var offset = pending.Dequeue();
            if (offset <= 0 || offset >= stream.Length || !visited.Add(offset))
            {
                continue;
            }

            var ifd = ReadIfd(stream, offset, littleEndian);
            if (ifd is null)
            {
                continue;
            }

            foreach (var subIfd in ifd.SubIfdOffsets)
            {
                pending.Enqueue(subIfd);
            }

            if (ifd.NextIfdOffset > 0)
            {
                pending.Enqueue(ifd.NextIfdOffset);
            }

            var candidate = ifd.ToJpegSegment();
            if (candidate is not null && candidate.Value.Length > (best?.Length ?? 0) &&
                candidate.Value.Offset + candidate.Value.Length <= stream.Length)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool TryReadHeader(Stream stream, out bool littleEndian, out long firstIfdOffset)
    {
        littleEndian = true;
        firstIfdOffset = 0;

        Span<byte> header = stackalloc byte[8];
        stream.Position = 0;
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            return false;
        }

        if (header[0] == 0x49 && header[1] == 0x49)
        {
            littleEndian = true;
        }
        else if (header[0] == 0x4D && header[1] == 0x4D)
        {
            littleEndian = false;
        }
        else
        {
            return false;
        }

        var magic = ReadUInt16(header[2..], littleEndian);
        if (magic != 42 && magic != 0x4F52 && magic != 0x5352)
        {
            return false;
        }

        firstIfdOffset = ReadUInt32(header[4..], littleEndian);
        return true;
    }

    private static IfdSummary? ReadIfd(Stream stream, long offset, bool littleEndian)
    {
        Span<byte> countBuffer = stackalloc byte[2];
        stream.Position = offset;
        if (stream.ReadAtLeast(countBuffer, 2, throwOnEndOfStream: false) < 2)
        {
            return null;
        }

        var entryCount = ReadUInt16(countBuffer, littleEndian);
        if (entryCount == 0 || entryCount > MaxEntriesPerIfd)
        {
            return null;
        }

        var body = new byte[entryCount * 12 + 4];
        if (stream.ReadAtLeast(body, body.Length, throwOnEndOfStream: false) < body.Length)
        {
            return null;
        }

        var summary = new IfdSummary();

        for (var i = 0; i < entryCount; i++)
        {
            var entry = body.AsSpan(i * 12, 12);
            var tag = ReadUInt16(entry, littleEndian);
            var type = ReadUInt16(entry[2..], littleEndian);
            var count = ReadUInt32(entry[4..], littleEndian);
            var valueField = entry[8..12];

            switch (tag)
            {
                case TagCompression:
                    summary.Compression = (ushort)ReadScalar(stream, type, count, valueField, littleEndian);
                    break;
                case TagStripOffsets:
                    summary.StripOffset = ReadScalar(stream, type, count, valueField, littleEndian);
                    break;
                case TagStripByteCounts:
                    summary.StripByteCount = ReadScalar(stream, type, count, valueField, littleEndian);
                    break;
                case TagJpegInterchangeFormat:
                    summary.JpegOffset = ReadScalar(stream, type, count, valueField, littleEndian);
                    break;
                case TagJpegInterchangeFormatLength:
                    summary.JpegLength = ReadScalar(stream, type, count, valueField, littleEndian);
                    break;
                case TagSubIfds:
                    summary.SubIfdOffsets.AddRange(ReadLongArray(stream, type, count, valueField, littleEndian));
                    break;
            }
        }

        summary.NextIfdOffset = ReadUInt32(body.AsSpan(entryCount * 12), littleEndian);
        return summary;
    }

    private static long ReadScalar(Stream stream, ushort type, uint count, ReadOnlySpan<byte> valueField, bool littleEndian)
    {
        var values = ReadLongArray(stream, type, count, valueField, littleEndian);
        return values.Count > 0 ? values[0] : 0;
    }

    private static List<long> ReadLongArray(Stream stream, ushort type, uint count, ReadOnlySpan<byte> valueField, bool littleEndian)
    {
        var elementSize = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 or 13 => 4, _ => 0 };
        var result = new List<long>();

        if (elementSize is 0 or 1 || count == 0 || count > 4096)
        {
            return result;
        }

        var totalBytes = (int)(count * elementSize);
        byte[] data;

        if (totalBytes <= 4)
        {
            data = valueField[..totalBytes].ToArray();
        }
        else
        {
            var dataOffset = ReadUInt32(valueField, littleEndian);
            if (dataOffset <= 0 || dataOffset + totalBytes > stream.Length)
            {
                return result;
            }

            data = ReadBytes(stream, dataOffset, totalBytes);
        }

        for (var i = 0; i < count; i++)
        {
            var slice = data.AsSpan(i * elementSize, elementSize);
            result.Add(elementSize == 2 ? ReadUInt16(slice, littleEndian) : ReadUInt32(slice, littleEndian));
        }

        return result;
    }

    /// <summary>Last-resort scan for a JPEG SOI/EOI pair; covers containers we do not parse (RAF).</summary>
    private static byte[]? ScanForJpeg(Stream stream)
    {
        const int windowSize = 1 << 20;
        var limit = (int)Math.Min(stream.Length, windowSize);
        var head = ReadBytes(stream, 0, limit);

        for (var i = 0; i < head.Length - 3; i++)
        {
            if (head[i] != 0xFF || head[i + 1] != 0xD8 || head[i + 2] != 0xFF)
            {
                continue;
            }

            var end = FindEndOfImage(stream, i);
            if (end > i + 4)
            {
                return ReadBytes(stream, i, (int)(end - i));
            }
        }

        return null;
    }

    private static long FindEndOfImage(Stream stream, long start)
    {
        const int chunkSize = 1 << 16;
        var buffer = new byte[chunkSize];
        var position = start;
        var previousWasMarker = false;

        while (position < stream.Length)
        {
            stream.Position = position;
            var read = stream.Read(buffer, 0, chunkSize);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                if (previousWasMarker && buffer[i] == 0xD9)
                {
                    return position + i + 1;
                }

                previousWasMarker = buffer[i] == 0xFF;
            }

            position += read;
        }

        return -1;
    }

    private static byte[] ReadBytes(Stream stream, long offset, int length)
    {
        var buffer = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static bool IsJpeg(byte[] bytes) => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(span)
        : BinaryPrimitives.ReadUInt16BigEndian(span);

    private static uint ReadUInt32(ReadOnlySpan<byte> span, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(span)
        : BinaryPrimitives.ReadUInt32BigEndian(span);

    private readonly record struct JpegSegment(long Offset, int Length);

    private sealed class IfdSummary
    {
        public ushort Compression { get; set; }
        public long StripOffset { get; set; }
        public long StripByteCount { get; set; }
        public long JpegOffset { get; set; }
        public long JpegLength { get; set; }
        public long NextIfdOffset { get; set; }
        public List<long> SubIfdOffsets { get; } = [];

        public JpegSegment? ToJpegSegment()
        {
            if (JpegOffset > 0 && JpegLength > 0)
            {
                return new JpegSegment(JpegOffset, (int)JpegLength);
            }

            if (StripOffset > 0 && StripByteCount > 0 &&
                Compression is CompressionOldJpeg or CompressionJpeg)
            {
                return new JpegSegment(StripOffset, (int)StripByteCount);
            }

            return null;
        }
    }
}
