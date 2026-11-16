using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class LetterboxDetectorTests
{
    private static readonly Rgb10 Black = new Rgb10(0, 0, 0);
    private static readonly Rgb10 Picture = new Rgb10(600, 400, 200);

    // Probe colors for a picture whose bars, top and bottom, reach `bar` of the screen height.
    private static Rgb10[] Bars(double bar)
    {
        return LetterboxDetector.ProbePoints
            .Select(point => (point.Edge == Edge.Top ? point.Y : 1 - point.Y) < bar ? Black : Picture)
            .ToArray();
    }

    private static Rgb10[] All(Rgb10 color)
    {
        return Enumerable.Repeat(color, LetterboxDetector.ProbePoints.Length).ToArray();
    }

    private static void Feed(LetterboxDetector detector, Rgb10[] colors, int times)
    {
        for (int i = 0; i < times; i++) detector.Update(colors);
    }

    [Fact]
    public void Probes_cover_every_column_and_depth_at_both_edges()
    {
        int expected = 2 * LetterboxDetector.Columns.Length * LetterboxDetector.Depths.Length;

        Assert.Equal(expected, LetterboxDetector.ProbePoints.Length);
        Assert.Equal(expected / 2, LetterboxDetector.ProbePoints.Count(point => point.Edge == Edge.Top));
        Assert.All(LetterboxDetector.ProbePoints.Where(point => point.Edge == Edge.Bottom), point => Assert.True(point.Y > 0.5));
    }

    [Theory]
    [InlineData(0.0, 0.05)]    // no bars
    [InlineData(0.019, 0.05)]  // 1.85:1, bars thinner than the outermost probe
    [InlineData(0.056, 0.08)]  // 2:1
    [InlineData(0.128, 0.16)]  // 2.39:1
    public void The_inset_is_the_first_depth_past_the_bars(double bar, double inset)
    {
        Assert.True(LetterboxDetector.TryMeasure(Bars(bar), out double measured));
        Assert.Equal(inset, measured, 6);
    }

    [Fact]
    public void A_black_screen_gives_no_answer()
    {
        Assert.False(LetterboxDetector.TryMeasure(All(Black), out _));
    }

    [Fact]
    public void A_dark_top_without_a_dark_bottom_is_not_a_bar()
    {
        Rgb10[] colors = LetterboxDetector.ProbePoints
            .Select(point => point.Edge == Edge.Top ? Black : Picture)
            .ToArray();

        Assert.True(LetterboxDetector.TryMeasure(colors, out double inset));
        Assert.Equal(CaptureLayout.DefaultInset, inset, 6);
    }

    [Fact]
    public void Bars_are_taken_only_after_they_are_confirmed()
    {
        var detector = new LetterboxDetector();

        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes - 1);
        Assert.Equal(CaptureLayout.DefaultInset, detector.Inset, 6);

        Assert.True(detector.Update(Bars(0.128)));
        Assert.Equal(0.16, detector.Inset, 6);
    }

    [Fact]
    public void An_interrupted_run_starts_counting_again()
    {
        var detector = new LetterboxDetector();

        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes - 1);
        detector.Update(Bars(0));
        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes - 1);

        Assert.Equal(CaptureLayout.DefaultInset, detector.Inset, 6);
    }

    [Fact]
    public void Dark_scenes_keep_the_bars_found()
    {
        var detector = new LetterboxDetector();
        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes);

        Feed(detector, All(Black), 10);

        Assert.Equal(0.16, detector.Inset, 6);
    }

    [Fact]
    public void Points_go_back_once_the_bars_are_gone()
    {
        var detector = new LetterboxDetector();
        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes);

        Feed(detector, Bars(0), LetterboxDetector.ConfirmProbes);

        Assert.Equal(CaptureLayout.DefaultInset, detector.Inset, 6);
    }

    [Fact]
    public void Reset_forgets_the_bars()
    {
        var detector = new LetterboxDetector();
        Feed(detector, Bars(0.128), LetterboxDetector.ConfirmProbes);

        detector.Reset();

        Assert.Equal(CaptureLayout.DefaultInset, detector.Inset, 6);
    }

    [Fact]
    public void The_wrong_number_of_colors_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new LetterboxDetector().Update(new Rgb10[3]));
    }

    [Fact]
    public void A_vertical_inset_moves_the_top_and_bottom_points_in()
    {
        CaptureLayout moved = CaptureLayout.Default.WithVerticalInset(0.16);

        Assert.All(moved.Points.Where(point => point.Edge == Edge.Top), point => Assert.Equal(0.16, point.Y, 6));
        Assert.All(moved.Points.Where(point => point.Edge == Edge.Bottom), point => Assert.Equal(0.84, point.Y, 6));
        Assert.All(moved.Points.Where(point => point.Edge == Edge.Left), point => Assert.Equal(CaptureLayout.DefaultInset, point.X, 6));
        Assert.Equal(0.5, moved.Points.Where(point => point.Edge == Edge.Left).ElementAt(1).Y, 6);
        Assert.All(moved.Points.Where(point => point.Edge == Edge.Right), point => Assert.Equal(1 - CaptureLayout.DefaultInset, point.X, 6));
        Assert.Equal((4, 4, 3, 3), (moved.Top, moved.Bottom, moved.Left, moved.Right));
    }

    [Fact]
    public void Side_zones_stay_between_the_bars()
    {
        // A 2.39:1 film: bars reach 12.8% from the top and the bottom.
        CaptureLayout moved = CaptureLayout.Default.WithVerticalInset(0.16);

        Assert.All(
            moved.Points.Where(point => point.Edge == Edge.Left || point.Edge == Edge.Right),
            point => Assert.InRange(point.Y, 0.128, 0.872));
    }

    [Fact]
    public void Without_bars_side_zones_spread_over_the_whole_height()
    {
        double[] left = CaptureLayout.Default.Points.Where(point => point.Edge == Edge.Left).Select(point => point.Y).ToArray();

        Assert.Equal(new[] { 1 / 6.0, 0.5, 5 / 6.0 }, left, new DoubleComparer(6));
    }

    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        private readonly double _tolerance;
        public DoubleComparer(int digits) { _tolerance = Math.Pow(10, -digits); }
        public bool Equals(double a, double b) => Math.Abs(a - b) < _tolerance;
        public int GetHashCode(double value) => 0;
    }

    [Fact]
    public void The_same_inset_keeps_the_same_layout()
    {
        Assert.Same(CaptureLayout.Default, CaptureLayout.Default.WithVerticalInset(CaptureLayout.DefaultInset));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public void An_inset_out_of_range_is_refused(double inset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureLayout(4, 4, 3, 3, inset));
    }
}
