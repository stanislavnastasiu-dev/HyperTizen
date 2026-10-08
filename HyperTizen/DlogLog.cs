using System;
using HyperTizen.Core;

namespace HyperTizen
{
    // Writes to the TV log; read it with `sdb dlog HyperTizen:V *:S`.
    public class DlogLog : ILog
    {
        private const string Tag = "HyperTizen";

        public void Info(string message)
        {
            Tizen.Log.Info(Tag, message);
        }

        public void Error(string message, Exception exception = null)
        {
            Tizen.Log.Error(Tag, exception == null ? message : message + ": " + exception);
        }
    }
}
