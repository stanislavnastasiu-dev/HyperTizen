using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Wires the parts together. Hosts create one of these and forward their lifecycle events to it.
    public sealed class HyperTizenService : IControlActions
    {
        private const string EnabledKey = "enabled";
        private const string RpcServerKey = "rpcServer";
        private const string MaxFpsKey = "maxFps";
        private const string PriorityKey = "priority";
        private const string InstanceKey = "instance";
        private const string SleepCapKey = "captureSleepMs";
        private const string ZonesTopKey = "zonesTop";
        private const string ZonesBottomKey = "zonesBottom";
        private const string ZonesLeftKey = "zonesLeft";
        private const string ZonesRightKey = "zonesRight";

        private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(3);
        private static readonly string Version = ReadVersion();

        private readonly IScreenCapturer _capturer;
        private readonly ISettingsStore _settings;
        private readonly ILog _log;
        private readonly LastErrorLog _errors;
        private readonly CaptureOptions _options = new CaptureOptions();
        private readonly HyperionClient _client;
        private readonly CaptureService _capture;
        private readonly LedTester _ledTester;
        private readonly SsdpScanner _scanner;
        private readonly ControlServer _control;

        public HyperTizenService(string listenPrefix, IScreenCapturer capturer, ISettingsStore settings, ILog log, string staticRoot = null)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            // Errors from the Hyperion link and the capture loop are what status reports as lastError.
            _errors = new LastErrorLog(log);
            // An error about the link is no longer current once a connection is made.
            _client = new HyperionClient(_errors, null, _errors.Clear);
            _capture = new CaptureService(capturer, _client, _errors, null, _options, _errors.Clear);
            _ledTester = new LedTester(_client, _capture, _options);
            _scanner = new SsdpScanner(log);
            _control = new ControlServer(listenPrefix, settings, this, log, staticRoot);
        }

        public async Task StartAsync()
        {
            if (!_settings.Contains(EnabledKey)) _settings.Set(EnabledKey, "false");
            if (_settings.Contains(RpcServerKey)) _client.SetServer(_settings.Get(RpcServerKey));
            _options.MaxFps = int.Parse(StoredOrDefault(MaxFpsKey, "0"), CultureInfo.InvariantCulture);
            _options.Priority = byte.Parse(StoredOrDefault(PriorityKey, "99"), CultureInfo.InvariantCulture);
            _options.Layout = StoredLayout();
            _capturer.SleepMsCap = int.Parse(StoredOrDefault(SleepCapKey, "0"), CultureInfo.InvariantCulture);
            await _client.SetInstanceAsync(int.Parse(StoredOrDefault(InstanceKey, "0"), CultureInfo.InvariantCulture)).ConfigureAwait(false);

            _control.Start();
            if (IsEnabled()) await _capture.StartAsync().ConfigureAwait(false);
        }

        public async Task OnDisplayOnAsync()
        {
            if (IsEnabled()) await _capture.StartAsync().ConfigureAwait(false);
        }

        public Task OnDisplayOffAsync()
        {
            return _capture.StopAsync();
        }

        public async Task StopAsync()
        {
            await _capture.StopAsync().ConfigureAwait(false);
            await _control.StopAsync().ConfigureAwait(false);
        }

        Task<List<SSDPScanResultEvent.SSDPDevice>> IControlActions.ScanAsync()
        {
            return _scanner.ScanAsync();
        }

        bool IControlActions.IsValidConfig(string key, string value)
        {
            if (!IsZoneKey(key)) return IsValid(key, value);

            // A zone count is judged together with the other three: the last zone cannot be removed.
            int count;
            return IsZoneCount(key, value, out count) && LayoutWith(key, count) != null;
        }

        async Task IControlActions.OnConfigChangedAsync(string key, string value)
        {
            if (IsZoneKey(key))
            {
                // The capture loop picks the new layout up on its next frame.
                _options.Layout = StoredLayout();
                return;
            }

            switch (key)
            {
                case RpcServerKey:
                    // Errors about the previous server say nothing about the new one.
                    _errors.Clear();
                    _client.SetServer(value);
                    break;

                case EnabledKey:
                    if (bool.Parse(value)) await _capture.StartAsync().ConfigureAwait(false);
                    else await _capture.StopAsync().ConfigureAwait(false);
                    break;

                case MaxFpsKey:
                    _options.MaxFps = int.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case SleepCapKey:
                    _capturer.SleepMsCap = int.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case PriorityKey:
                    // The capture loop clears the old priority before its next frame.
                    _options.Priority = byte.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case InstanceKey:
                    await SwitchInstanceAsync(int.Parse(value, CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    break;
            }
        }

        // The instance being left keeps showing the last image unless it is cleared first.
        private async Task SwitchInstanceAsync(int instance)
        {
            await _client.SendClearAsync(_options.Priority).ConfigureAwait(false);
            await _client.SetInstanceAsync(instance).ConfigureAwait(false);
        }

        async Task IControlActions.OnConfigDeletedAsync(string key)
        {
            if (IsZoneKey(key))
            {
                // The capture loop picks the new layout up on its next frame.
                _options.Layout = StoredLayout();
                return;
            }

            switch (key)
            {
                case RpcServerKey:
                    // Without a server there is nothing to capture for.
                    _settings.Set(EnabledKey, "false");
                    await _capture.StopAsync().ConfigureAwait(false);
                    _client.SetServer(null);
                    _errors.Clear();
                    break;

                case MaxFpsKey:
                    _options.MaxFps = 0;
                    break;

                case PriorityKey:
                    _options.Priority = 99;
                    break;

                case InstanceKey:
                    await SwitchInstanceAsync(0).ConfigureAwait(false);
                    break;
            }
        }

        StatusResultEvent IControlActions.GetStatus()
        {
            return new StatusResultEvent
            {
                version = Version,
                enabled = IsEnabled(),
                rpcServer = _settings.Contains(RpcServerKey) ? _settings.Get(RpcServerKey) : null,
                connected = _client.IsConnected,
                capture = _capture.State,
                // A refusal stays true for as long as the server keeps refusing; other errors pass.
                lastError = _client.Rejection ?? _errors.LastError,
                frameMs = _capture.FrameMs,
                fps = _capture.Fps,
                captureDetails = _capturer.Diagnostics
            };
        }

        async Task<TestLedsResultEvent> IControlActions.TestLedsAsync()
        {
            TestLedsResultEvent result = await _ledTester.RunAsync().ConfigureAwait(false);
            if (result.ok) _errors.Clear();
            return result;
        }

        async Task<PreviewResultEvent> IControlActions.GetPreviewAsync()
        {
            PreviewFrame frame = await _capture.GetPreviewAsync(PreviewTimeout).ConfigureAwait(false);
            if (frame.Colors == null) return new PreviewResultEvent(false, null, frame.Error);

            var colors = new int[frame.Colors.Length][];
            var points = new PreviewPoint[frame.Colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Rgb10 color = frame.Colors[i];
                colors[i] = new int[] { FrameEncoder.ToByte(color.R), FrameEncoder.ToByte(color.G), FrameEncoder.ToByte(color.B) };

                CapturePoint point = frame.Layout.Points[i];
                points[i] = new PreviewPoint(point.X, point.Y, point.Edge.ToString().ToLowerInvariant());
            }
            return new PreviewResultEvent(true, colors, null, points);
        }

        private static bool IsValid(string key, string value)
        {
            switch (key)
            {
                case EnabledKey:
                    return value == "true" || value == "false";

                case MaxFpsKey:
                    // 0 is unlimited.
                    int fps;
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out fps) && fps <= 60;

                case SleepCapKey:
                    // 0 keeps the device's own settle time; otherwise an upper bound in milliseconds.
                    int sleep;
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out sleep) && sleep <= 1000;

                case PriorityKey:
                    int priority;
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out priority)
                        && priority >= 1 && priority <= 253;

                case InstanceKey:
                    int instance;
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out instance) && instance <= 254;

                case RpcServerKey:
                    Uri server;
                    return Uri.TryCreate(value, UriKind.Absolute, out server) && (server.Scheme == "ws" || server.Scheme == "wss");

                default:
                    // Anyone on the network can reach the control server; it stores only what it knows.
                    return false;
            }
        }

        // A stored value written by an older version may not be valid; fall back instead of failing to start.
        private string StoredOrDefault(string key, string fallback)
        {
            if (!_settings.Contains(key)) return fallback;
            string stored = _settings.Get(key);
            return IsValid(key, stored) ? stored : fallback;
        }

        private static bool IsZoneKey(string key)
        {
            return key == ZonesTopKey || key == ZonesBottomKey || key == ZonesLeftKey || key == ZonesRightKey;
        }

        private static bool IsZoneCount(string key, string value, out int count)
        {
            int limit = key == ZonesTopKey || key == ZonesBottomKey ? CaptureLayout.MaxTopBottom : CaptureLayout.MaxLeftRight;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count <= limit;
        }

        private static int DefaultZones(string key)
        {
            switch (key)
            {
                case ZonesTopKey: return CaptureLayout.Default.Top;
                case ZonesBottomKey: return CaptureLayout.Default.Bottom;
                case ZonesLeftKey: return CaptureLayout.Default.Left;
                default: return CaptureLayout.Default.Right;
            }
        }

        // A stored count that is missing or not valid counts as the default for its edge.
        private int StoredZones(string key)
        {
            int count;
            return _settings.Contains(key) && IsZoneCount(key, _settings.Get(key), out count) ? count : DefaultZones(key);
        }

        // The layout the stored counts give with one of them replaced; null when that is not a layout.
        private CaptureLayout LayoutWith(string key, int count)
        {
            int top = key == ZonesTopKey ? count : StoredZones(ZonesTopKey);
            int bottom = key == ZonesBottomKey ? count : StoredZones(ZonesBottomKey);
            int left = key == ZonesLeftKey ? count : StoredZones(ZonesLeftKey);
            int right = key == ZonesRightKey ? count : StoredZones(ZonesRightKey);
            return CaptureLayout.IsValid(top, bottom, left, right) ? new CaptureLayout(top, bottom, left, right) : null;
        }

        private CaptureLayout StoredLayout()
        {
            return LayoutWith(null, 0) ?? CaptureLayout.Default;
        }

        private bool IsEnabled()
        {
            return _settings.Contains(EnabledKey)
                && bool.TryParse(_settings.Get(EnabledKey), out bool enabled)
                && enabled;
        }

        private static string ReadVersion()
        {
            Assembly assembly = typeof(HyperTizenService).GetTypeInfo().Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute != null ? attribute.InformationalVersion : assembly.GetName().Version.ToString();
        }
    }
}
