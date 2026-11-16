using System;
using System.Collections.Generic;

namespace HyperTizen.Core
{
    // Finds black bars above and below the picture (letterboxed films) and says how far in the top
    // and bottom capture points must sit to measure the picture instead of the bars.
    //
    // It is fed the colors of ProbePoints: a few columns, each measured at several depths from the
    // top and from the bottom edge. A depth counts as bar when every probe at it, top and bottom, is
    // black. Bars are symmetric, so a dark sky or a dark floor alone does not count.
    // A new inset is taken only after the same answer came back ConfirmProbes times in a row, so a
    // dark scene or a fade does not move the points back and forth.
    public sealed class LetterboxDetector
    {
        // Depths measured from the top and bottom edges, outermost first. A 2.39:1 film on a 16:9
        // screen has bars about 12.8% high; 2:1 about 5.6%; 1.85:1 about 1.9%.
        public static readonly double[] Depths = { 0.04, 0.08, 0.12, 0.16 };

        // Where across the screen the depths are measured.
        public static readonly double[] Columns = { 0.35, 0.65 };

        // A 10-bit channel at or below this counts as black.
        public const int BlackLevel = 48;

        public const int ConfirmProbes = 3;

        // Top probes first (column by column, outermost depth first), then the bottom probes in the
        // same order.
        public static readonly CapturePoint[] ProbePoints = BuildProbePoints();

        private double _inset = CaptureLayout.DefaultInset;
        private double _candidate = double.NaN;
        private int _seen;

        // Where the top and bottom points should sit now.
        public double Inset
        {
            get { return _inset; }
        }

        // Back to no bars, forgetting any answer that was waiting for confirmation.
        public void Reset()
        {
            _inset = CaptureLayout.DefaultInset;
            _candidate = double.NaN;
            _seen = 0;
        }

        // Takes one probe's colors; true when Inset changed.
        public bool Update(IReadOnlyList<Rgb10> colors)
        {
            if (colors == null || colors.Count != ProbePoints.Length)
                throw new ArgumentException("Expected " + ProbePoints.Length + " probe colors.", nameof(colors));

            double target;
            if (!TryMeasure(colors, out target)) return false;

            if (target == _inset)
            {
                _candidate = double.NaN;
                _seen = 0;
                return false;
            }

            if (target == _candidate)
            {
                _seen++;
            }
            else
            {
                _candidate = target;
                _seen = 1;
            }

            if (_seen < ConfirmProbes) return false;

            _inset = target;
            _candidate = double.NaN;
            _seen = 0;
            return true;
        }

        // The inset these colors call for; false when they cannot tell (everything is black).
        public static bool TryMeasure(IReadOnlyList<Rgb10> colors, out double inset)
        {
            int half = Columns.Length * Depths.Length;
            int bar = 0;
            while (bar < Depths.Length && IsBlackAtDepth(colors, bar, half)) bar++;

            inset = CaptureLayout.DefaultInset;
            // Black all the way in is a dark scene or bars too tall to be a film: no answer.
            if (bar == Depths.Length) return false;

            // The first depth that is picture; no bar keeps the points where they always were.
            if (bar > 0) inset = Math.Max(CaptureLayout.DefaultInset, Depths[bar]);
            return true;
        }

        private static bool IsBlackAtDepth(IReadOnlyList<Rgb10> colors, int depth, int half)
        {
            for (int column = 0; column < Columns.Length; column++)
            {
                int index = column * Depths.Length + depth;
                if (!IsBlack(colors[index]) || !IsBlack(colors[half + index])) return false;
            }
            return true;
        }

        private static bool IsBlack(Rgb10 color)
        {
            return color.R <= BlackLevel && color.G <= BlackLevel && color.B <= BlackLevel;
        }

        private static CapturePoint[] BuildProbePoints()
        {
            var points = new CapturePoint[2 * Columns.Length * Depths.Length];
            int next = 0;
            foreach (double x in Columns)
                foreach (double depth in Depths)
                    points[next++] = new CapturePoint(x, depth, Edge.Top);
            foreach (double x in Columns)
                foreach (double depth in Depths)
                    points[next++] = new CapturePoint(x, 1 - depth, Edge.Bottom);
            return points;
        }
    }
}
