using System.Net;
using HyperTizen.Core;
using HyperTizen.Desktop;

const string ListenPrefix = "http://127.0.0.1:8086/";

var log = new ConsoleLog();
string settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HyperTizen",
    "settings.json");

var service = new HyperTizenService(
    ListenPrefix,
    new SimulatedCapturer(),
    new JsonFileSettingsStore(settingsPath),
    log);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

try
{
    await service.StartAsync();
}
catch (HttpListenerException ex)
{
    log.Error("Cannot listen on " + ListenPrefix + " (is another instance running?)", ex);
    return 1;
}

log.Info("HyperTizen desktop host running with simulated capture.");
log.Info("Settings file: " + settingsPath);
log.Info("Press Ctrl+C to stop.");

try
{
    await Task.Delay(Timeout.Infinite, stop.Token);
}
catch (OperationCanceledException)
{
}

await service.StopAsync();
return 0;
