using System;

namespace HyperTizen.Core
{
    public enum Edge
    {
        Top,
        Right,
        Bottom,
        Left
    }

    // A place on the screen whose color is measured. X and Y run from 0 to 1.
    public struct CapturePoint
    {
        public double X;
        public double Y;
        public Edge Edge;

        public CapturePoint(double x, double y, Edge edge)
        {
            X = x;
            Y = y;
            Edge = edge;
        }
    }

    // How many zones each edge of the screen has, and where their capture points are.
    public sealed class CaptureLayout
    {
        public const int MaxTopBottom = 16;
        public const int MaxLeftRight = 12;

        // How far in from the edge of the screen the points sit.
        private const double Inset = 0.05;

        public static readonly CaptureLayout Default = new CaptureLayout(4, 4, 3, 3);

        public CaptureLayout(int top, int bottom, int left, int right)
        {
            if (!IsValid(top, bottom, left, right))
                throw new ArgumentOutOfRangeException(
                    nameof(top), "Zones per edge: top and bottom 0 to " + MaxTopBottom + ", left and right 0 to " + MaxLeftRight + ", at least one in total.");

            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;

            // Top, right, bottom, left; along an edge from left to right or from top to bottom.
            var points = new CapturePoint[top + right + bottom + left];
            int next = 0;
            for (int i = 0; i < top; i++) points[next++] = new CapturePoint((i + 0.5) / top, Inset, Edge.Top);
            for (int i = 0; i < right; i++) points[next++] = new CapturePoint(1 - Inset, (i + 0.5) / right, Edge.Right);
            for (int i = 0; i < bottom; i++) points[next++] = new CapturePoint((i + 0.5) / bottom, 1 - Inset, Edge.Bottom);
            for (int i = 0; i < left; i++) points[next++] = new CapturePoint(Inset, (i + 0.5) / left, Edge.Left);
            Points = points;
            SpreadOrder = Spread(top, right, bottom, left);
        }

        private static int[] Spread(int top, int right, int bottom, int left)
        {
            // The points once around the screen: top and right as they are stored, bottom and
            // left backwards.
            int count = top + right + bottom + left;
            var around = new int[count];
            int next = 0;
            for (int i = 0; i < top + right; i++) around[next++] = i;
            for (int i = bottom - 1; i >= 0; i--) around[next++] = top + right + i;
            for (int i = left - 1; i >= 0; i--) around[next++] = top + right + bottom + i;

            // Counting with the bits reversed halves the gaps again and again: 0, 8, 4, 12, 2, ...
            // of sixteen. Numbers past the end are skipped.
            int bits = 0;
            while (1 << bits < count) bits++;

            var order = new int[count];
            next = 0;
            for (int i = 0; i < 1 << bits; i++)
            {
                int reversed = 0;
                for (int bit = 0; bit < bits; bit++)
                    if ((i & (1 << bit)) != 0) reversed |= 1 << (bits - 1 - bit);
                if (reversed < count) order[next++] = around[reversed];
            }
            return order;
        }

        public int Top { get; }
        public int Bottom { get; }
        public int Left { get; }
        public int Right { get; }

        // One point per zone. Do not change the array.
        public CapturePoint[] Points { get; }

        // Every index into Points once, in an order that jumps around the screen. Measuring a few
        // points at a time in this order spreads a change of picture over all edges at once
        // instead of letting it travel around the screen.
        public int[] SpreadOrder { get; }

        public static bool IsValid(int top, int bottom, int left, int right)
        {
            return top >= 0 && top <= MaxTopBottom
                && bottom >= 0 && bottom <= MaxTopBottom
                && left >= 0 && left <= MaxLeftRight
                && right >= 0 && right <= MaxLeftRight
                && top + bottom + left + right > 0;
        }

        public int Count(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top: return Top;
                case Edge.Right: return Right;
                case Edge.Bottom: return Bottom;
                default: return Left;
            }
        }

        // Where in Points an edge's first zone is.
        public int Offset(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top: return 0;
                case Edge.Right: return Top;
                case Edge.Bottom: return Top + Right;
                default: return Top + Right + Bottom;
            }
        }
    }
}
