using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using HyperTizen.Core;
using Tizen.Applications.Notifications;
using Tizen.System;

namespace HyperTizen
{
    // Reads screen colors through the TV's video enhancement library.
    public class VideoEnhanceCapturer : IScreenCapturer
    {
        private const string Library = "/usr/lib/libvideoenhance.so";

        // A point that keeps failing must not hold the capture loop forever.
        private const int MaxPixelReadAttempts = 50;

        private delegate int ConditionCall(out Condition condition);
        private delegate int PositionCall(int index, int x, int y);
        private delegate int PixelCall(int index, out Color color);

        // The same three functions exist under different names depending on the TV's software.
        private sealed class Api
        {
            public string Name;
            public ConditionCall Condition;
            public PositionCall Position;
            public PixelCall Pixel;
        }

        private static readonly Api Legacy = new Api { Name = "cs_ve", Condition = ConditionCs, Position = PositionCs, Pixel = PixelCs };
        private static readonly Api Tizen7 = new Api { Name = "ve", Condition = ConditionVe, Position = PositionVe, Pixel = PixelVe };
        // Seen on a 2024 TV reporting platform version 9.0.
        private static readonly Api Ppi = new Api { Name = "ppi_ve", Condition = ConditionPpi, Position = PositionPpi, Pixel = PixelPpi };

        private readonly ILog _log;
        private readonly Api[] _candidates;
        private Api _api;
        private Condition _condition;
        private bool _notified;
        private volatile string _lastCapture = "";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_get_rgb_measure_condition")]
        private static extern int ConditionCs(out Condition condition);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_set_rgb_measure_position")]
        private static extern int PositionCs(int index, int x, int y);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_get_rgb_measure_pixel")]
        private static extern int PixelCs(int index, out Color color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_get_rgb_measure_condition")]
        private static extern int ConditionVe(out Condition condition);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_set_rgb_measure_position")]
        private static extern int PositionVe(int index, int x, int y);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_get_rgb_measure_pixel")]
        private static extern int PixelVe(int index, out Color color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ppi_ve_get_rgb_measure_condition")]
        private static extern int ConditionPpi(out Condition condition);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ppi_ve_set_rgb_measure_position")]
        private static extern int PositionPpi(int index, int x, int y);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ppi_ve_get_rgb_measure_pixel")]
        private static extern int PixelPpi(int index, out Color color);

        public VideoEnhanceCapturer(ILog log)
        {
            _log = log;

            string version;
            Information.TryGetValue("http://tizen.org/feature/platform.version", out version);
            _log.Info("Platform version: " + version);

            int major;
            bool tizen7OrHigher = version != null && int.TryParse(version.Split('.')[0], out major) && major >= 7;
            // Most likely name first; the others are tried when a TV does not have it.
            _candidates = tizen7OrHigher ? new[] { Tizen7, Ppi, Legacy } : new[] { Legacy, Tizen7, Ppi };
        }

        public bool Initialize()
        {
            Exception missing = null;
            foreach (Api candidate in _candidates)
            {
                int res;
                try
                {
                    res = candidate.Condition(out _condition);
                }
                catch (EntryPointNotFoundException ex)
                {
                    missing = ex;
                    continue;
                }
                catch (DllNotFoundException ex)
                {
                    missing = ex;
                    break;
                }

                _api = candidate;
                _log.Info("Measure condition (" + candidate.Name + ") result " + res + ": points=" + _condition.ScreenCapturePoints
                    + " size=" + _condition.Width + "x" + _condition.Height
                    + " density=" + _condition.PixelDensityX + "x" + _condition.PixelDensityY
                    + " sleep=" + _condition.SleepMS);
                return res >= 0;
            }

            _log.Error("The video enhancement library is not usable", missing);

            // Tell the user once, not every time capture is tried again.
            if (!_notified)
            {
                _notified = true;
                NotificationManager.Post(new Notification
                {
                    Title = "HyperTizen",
                    Content = "Your TV does not support the required functions for HyperTizen.",
                    Count = 1
                });
            }
            return false;
        }

        public int BatchSize
        {
            get { return _condition.ScreenCapturePoints; }
        }

        public string Diagnostics
        {
            get
            {
                if (_api == null) return null;
                return _api.Name + ": points=" + _condition.ScreenCapturePoints
                    + " sleep=" + _condition.SleepMS
                    + " size=" + _condition.Width + "x" + _condition.Height
                    + " density=" + _condition.PixelDensityX + "x" + _condition.PixelDensityY
                    + _lastCapture;
            }
        }

        public Rgb10[] Capture(IReadOnlyList<CapturePoint> points)
        {
            Rgb10[] colorData = new Rgb10[points.Count];
            // Where the time of this capture goes, for Diagnostics.
            var clock = Stopwatch.StartNew();
            long positionTicks = 0;
            long waitTicks = 0;
            long readTicks = 0;
            int reads = 0;

            // The TV measures a few points at a time: set their positions, wait, then read them back.
            int i = 0;
            while (i < points.Count)
            {
                if (_condition.ScreenCapturePoints <= 0) break;

                int batch = Math.Min(_condition.ScreenCapturePoints, points.Count - i);
                long started = clock.ElapsedTicks;
                for (int j = 0; j < batch; j++)
                {
                    int x = CaptureGeometry.Origin(points[i + j].X, _condition.Width, _condition.PixelDensityX);
                    int y = CaptureGeometry.Origin(points[i + j].Y, _condition.Height, _condition.PixelDensityY);

                    int res = _api.Position(j, x, y);
                    if (res < 0) throw new InvalidOperationException("Setting capture point " + (i + j) + " failed with " + res + ".");
                }

                long positioned = clock.ElapsedTicks;

                if (_condition.SleepMS > 0)
                {
                    Thread.Sleep(_condition.SleepMS);
                }
                long waited = clock.ElapsedTicks;

                for (int k = 0; k < batch; k++)
                {
                    Color color = default(Color);
                    int res = -1;
                    bool valid = false;
                    for (int attempt = 0; attempt < MaxPixelReadAttempts && !valid; attempt++)
                    {
                        res = _api.Pixel(k, out color);
                        reads++;
                        valid = res >= 0 && color.R <= 1023 && color.G <= 1023 && color.B <= 1023;
                    }

                    if (!valid)
                        throw new InvalidOperationException("Reading capture point " + (i + k) + " failed with " + res
                            + " (" + color.R + ", " + color.G + ", " + color.B + ").");

                    colorData[i + k] = new Rgb10(color.R, color.G, color.B);
                }

                positionTicks += positioned - started;
                waitTicks += waited - positioned;
                readTicks += clock.ElapsedTicks - waited;
                i += batch;
            }

            _lastCapture = "; last capture of " + points.Count + " points: position " + Milliseconds(positionTicks)
                + " ms, wait " + Milliseconds(waitTicks) + " ms, read " + Milliseconds(readTicks) + " ms in " + reads + " reads";
            return colorData;
        }

        private static string Milliseconds(long ticks)
        {
            return (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // Newer TVs write four values here, older ones three. The struct must hold four either way:
        // with room for only three, the fourth lands on whatever sits next to it in memory.
#pragma warning disable CS0649 // These fields are filled in by the TV's library, not by C# code.
        private struct Color
        {
            public int R;
            public int G;
            public int B;
            public int Reserved;
        }

        private struct Condition
        {
            public int ScreenCapturePoints;

            public int PixelDensityX;

            public int PixelDensityY;

            public int SleepMS;

            public int Width;

            public int Height;
        }
#pragma warning restore CS0649
    }
}
