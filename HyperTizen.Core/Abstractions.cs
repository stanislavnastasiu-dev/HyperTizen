using System;

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

        // One frame of colors. Blocks for as long as the device needs.
        Rgb10[] Capture();
    }

    public interface ISettingsStore
    {
        bool Contains(string key);
        string Get(string key);
        void Set(string key, string value);
    }

    public interface ILog
    {
        void Info(string message);
        void Error(string message, Exception exception = null);
    }
}
