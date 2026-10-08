using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class CaptureGeometryTests
{
    [Fact]
    public void A_measured_area_is_centered_on_its_point()
    {
        Assert.Equal(930, CaptureGeometry.Origin(0.5, 1920, 60));
        Assert.Equal(976, CaptureGeometry.Origin(0.95, 1080, 100));
    }

    [Fact]
    public void An_area_that_would_start_before_the_screen_starts_at_zero()
    {
        // The first of 16 zones on a 1920 wide screen, measured over 192 pixels.
        Assert.Equal(0, CaptureGeometry.Origin(1.0 / 32, 1920, 192));
    }

    [Fact]
    public void An_area_that_would_run_past_the_screen_ends_one_pixel_inside_it()
    {
        Assert.Equal(1727, CaptureGeometry.Origin(31.0 / 32, 1920, 192));
        Assert.Equal(979, CaptureGeometry.Origin(0.99, 1080, 100));
    }

    [Fact]
    public void An_area_larger_than_the_screen_starts_at_zero()
    {
        Assert.Equal(0, CaptureGeometry.Origin(0.5, 100, 400));
    }
}
