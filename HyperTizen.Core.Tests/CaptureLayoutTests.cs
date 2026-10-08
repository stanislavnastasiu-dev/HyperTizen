using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class CaptureLayoutTests
{
    private static void AssertPoint(CapturePoint point, double x, double y, Edge edge)
    {
        Assert.Equal(x, point.X, 6);
        Assert.Equal(y, point.Y, 6);
        Assert.Equal(edge, point.Edge);
    }

    [Fact]
    public void The_default_has_fourteen_points()
    {
        var layout = CaptureLayout.Default;

        Assert.Equal((4, 4, 3, 3), (layout.Top, layout.Bottom, layout.Left, layout.Right));
        Assert.Equal(14, layout.Points.Length);
    }

    [Fact]
    public void Points_are_ordered_top_right_bottom_left()
    {
        var edges = CaptureLayout.Default.Points.Select(point => point.Edge).ToArray();

        Assert.Equal(
            new[]
            {
                Edge.Top, Edge.Top, Edge.Top, Edge.Top,
                Edge.Right, Edge.Right, Edge.Right,
                Edge.Bottom, Edge.Bottom, Edge.Bottom, Edge.Bottom,
                Edge.Left, Edge.Left, Edge.Left
            },
            edges);
    }

    [Theory]
    [InlineData(4, 4, 3, 3)]
    [InlineData(16, 16, 12, 12)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 5, 0, 2)]
    [InlineData(3, 0, 0, 0)]
    public void The_spread_order_visits_every_point_once(int top, int bottom, int left, int right)
    {
        var layout = new CaptureLayout(top, bottom, left, right);

        Assert.Equal(Enumerable.Range(0, layout.Points.Length), layout.SpreadOrder.OrderBy(index => index));
    }

    [Fact]
    public void The_spread_order_pairs_points_from_different_edges()
    {
        var layout = CaptureLayout.Default;
        var edges = layout.SpreadOrder.Select(index => layout.Points[index].Edge).ToArray();

        for (int i = 0; i + 1 < edges.Length; i += 2)
            Assert.NotEqual(edges[i], edges[i + 1]);
    }

    [Fact]
    public void The_spread_order_does_not_walk_along_an_edge()
    {
        // Sixteen points on one edge: neighbors in the order are never neighbors on the screen.
        int[] order = new CaptureLayout(16, 0, 0, 0).SpreadOrder;

        for (int i = 0; i + 1 < order.Length; i++)
            Assert.True(Math.Abs(order[i] - order[i + 1]) > 1, order[i] + " then " + order[i + 1]);
    }

    [Fact]
    public void Top_and_bottom_run_left_to_right_evenly_spaced()
    {
        var points = CaptureLayout.Default.Points;

        AssertPoint(points[0], 0.125, 0.05, Edge.Top);
        AssertPoint(points[1], 0.375, 0.05, Edge.Top);
        AssertPoint(points[3], 0.875, 0.05, Edge.Top);
        AssertPoint(points[7], 0.125, 0.95, Edge.Bottom);
        AssertPoint(points[10], 0.875, 0.95, Edge.Bottom);
    }

    [Fact]
    public void Left_and_right_run_top_to_bottom_evenly_spaced()
    {
        var points = CaptureLayout.Default.Points;

        AssertPoint(points[4], 0.95, 1.0 / 6, Edge.Right);
        AssertPoint(points[5], 0.95, 0.5, Edge.Right);
        AssertPoint(points[6], 0.95, 5.0 / 6, Edge.Right);
        AssertPoint(points[11], 0.05, 1.0 / 6, Edge.Left);
        AssertPoint(points[13], 0.05, 5.0 / 6, Edge.Left);
    }

    [Fact]
    public void An_edge_with_no_zones_has_no_points()
    {
        var layout = new CaptureLayout(4, 0, 3, 3);

        Assert.Equal(10, layout.Points.Length);
        Assert.DoesNotContain(layout.Points, point => point.Edge == Edge.Bottom);
        Assert.Equal(0, layout.Count(Edge.Bottom));
    }

    [Fact]
    public void Offset_and_count_locate_each_edge_in_the_list()
    {
        var layout = new CaptureLayout(5, 2, 1, 3);

        Assert.Equal((0, 5), (layout.Offset(Edge.Top), layout.Count(Edge.Top)));
        Assert.Equal((5, 3), (layout.Offset(Edge.Right), layout.Count(Edge.Right)));
        Assert.Equal((8, 2), (layout.Offset(Edge.Bottom), layout.Count(Edge.Bottom)));
        Assert.Equal((10, 1), (layout.Offset(Edge.Left), layout.Count(Edge.Left)));
        Assert.Equal(Edge.Left, layout.Points[10].Edge);
    }

    [Theory]
    [InlineData(16, 16, 12, 12, true)]
    [InlineData(1, 0, 0, 0, true)]
    [InlineData(0, 0, 0, 1, true)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(17, 4, 3, 3, false)]
    [InlineData(4, 17, 3, 3, false)]
    [InlineData(4, 4, 13, 3, false)]
    [InlineData(4, 4, 3, 13, false)]
    [InlineData(-1, 4, 3, 3, false)]
    public void Limits(int top, int bottom, int left, int right, bool valid)
    {
        Assert.Equal(valid, CaptureLayout.IsValid(top, bottom, left, right));
    }

    [Fact]
    public void A_layout_that_is_not_valid_cannot_be_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureLayout(0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureLayout(17, 4, 3, 3));
    }
}
