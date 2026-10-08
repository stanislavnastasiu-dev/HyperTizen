using System;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Wires the parts together. Hosts create one of these and forward their lifecycle events to it.
    public sealed class HyperTizenService
    {
        private const string EnabledKey = "enabled";
        private const string RpcServerKey = "rpcServer";

        private readonly ISettingsStore _settings;
        private readonly ILog _log;
        private readonly HyperionClient _client;
        private readonly CaptureService _capture;
        private readonly ControlServer _control;

        public HyperTizenService(string listenPrefix, IScreenCapturer capturer, ISettingsStore settings, ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _client = new HyperionClient(log);
            _capture = new CaptureService(capturer, _client, log);
            var scanner = new SsdpScanner(log);
            _control = new ControlServer(listenPrefix, settings, scanner.ScanAsync, OnConfigChangedAsync, log);
        }

        public async Task StartAsync()
        {
            if (!_settings.Contains(EnabledKey)) _settings.Set(EnabledKey, "false");
            if (_settings.Contains(RpcServerKey)) _client.SetServer(_settings.Get(RpcServerKey));

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

        private bool IsEnabled()
        {
            return _settings.Contains(EnabledKey)
                && bool.TryParse(_settings.Get(EnabledKey), out bool enabled)
                && enabled;
        }

        private async Task OnConfigChangedAsync(string key, string value)
        {
            switch (key)
            {
                case RpcServerKey:
                    _client.SetServer(value);
                    break;

                case EnabledKey:
                    if (!bool.TryParse(value, out bool enabled))
                    {
                        _log.Error("Ignoring invalid 'enabled' value: " + value);
                        return;
                    }

                    if (enabled) await _capture.StartAsync().ConfigureAwait(false);
                    else await _capture.StopAsync().ConfigureAwait(false);
                    break;
            }
        }
    }
}
