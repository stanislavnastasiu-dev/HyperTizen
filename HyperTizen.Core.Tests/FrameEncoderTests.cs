using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class FrameEncoderTests
{
    private static Rgb10[] Colors()
    {
        var colors = new Rgb10[16];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Rgb10(i * 10 + 1, i * 10 + 2, i * 10 + 3);
        return colors;
    }

    private static (int R, int G, int B) Pixel(byte[] rgb, int x, int y)
    {
        int offset = (y * FrameEncoder.Width + x) * 3;
        return (rgb[offset], rgb[offset + 1], rgb[offset + 2]);
    }

    private static (int, int, int) Expected(int index) => (index * 10 + 1, index * 10 + 2, index * 10 + 3);

    [Theory]
    [InlineData(8, 0, 0)]
    [InlineData(24, 0, 1)]
    [InlineData(40, 3, 2)]
    [InlineData(56, 3, 3)]
    public void Top_edge_uses_colors_0_to_3(int x, int y, int colorIndex)
    {
        Assert.Equal(Expected(colorIndex), Pixel(FrameEncoder.ToRgb(Colors()), x, y));
    }

    [Theory]
    [InlineData(8, 44, 7)]
    [InlineData(24, 44, 8)]
    [InlineData(40, 47, 9)]
    [InlineData(56, 47, 10)]
    public void Bottom_edge_uses_colors_7_to_10(int x, int y, int colorIndex)
    {
        Assert.Equal(Expected(colorIndex), Pixel(FrameEncoder.ToRgb(Colors()), x, y));
    }

    [Theory]
    [InlineData(0, 0, 11)]
    [InlineData(2, 8, 11)]
    [InlineData(1, 24, 12)]
    [InlineData(0, 47, 13)]
    public void Left_edge_uses_colors_11_to_13_and_wins_the_corners(int x, int y, int colorIndex)
    {
        Assert.Equal(Expected(colorIndex), Pixel(FrameEncoder.ToRgb(Colors()), x, y));
    }

    [Theory]
    [InlineData(63, 0, 4)]
    [InlineData(61, 8, 4)]
    [InlineData(62, 24, 5)]
    [InlineData(63, 47, 6)]
    public void Right_edge_uses_colors_4_to_6_and_wins_the_corners(int x, int y, int colorIndex)
    {
        Assert.Equal(Expected(colorIndex), Pixel(FrameEncoder.ToRgb(Colors()), x, y));
    }

    [Fact]
    public void Interior_is_black()
    {
        Assert.Equal((0, 0, 0), Pixel(FrameEncoder.ToRgb(Colors()), 32, 24));
    }

    [Fact]
    public void Channels_are_clamped_to_a_byte()
    {
        var colors = Colors();
        colors[0] = new Rgb10(1023, 300, -5);

        Assert.Equal((255, 255, 0), Pixel(FrameEncoder.ToRgb(colors), 8, 0));
    }

    [Fact]
    public void Rejects_too_few_colors()
    {
        Assert.Throws<ArgumentException>(() => FrameEncoder.ToRgb(new Rgb10[13]));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => FrameEncoder.ToRgb(null!));
    }

    [Fact]
    public void Base64_output_is_a_png()
    {
        byte[] png = Convert.FromBase64String(FrameEncoder.ToBase64Png(Colors()));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }
}
