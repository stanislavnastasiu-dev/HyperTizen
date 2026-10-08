using System.Collections.Generic;

namespace HyperTizen.Core
{
    public enum Event
    {
        SetConfig,
        ReadConfig,
        ReadConfigResult,
        ScanSSDP,
        SSDPScanResult,
        GetStatus,
        StatusResult,
        TestLeds,
        TestLedsResult,
        GetPreview,
        PreviewResult,
        DeleteConfig
    }

    public class BasicEvent
    {
        public Event Event { get; set; }
    }

    public class SetConfigEvent : BasicEvent
    {
        public string key { get; set; }
        public string value { get; set; }
    }

    public class ReadConfigEvent : BasicEvent
    {
        public string key { get; set; }
    }

    public class DeleteConfigEvent : BasicEvent
    {
        public string key { get; set; }
    }

    public class ReadConfigResultEvent : BasicEvent
    {
        public ReadConfigResultEvent(bool error, string key, object value)
        {
            this.Event = Event.ReadConfigResult;
            this.error = error;
            this.value = value;
            this.key = key;
        }

        public bool error { get; set; }
        public string key { get; set; }
        public object value { get; set; }
    }

    public class SSDPScanResultEvent : BasicEvent
    {
        public SSDPScanResultEvent(List<SSDPDevice> devices)
        {
            this.devices = devices;
            this.Event = Event.SSDPScanResult;
        }

        public List<SSDPDevice> devices { get; set; }

        public class SSDPDevice
        {
            public string FriendlyName { get; set; }
            public string UrlBase { get; set; }

            public SSDPDevice(string friendlyName, string urlBase)
            {
                FriendlyName = friendlyName;
                UrlBase = urlBase;
            }
        }
    }

    public class StatusResultEvent : BasicEvent
    {
        public StatusResultEvent()
        {
            this.Event = Event.StatusResult;
        }

        public string version { get; set; }
        public bool enabled { get; set; }
        public string rpcServer { get; set; }
        public bool connected { get; set; }
        // "running", "stopped", "unsupported" or "unknown"
        public string capture { get; set; }
        public string lastError { get; set; }
        // Milliseconds the latest frame took; null when no frame was sent lately.
        public int? frameMs { get; set; }
        // Frames sent per second over the last few seconds.
        public double fps { get; set; }
        // What the capturer reports about the device and its latest capture; null when nothing.
        public string captureDetails { get; set; }
    }

    public class TestLedsResultEvent : BasicEvent
    {
        public TestLedsResultEvent(bool ok, string error)
        {
            this.Event = Event.TestLedsResult;
            this.ok = ok;
            this.error = error;
        }

        public bool ok { get; set; }
        public string error { get; set; }
    }

    public class PreviewResultEvent : BasicEvent
    {
        public PreviewResultEvent(bool ok, int[][] colors, string error, PreviewPoint[] points = null)
        {
            this.Event = Event.PreviewResult;
            this.ok = ok;
            this.colors = colors;
            this.error = error;
            this.points = points;
        }

        public bool ok { get; set; }
        // One [r, g, b] per capture point, each 0..255.
        public int[][] colors { get; set; }
        public string error { get; set; }
        // Where each color was measured, in the same order as colors.
        public PreviewPoint[] points { get; set; }
    }

    public class PreviewPoint
    {
        public PreviewPoint(double x, double y, string edge)
        {
            this.x = x;
            this.y = y;
            this.edge = edge;
        }

        // 0..1 of the screen width and height.
        public double x { get; set; }
        public double y { get; set; }
        // "top", "right", "bottom" or "left".
        public string edge { get; set; }
    }

    public class ImageCommand
    {
        public ImageCommand(string image, byte priority = 99)
        {
            imagedata = image;
            this.priority = priority;
        }

        public string command { get; set; } = "image";
        public string imagedata { get; set; }
        public string name { get; set; } = "HyperTizen Data";
        public string format { get; set; } = "auto";
        public byte priority { get; set; }
        public string origin { get; set; } = "HyperTizen";
    }

    public class ClearCommand
    {
        public ClearCommand(byte priority = 99)
        {
            this.priority = priority;
        }

        public string command { get; set; } = "clear";
        public byte priority { get; set; }
    }

    public class ColorCommand
    {
        public ColorCommand(int r, int g, int b, byte priority)
        {
            color = new[] { r, g, b };
            this.priority = priority;
        }

        public string command { get; set; } = "color";
        public int[] color { get; set; }
        public byte priority { get; set; }
        public string origin { get; set; } = "HyperTizen";
    }
}
