using System;
using System.Collections.Generic;

namespace HyperTizen.Core
{
    // One captured color. Channels are 10-bit (0..1023) as delivered by the TV.
    public struct Rgb10
    {
        public int R;
        public int G;
        public int B;

        public Rgb10(int r, int g, int b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    public interface IScreenCapturer
    {
        // False when the device cannot capture; the capture loop does not start.
        bool Initialize();

        // How many points the device measures in one go, known once Initialize has run. Zero when
        // it takes any number at once.
        int BatchSize { get; }

        // What the device says about itself and how long its latest capture took, for the status
        // reply. Null when there is nothing to tell.
        string Diagnostics { get; }

        // Upper bound, in milliseconds, on the settle time waited between setting measure points and
        // reading them. 0 leaves the device's own figure untouched; a smaller value overrides it to
        // trade accuracy for speed. Read live, so a change applies on the next frame.
        int SleepMsCap { get; set; }

        // One color per point, in the same order. Blocks for as long as the device needs.
        Rgb10[] Capture(IReadOnlyList<CapturePoint> points);
    }

    public interface ISettingsStore
    {
        bool Contains(string key);
        string Get(string key);
        void Set(string key, string value);

        // Does nothing when the key is absent.
        void Remove(string key);
    }

    public interface ILog
    {
        void Info(string message);
        void Error(string message, Exception exception = null);
    }
}
