using System;

namespace HyperTizen.Core
{
    // Passes everything on and remembers the most recent error so status can show it.
    public sealed class LastErrorLog : ILog
    {
        private readonly ILog _inner;
        private volatile string _lastError;

        public LastErrorLog(ILog inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public void Clear()
        {
            _lastError = null;
        }

        public void Info(string message)
        {
            _inner.Info(message);
        }

        public void Error(string message, Exception exception = null)
        {
            _lastError = exception == null ? message : message + ": " + exception.Message;
            _inner.Error(message, exception);
        }
    }
}
