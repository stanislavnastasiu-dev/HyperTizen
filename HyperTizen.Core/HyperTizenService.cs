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

        private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(3);
        private static readonly string Version = ReadVersion();

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
            return IsValid(key, value);
        }

        async Task IControlActions.OnConfigChangedAsync(string key, string value)
        {
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

                case PriorityKey:
                    // The capture loop clears the old priority before its next frame.
                    _options.Priority = byte.Parse(value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        async Task IControlActions.OnConfigDeletedAsync(string key)
        {
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
                lastError = _errors.LastError
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
            for (int i = 0; i < colors.Length; i++)
            {
                Rgb10 color = frame.Colors[i];
                colors[i] = new[] { ClampToByte(color.R), ClampToByte(color.G), ClampToByte(color.B) };
            }
            return new PreviewResultEvent(true, colors, null);
        }

        private static bool IsValid(string key, string value)
        {
            switch (key)
            {
                case EnabledKey:
                    return value == "true" || value == "false";

                case MaxFpsKey:
                    return value == "0" || value == "10" || value == "20" || value == "30";

                case PriorityKey:
                    int priority;
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out priority)
                        && priority >= 1 && priority <= 253;

                case RpcServerKey:
                    return !string.IsNullOrWhiteSpace(value);

                default:
                    return true;
            }
        }

        // A stored value written by an older version may not be valid; fall back instead of failing to start.
        private string StoredOrDefault(string key, string fallback)
        {
            if (!_settings.Contains(key)) return fallback;
            string stored = _settings.Get(key);
            return IsValid(key, stored) ? stored : fallback;
        }

        private bool IsEnabled()
        {
            return _settings.Contains(EnabledKey)
                && bool.TryParse(_settings.Get(EnabledKey), out bool enabled)
                && enabled;
        }

        private static int ClampToByte(int channel)
        {
            return Math.Max(0, Math.Min(channel, 255));
        }

        private static string ReadVersion()
        {
            Assembly assembly = typeof(HyperTizenService).GetTypeInfo().Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute != null ? attribute.InformationalVersion : assembly.GetName().Version.ToString();
        }
    }
}
