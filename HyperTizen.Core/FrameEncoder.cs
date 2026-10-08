using System;

namespace HyperTizen.Core
{
    // Turns one frame of captured colors into the small border image Hyperion maps onto the LEDs.
    public static class FrameEncoder
    {
        public const int Width = 64;
        public const int Height = 48;
        public const int MinColors = 14;

        public static byte[] ToRgb(Rgb10[] colors)
        {
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (colors.Length < MinColors)
                throw new ArgumentException("Expected at least " + MinColors + " colors.", nameof(colors));

            var rgb = new byte[Width * Height * 3];

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < 4; y++)
                    SetPixel(rgb, x, y, colors[x / 16]);

            for (int x = 0; x < Width; x++)
                for (int y = 44; y < Height; y++)
                    SetPixel(rgb, x, y, colors[7 + x / 16]);

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < 3; x++)
                    SetPixel(rgb, x, y, colors[11 + y / 16]);

            for (int y = 0; y < Height; y++)
                for (int x = 61; x < Width; x++)
                    SetPixel(rgb, x, y, colors[4 + y / 16]);

            return rgb;
        }

        public static string ToBase64Png(Rgb10[] colors)
        {
            return Convert.ToBase64String(PngWriter.EncodeRgb(Width, Height, ToRgb(colors)));
        }

        private static void SetPixel(byte[] rgb, int x, int y, Rgb10 color)
        {
            int offset = (y * Width + x) * 3;
            rgb[offset] = Clamp(color.R);
            rgb[offset + 1] = Clamp(color.G);
            rgb[offset + 2] = Clamp(color.B);
        }

        private static byte Clamp(int channel)
        {
            return (byte)Math.Max(0, Math.Min(channel, 255));
        }
    }
}
