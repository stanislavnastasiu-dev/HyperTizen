namespace HyperTizen.Core
{
    // Settings the capture loop reads on every frame, so changes apply without a restart.
    public sealed class CaptureOptions
    {
        // Frames per second; 0 means unlimited.
        public volatile int MaxFps;

        // Hyperion priority used for image, color and clear commands.
        public volatile byte Priority = 99;
    }
}
