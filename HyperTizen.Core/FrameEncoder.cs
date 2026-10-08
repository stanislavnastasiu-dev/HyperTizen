using System;

namespace HyperTizen.Core
{
    // Turns one frame of captured colors into the small border image Hyperion maps onto the LEDs.
    public static class FrameEncoder
    {
        public const int Width = 64;
        public const int Height = 48;

        private const int TopBottomRows = 4;
        private const int LeftRightColumns = 3;

        // Each border is split evenly into one band per zone of its edge, and a zone colors the band
        // at the place it was measured. A border whose edge has no zones stays black.
        public static byte[] ToRgb(Rgb10[] colors, CaptureLayout layout)
        {
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (colors.Length != layout.Points.Length)
                throw new ArgumentException(
                    "Expected " + layout.Points.Length + " colors for this layout, got " + colors.Length + ".", nameof(colors));

            var rgb = new byte[Width * Height * 3];

            if (layout.Top > 0)
                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < TopBottomRows; y++)
                        SetPixel(rgb, x, y, colors[layout.Offset(Edge.Top) + x * layout.Top / Width]);

            if (layout.Bottom > 0)
                for (int x = 0; x < Width; x++)
                    for (int y = Height - TopBottomRows; y < Height; y++)
                        SetPixel(rgb, x, y, colors[layout.Offset(Edge.Bottom) + x * layout.Bottom / Width]);

            // Drawn last, so the left and right borders win the corners.
            if (layout.Left > 0)
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < LeftRightColumns; x++)
                        SetPixel(rgb, x, y, colors[layout.Offset(Edge.Left) + y * layout.Left / Height]);

            if (layout.Right > 0)
                for (int y = 0; y < Height; y++)
                    for (int x = Width - LeftRightColumns; x < Width; x++)
                        SetPixel(rgb, x, y, colors[layout.Offset(Edge.Right) + y * layout.Right / Height]);

            return rgb;
        }

        public static string ToBase64Png(Rgb10[] colors, CaptureLayout layout)
        {
            return Convert.ToBase64String(PngWriter.EncodeRgb(Width, Height, ToRgb(colors, layout)));
        }

        private static void SetPixel(byte[] rgb, int x, int y, Rgb10 color)
        {
            int offset = (y * Width + x) * 3;
            rgb[offset] = ToByte(color.R);
            rgb[offset + 1] = ToByte(color.G);
            rgb[offset + 2] = ToByte(color.B);
        }

        // Captured channels run from 0 to 1023; images and previews use 0 to 255.
        public static byte ToByte(int tenBit)
        {
            return (byte)(Math.Max(0, Math.Min(tenBit, 1023)) >> 2);
        }
    }
}
