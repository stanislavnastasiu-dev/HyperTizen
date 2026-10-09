using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class FrameEncoderTests
{
    // Color i is (i * 20 + 100, i * 20 + 200, i * 20 + 300): distinct for every zone of the largest layout.
    private static Rgb10[] Colors(CaptureLayout layout)
    {
        var colors = new Rgb10[layout.Points.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Rgb10(i * 20 + 100, i * 20 + 200, i * 20 + 300);
        return colors;
    }

    private static (int R, int G, int B) Pixel(byte[] rgb, int x, int y)
    {
        int offset = (y * FrameEncoder.Width + x) * 3;
        return (rgb[offset], rgb[offset + 1], rgb[offset + 2]);
    }

    // Captured channels are 10-bit; the image holds 8-bit ones, a quarter of the value.
    private static (int, int, int) Expected(int index) => ((index * 20 + 100) / 4, (index * 20 + 200) / 4, (index * 20 + 300) / 4);

    private static (int, int, int) PixelOf(CaptureLayout layout, int x, int y) =>
        Pixel(FrameEncoder.ToRgb(Colors(layout), layout), x, y);

    [Theory]
    [InlineData(8, 0, 0)]
    [InlineData(24, 0, 1)]
    [InlineData(40, 3, 2)]
    [InlineData(56, 3, 3)]
    public void Top_border_shows_the_top_zones_left_to_right(int x, int y, int zone)
    {
        Assert.Equal(Expected(zone), PixelOf(CaptureLayout.Default, x, y));
    }

    [Theory]
    [InlineData(8, 44, 7)]
    [InlineData(24, 44, 8)]
    [InlineData(40, 47, 9)]
    [InlineData(56, 47, 10)]
    public void Bottom_border_shows_the_bottom_zones_left_to_right(int x, int y, int zone)
    {
        Assert.Equal(Expected(zone), PixelOf(CaptureLayout.Default, x, y));
    }

    [Theory]
    [InlineData(0, 0, 11)]
    [InlineData(2, 8, 11)]
    [InlineData(1, 24, 12)]
    [InlineData(0, 47, 13)]
    public void Left_border_shows_the_left_zones_top_to_bottom_and_wins_the_corners(int x, int y, int zone)
    {
        Assert.Equal(Expected(zone), PixelOf(CaptureLayout.Default, x, y));
    }

    [Theory]
    [InlineData(63, 0, 4)]
    [InlineData(61, 8, 4)]
    [InlineData(62, 24, 5)]
    [InlineData(63, 47, 6)]
    public void Right_border_shows_the_right_zones_top_to_bottom_and_wins_the_corners(int x, int y, int zone)
    {
        Assert.Equal(Expected(zone), PixelOf(CaptureLayout.Default, x, y));
    }

    [Fact]
    public void The_zone_measured_at_the_bottom_left_colors_the_bottom_left_of_the_image()
    {
        var layout = CaptureLayout.Default;
        int zone = layout.Offset(Edge.Bottom);

        Assert.True(layout.Points[zone].X < 0.5 && layout.Points[zone].Y > 0.5);
        Assert.Equal(Expected(zone), PixelOf(layout, 8, 46));
    }

    [Fact]
    public void The_zone_measured_at_the_top_left_colors_the_top_of_the_left_border()
    {
        var layout = CaptureLayout.Default;
        int zone = layout.Offset(Edge.Left);

        Assert.True(layout.Points[zone].X < 0.5 && layout.Points[zone].Y < 0.5);
        Assert.Equal(Expected(zone), PixelOf(layout, 1, 8));
    }

    [Fact]
    public void Each_border_is_split_into_as_many_bands_as_its_edge_has_zones()
    {
        // 16 top zones: bands 4 pixels wide. 12 right zones: bands 4 pixels high. 2 bottom zones.
        var layout = new CaptureLayout(16, 2, 0, 12);

        Assert.Equal(Expected(0), PixelOf(layout, 3, 0));
        Assert.Equal(Expected(1), PixelOf(layout, 4, 0));
        Assert.Equal(Expected(14), PixelOf(layout, 59, 0));
        Assert.Equal(Expected(layout.Offset(Edge.Right)), PixelOf(layout, 62, 3));
        Assert.Equal(Expected(layout.Offset(Edge.Right) + 1), PixelOf(layout, 62, 4));
        Assert.Equal(Expected(layout.Offset(Edge.Right) + 11), PixelOf(layout, 62, 44));
        Assert.Equal(Expected(layout.Offset(Edge.Bottom)), PixelOf(layout, 31, 46));
        Assert.Equal(Expected(layout.Offset(Edge.Bottom) + 1), PixelOf(layout, 32, 46));
    }

    [Fact]
    public void A_border_whose_edge_has_no_zones_is_black()
    {
        var layout = new CaptureLayout(4, 0, 3, 3);

        Assert.Equal((0, 0, 0), PixelOf(layout, 32, 46));
    }

    [Fact]
    public void Without_left_zones_the_top_border_keeps_its_corner()
    {
        var layout = new CaptureLayout(16, 2, 0, 12);

        Assert.Equal(Expected(0), PixelOf(layout, 0, 0));
        Assert.Equal((0, 0, 0), PixelOf(layout, 1, 24));
    }

    [Theory]
    [InlineData(40, 12, 2)]
    [InlineData(24, 36, 8)]
    [InlineData(12, 24, 12)]
    [InlineData(52, 24, 5)]
    public void A_zone_colors_the_image_from_its_edge_to_the_middle(int x, int y, int zone)
    {
        Assert.Equal(Expected(zone), PixelOf(CaptureLayout.Default, x, y));
    }

    [Fact]
    public void No_pixel_is_black_when_every_edge_has_zones()
    {
        byte[] rgb = FrameEncoder.ToRgb(Colors(CaptureLayout.Default), CaptureLayout.Default);

        for (int y = 0; y < FrameEncoder.Height; y++)
            for (int x = 0; x < FrameEncoder.Width; x++)
                Assert.NotEqual((0, 0, 0), Pixel(rgb, x, y));
    }

    [Fact]
    public void The_part_of_an_edge_without_zones_is_black_to_the_middle()
    {
        var layout = new CaptureLayout(4, 0, 3, 3);

        Assert.Equal((0, 0, 0), PixelOf(layout, 32, 30));
        Assert.Equal(Expected(2), PixelOf(layout, 40, 12));
    }

    [Fact]
    public void Ten_bit_channels_are_scaled_to_a_byte()
    {
        var colors = Colors(CaptureLayout.Default);
        colors[0] = new Rgb10(1023, 512, 4);

        Assert.Equal((255, 128, 1), Pixel(FrameEncoder.ToRgb(colors, CaptureLayout.Default), 8, 0));
    }

    [Fact]
    public void Out_of_range_channels_are_limited_before_scaling()
    {
        var colors = Colors(CaptureLayout.Default);
        colors[0] = new Rgb10(4000, -5, 1024);

        Assert.Equal((255, 0, 255), Pixel(FrameEncoder.ToRgb(colors, CaptureLayout.Default), 8, 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(188, 47)]
    [InlineData(1023, 255)]
    public void To_byte_divides_by_four(int tenBit, int expected)
    {
        Assert.Equal(expected, FrameEncoder.ToByte(tenBit));
    }

    [Fact]
    public void Rejects_a_number_of_colors_that_does_not_match_the_layout()
    {
        Assert.Throws<ArgumentException>(() => FrameEncoder.ToRgb(new Rgb10[13], CaptureLayout.Default));
        Assert.Throws<ArgumentException>(() => FrameEncoder.ToRgb(new Rgb10[16], CaptureLayout.Default));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => FrameEncoder.ToRgb(null!, CaptureLayout.Default));
        Assert.Throws<ArgumentNullException>(() => FrameEncoder.ToRgb(new Rgb10[14], null!));
    }

    [Fact]
    public void Base64_output_is_a_png()
    {
        byte[] png = Convert.FromBase64String(FrameEncoder.ToBase64Png(Colors(CaptureLayout.Default), CaptureLayout.Default));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }
}
