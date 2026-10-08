using HyperTizen.Core;

namespace HyperTizen.Desktop;

public sealed class ConsoleLog : ILog
{
    public void Info(string message)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }

    public void Error(string message, Exception? exception = null)
    {
        Console.Error.WriteLine(exception == null
            ? $"{DateTime.Now:HH:mm:ss} ERROR {message}"
            : $"{DateTime.Now:HH:mm:ss} ERROR {message}: {exception.Message}");
    }
}
