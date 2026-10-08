using System;
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
        private readonly ILog _log;
        private readonly bool _isTizen7OrHigher;
        private Condition _condition;
        private bool _notified;

        private readonly CapturePoint[] _capturedPoints = new CapturePoint[] {
            new CapturePoint(0.21, 0.05),
            new CapturePoint(0.45, 0.05),
            new CapturePoint(0.7, 0.05),
            new CapturePoint(0.93, 0.07),
            new CapturePoint(0.95, 0.275),
            new CapturePoint(0.95, 0.5),
            new CapturePoint(0.95, 0.8),
            new CapturePoint(0.79, 0.95),
            new CapturePoint(0.65, 0.95),
            new CapturePoint(0.35, 0.95),
            new CapturePoint(0.15, 0.95),
            new CapturePoint(0.05, 0.725),
            new CapturePoint(0.05, 0.4),
            new CapturePoint(0.05, 0.2),
            new CapturePoint(0.35, 0.5),
            new CapturePoint(0.65, 0.5)
        };

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition(out Condition unknown);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition(int i, int x, int y);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel(int i, out Color color);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition7(out Condition unknown);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_set_rgb_measure_position")]
        private static extern int MeasurePosition7(int i, int x, int y);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel7(int i, out Color color);

        public VideoEnhanceCapturer(ILog log)
        {
            _log = log;

            string version;
            Information.TryGetValue("http://tizen.org/feature/platform.version", out version);
            _log.Info("Platform version: " + version);
            _isTizen7OrHigher = version != null && int.Parse(version.Split('.')[0]) >= 7;
        }

        public bool Initialize()
        {
            int res = -1;
            try
            {
                if (!_isTizen7OrHigher)
                {
                    res = MeasureCondition(out _condition);
                } else
                {
                    res = MeasureCondition7(out _condition);
                }
            } catch (Exception ex)
            {
                _log.Error("The video enhancement library is not usable", ex);

                // Tell the user once, not every time capture is tried again.
                if (_notified) return false;
                _notified = true;

                Notification notification = new Notification
                {
                    Title = "HyperTizen",
                    Content = "Your TV does not support the required functions for HyperTizen.",
                    Count = 1
                };

                NotificationManager.Post(notification);
            }

            _log.Info("Measure condition result " + res + ": points=" + _condition.ScreenCapturePoints
                + " size=" + _condition.Width + "x" + _condition.Height
                + " density=" + _condition.PixelDensityX + "x" + _condition.PixelDensityY
                + " sleep=" + _condition.SleepMS);
            return res >= 0;
        }

        public Rgb10[] Capture()
        {
            Rgb10[] colorData = new Rgb10[_capturedPoints.Length];

            int i = 0;
            while (i < _capturedPoints.Length)
            {
                if (_condition.ScreenCapturePoints == 0) break;
                for (int j = 0; j < _condition.ScreenCapturePoints; j++)
                {
                    int x = (int)(_capturedPoints[i].X * (double)_condition.Width) - _condition.PixelDensityX / 2;
                    int y = (int)(_capturedPoints[i].Y * (double)_condition.Height) - _condition.PixelDensityY / 2;
                    x = (x >= _condition.Width - _condition.PixelDensityX) ? _condition.Width - (_condition.PixelDensityX + 1) : x;
                    y = (y >= _condition.Height - _condition.PixelDensityY) ? (_condition.Height - _condition.PixelDensityY + 1) : y;
                    int res;
                    if (!_isTizen7OrHigher)
                    {
                        res = MeasurePosition(j, x, y);
                    } else
                    {
                        res = MeasurePosition7(j, x, y);
                    }

                    i++;
                    if (res < 0)
                    {
                        // This should not happen, handle it.
                    }
                }

                if (_condition.SleepMS > 0)
                {
                    Thread.Sleep(_condition.SleepMS);
                }
                int k = 0;
                while (k < _condition.ScreenCapturePoints)
                {
                    Color color;

                    int res;

                    if (!_isTizen7OrHigher)
                    {
                        res = MeasurePixel(k, out color);
                    } else
                    {
                        res = MeasurePixel7(k, out color);
                    }

                    if (res < 0)
                    {
                        // This should not happen, handle it.
                    } else
                    {
                        bool invalidColorData = color.R > 1023 || color.G > 1023 || color.B > 1023;

                        if (invalidColorData)
                        {
                            // This should not happen, handle it.
                        } else
                        {
                            colorData[i - _condition.ScreenCapturePoints + k] = new Rgb10(color.R, color.G, color.B);
                            k++;
                        }
                    }
                }
            }
            return colorData;
        }

        private struct Color
        {
            public int R;
            public int G;
            public int B;
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

        private struct CapturePoint
        {
            public CapturePoint(double x, double y) {
                this.X = x;
                this.Y = y;
            }

            public double X;
            public double Y;
        }
    }
}
