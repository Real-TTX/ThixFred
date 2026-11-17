using System.Buffers.Binary;
using System.IO.Compression;

namespace ThixFred.Core.Infrastructure;

/// <summary>
/// Writes BGRA pixels (premultiplied alpha, top-down) as a PNG file: the icon cache needs nothing more, so no image library
/// is shipped for it.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(byte[] bgraPremultiplied, int width, int height)
    {
        // RGBA rows, each with filter byte 0, alpha un-premultiplied
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var source = (y * width + x) * 4;
                var target = row + 1 + x * 4;
                var alpha = bgraPremultiplied[source + 3];
                raw[target] = Unpremultiply(bgraPremultiplied[source + 2], alpha);
                raw[target + 1] = Unpremultiply(bgraPremultiplied[source + 1], alpha);
                raw[target + 2] = Unpremultiply(bgraPremultiplied[source], alpha);
                raw[target + 3] = alpha;
            }
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(output, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte Unpremultiply(byte value, byte alpha) => alpha is 0 or 255 ? value : (byte)Math.Min(255, value * 255 / alpha);

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var value = (uint)index;
        for (var bit = 0; bit < 8; bit++)
        {
            value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
        }

        return value;
    }).ToArray();

    private static uint Crc(uint crc, byte[] bytes)
    {
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        output.Write(checksum);
    }
}
