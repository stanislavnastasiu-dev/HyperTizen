using System.Diagnostics;
using HyperTizen.Core;

namespace HyperTizen.Desktop;

// Produces a rotating rainbow in place of real screen colors, at about 30 frames per second.
public sealed class SimulatedCapturer : IScreenCapturer
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public bool Initialize() => true;

    public int BatchSize => 0;

    public string? Diagnostics => null;

    public Rgb10[] Capture(IReadOnlyList<CapturePoint> points)
    {
        Thread.Sleep(33);

        // One turn of the rainbow spread over the points, in the order they go around the screen.
        double baseHue = _clock.Elapsed.TotalSeconds * 60.0;
        var colors = new Rgb10[points.Count];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = FromHue((baseHue + i * 360.0 / colors.Length) % 360.0);
        return colors;
    }

    // Full-saturation, full-brightness color for a hue in degrees, on the TV's 0..1023 scale.
    private static Rgb10 FromHue(double hue)
    {
        const int Max = 1023;
        double sector = hue / 60.0;
        int rising = (int)(Max * (sector - Math.Floor(sector)));
        int falling = Max - rising;

        return (int)Math.Floor(sector) switch
        {
            0 => new Rgb10(Max, rising, 0),
            1 => new Rgb10(falling, Max, 0),
            2 => new Rgb10(0, Max, rising),
            3 => new Rgb10(0, falling, Max),
            4 => new Rgb10(rising, 0, Max),
            _ => new Rgb10(Max, 0, falling)
        };
    }
}
