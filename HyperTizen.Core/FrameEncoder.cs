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

        // Every pixel belongs to the edge it is nearest to. That edge is split evenly into one band
        // per zone, and a zone colors its band all the way to the middle of the image: however deep
        // the server's LED areas reach into the picture, they find color and not black. The part of
        // an edge without zones stays black, so its LEDs stay dark.
        public static byte[] ToRgb(Rgb10[] colors, CaptureLayout layout)
        {
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (colors.Length != layout.Points.Length)
                throw new ArgumentException(
                    "Expected " + layout.Points.Length + " colors for this layout, got " + colors.Length + ".", nameof(colors));

            var rgb = new byte[Width * Height * 3];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Edge? edge = EdgeOf(x, y, layout);
                    if (edge == null) continue;

                    bool alongX = edge == Edge.Top || edge == Edge.Bottom;
                    int zone = alongX ? x * layout.Count(edge.Value) / Width : y * layout.Count(edge.Value) / Height;
                    SetPixel(rgb, x, y, colors[layout.Offset(edge.Value) + zone]);
                }
            }

            return rgb;
        }

        // The edge whose zones color a pixel; null when the pixel stays black.
        private static Edge? EdgeOf(int x, int y, CaptureLayout layout)
        {
            // Distances as a share of the image, so the borders between edges are its diagonals.
            // Left and right come first: they win a tie, which is what gives them the corners.
            Edge nearest = Edge.Left;
            double distance = (x + 0.5) / Width;
            Consider(Edge.Right, (Width - x - 0.5) / Width, ref nearest, ref distance);
            Consider(Edge.Top, (y + 0.5) / Height, ref nearest, ref distance);
            Consider(Edge.Bottom, (Height - y - 0.5) / Height, ref nearest, ref distance);
            if (layout.Count(nearest) > 0) return nearest;

            // Next to an edge without zones, a border keeps its own end up to the corner.
            if (y < TopBottomRows && layout.Top > 0) return Edge.Top;
            if (y >= Height - TopBottomRows && layout.Bottom > 0) return Edge.Bottom;
            if (x < LeftRightColumns && layout.Left > 0) return Edge.Left;
            if (x >= Width - LeftRightColumns && layout.Right > 0) return Edge.Right;
            return null;
        }

        private static void Consider(Edge edge, double distance, ref Edge nearest, ref double nearestDistance)
        {
            if (distance >= nearestDistance) return;
            nearest = edge;
            nearestDistance = distance;
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
