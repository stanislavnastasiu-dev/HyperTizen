using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Sends a short color sequence to Hyperion so the user can see that the link works.
    public sealed class LedTester
    {
        private static readonly int[][] Sequence =
        {
            new[] { 255, 0, 0 },
            new[] { 0, 255, 0 },
            new[] { 0, 0, 255 },
            new[] { 255, 255, 255 }
        };

        private readonly HyperionClient _client;
        private readonly CaptureService _capture;
        private readonly CaptureOptions _options;
        private readonly TimeSpan _step;
        private readonly TimeSpan _connectTimeout;
        private int _running;

        public LedTester(
            HyperionClient client,
            CaptureService capture,
            CaptureOptions options,
            TimeSpan? step = null,
            TimeSpan? connectTimeout = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _step = step ?? TimeSpan.FromMilliseconds(700);
            _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        }

        public async Task<TestLedsResultEvent> RunAsync()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return new TestLedsResultEvent(false, "A test is already running.");

            try
            {
                if (!_client.HasServer) return new TestLedsResultEvent(false, "No server is configured.");

                // When capture is off there is no connection yet; open one for the test only.
                bool startedHere = !_client.IsStarted;
                if (startedHere) _client.Start();
                try
                {
                    var waited = Stopwatch.StartNew();
                    while (!_client.IsConnected)
                    {
                        if (waited.Elapsed >= _connectTimeout)
                            return new TestLedsResultEvent(false, "Could not connect to the server.");
                        await Task.Delay(50).ConfigureAwait(false);
                    }

                    _capture.Paused = true;
                    byte priority = _options.Priority;
                    foreach (int[] color in Sequence)
                    {
                        if (!await _client.SendColorAsync(color[0], color[1], color[2], priority).ConfigureAwait(false))
                            return new TestLedsResultEvent(false, "The server stopped responding.");
                        await Task.Delay(_step).ConfigureAwait(false);

                        // Sent is not shown: a server that wants a login, say, answers with a refusal.
                        string rejection = _client.Rejection;
                        if (rejection != null) return new TestLedsResultEvent(false, rejection);
                    }

                    await _client.SendClearAsync(priority).ConfigureAwait(false);
                    return new TestLedsResultEvent(true, null);
                }
                finally
                {
                    _capture.Paused = false;
                    if (startedHere && !_capture.IsRunning) await _client.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
