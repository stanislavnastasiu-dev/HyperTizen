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
