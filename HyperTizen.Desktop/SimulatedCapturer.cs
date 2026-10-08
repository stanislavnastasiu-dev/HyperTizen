using System.Diagnostics;
using HyperTizen.Core;

namespace HyperTizen.Desktop;

// Produces a rotating rainbow in place of real screen colors, at about 30 frames per second.
public sealed class SimulatedCapturer : IScreenCapturer
{
    private const int ColorCount = 16;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public bool Initialize() => true;

    public Rgb10[] Capture()
    {
        Thread.Sleep(33);

        double baseHue = _clock.Elapsed.TotalSeconds * 60.0;
        var colors = new Rgb10[ColorCount];
        for (int i = 0; i < ColorCount; i++)
            colors[i] = FromHue((baseHue + i * 360.0 / ColorCount) % 360.0);
        return colors;
    }

    // Full-saturation, full-brightness color for a hue in degrees. Channels are 0..255
    // because the frame encoder clamps to a byte rather than scaling.
    private static Rgb10 FromHue(double hue)
    {
        double sector = hue / 60.0;
        int rising = (int)(255 * (sector - Math.Floor(sector)));
        int falling = 255 - rising;

        return (int)Math.Floor(sector) switch
        {
            0 => new Rgb10(255, rising, 0),
            1 => new Rgb10(falling, 255, 0),
            2 => new Rgb10(0, 255, rising),
            3 => new Rgb10(0, falling, 255),
            4 => new Rgb10(rising, 0, 255),
            _ => new Rgb10(255, 0, falling)
        };
    }
}
