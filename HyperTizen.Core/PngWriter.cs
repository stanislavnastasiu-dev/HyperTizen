using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HyperTizen.Core
{
    // Minimal encoder for 8-bit RGB PNG images, enough for the small frames sent to Hyperion.
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] EncodeRgb(int width, int height, byte[] rgb)
        {
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (rgb.Length != width * height * 3)
                throw new ArgumentException("Expected " + (width * height * 3) + " bytes of RGB data.", nameof(rgb));

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;  // bit depth
            header[9] = 2;  // color type: truecolor
            header[10] = 0; // compression
            header[11] = 0; // filter
            header[12] = 0; // interlace

            using (var output = new MemoryStream())
            {
                output.Write(Signature, 0, Signature.Length);
                WriteChunk(output, "IHDR", header);
                WriteChunk(output, "IDAT", ZlibCompress(AddFilterBytes(width, height, rgb)));
                WriteChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        // Each scanline is prefixed with filter type 0 (none).
        private static byte[] AddFilterBytes(int width, int height, byte[] rgb)
        {
            int stride = width * 3;
            var raw = new byte[height * (stride + 1)];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
            return raw;
        }

        private static byte[] ZlibCompress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                    deflate.Write(raw, 0, raw.Length);

                var checksum = new byte[4];
                WriteBigEndian(checksum, 0, Adler32(raw));
                output.Write(checksum, 0, checksum.Length);
                return output.ToArray();
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);

            uint crc = 0xFFFFFFFF;
            crc = UpdateCrc(crc, typeBytes);
            crc = UpdateCrc(crc, data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, ~crc);

            output.Write(length, 0, 4);
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, data.Length);
            output.Write(crcBytes, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] bytes)
        {
            foreach (byte b in bytes)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Adler32(byte[] bytes)
        {
            uint a = 1, b = 0;
            foreach (byte value in bytes)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
