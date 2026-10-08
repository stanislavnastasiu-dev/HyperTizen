using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class PngWriterTests
{
    private const int Width = 5;
    private const int Height = 3;

    private static byte[] SamplePixels()
    {
        var pixels = new byte[Width * Height * 3];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7);
        return pixels;
    }

    private static List<(string Type, byte[] Data, uint Crc)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        int pos = 8;
        while (pos < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            byte[] data = png.AsSpan(pos + 8, length).ToArray();
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + length));
            chunks.Add((type, data, crc));
            pos += 12 + length;
        }
        return chunks;
    }

    // Bitwise CRC-32, written independently of the table-driven one in PngWriter.
    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void Starts_with_png_signature()
    {
        byte[] png = PngWriter.EncodeRgb(Width, Height, SamplePixels());

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
    }

    [Fact]
    public void Header_describes_8bit_rgb_of_the_given_size()
    {
        var chunks = ReadChunks(PngWriter.EncodeRgb(Width, Height, SamplePixels()));

        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(c => c.Type).ToArray());
        byte[] ihdr = chunks[0].Data;
        Assert.Equal(13, ihdr.Length);
        Assert.Equal(Width, BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(0)));
        Assert.Equal(Height, BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4)));
        Assert.Equal(new byte[] { 8, 2, 0, 0, 0 }, ihdr[8..]);
    }

    [Fact]
    public void Chunk_crcs_verify()
    {
        foreach (var chunk in ReadChunks(PngWriter.EncodeRgb(Width, Height, SamplePixels())))
        {
            byte[] covered = Encoding.ASCII.GetBytes(chunk.Type).Concat(chunk.Data).ToArray();
            Assert.Equal(Crc32(covered), chunk.Crc);
        }
    }

    [Fact]
    public void Idat_inflates_to_the_original_pixels_with_filter_bytes()
    {
        byte[] pixels = SamplePixels();
        byte[] idat = ReadChunks(PngWriter.EncodeRgb(Width, Height, pixels))[1].Data;

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress))
            zlib.CopyTo(inflated);
        byte[] raw = inflated.ToArray();

        int stride = Width * 3;
        Assert.Equal(Height * (stride + 1), raw.Length);
        for (int y = 0; y < Height; y++)
        {
            Assert.Equal(0, raw[y * (stride + 1)]);
            Assert.Equal(pixels.AsSpan(y * stride, stride).ToArray(),
                         raw.AsSpan(y * (stride + 1) + 1, stride).ToArray());
        }
    }

    [Fact]
    public void Rejects_a_buffer_of_the_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.EncodeRgb(Width, Height, new byte[10]));
    }
}
