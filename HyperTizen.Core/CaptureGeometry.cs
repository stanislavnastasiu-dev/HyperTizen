using System;

namespace HyperTizen.Core
{
    // Where on the screen, in pixels, the TV measures a capture point.
    public static class CaptureGeometry
    {
        // The first pixel of an area `density` pixels long, centered on `position` (0 to 1) of a
        // screen `size` pixels long, and kept inside the screen. Points of the outermost zones sit
        // close enough to the edge for the area to reach past it otherwise.
        public static int Origin(double position, int size, int density)
        {
            int origin = (int)(position * size) - density / 2;
            return Math.Max(0, Math.Min(origin, size - density - 1));
        }
    }
}
