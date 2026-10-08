using System;
using System.Threading.Tasks;
using HyperTizen.Core;
using Tizen.Applications;
using Tizen.System;

namespace HyperTizen
{
    class App : ServiceApplication
    {
        private ILog _log;
        private HyperTizenService _service;

        protected override void OnCreate()
        {
            base.OnCreate();
            _log = new DlogLog();
            _service = new HyperTizenService("http://+:8086/", new VideoEnhanceCapturer(_log), new PreferenceSettingsStore(), _log);
            RunInBackground(_service.StartAsync, "start");
            Display.StateChanged += Display_StateChanged;
        }

        private void Display_StateChanged(object sender, DisplayStateChangedEventArgs e)
        {
            if (e.State == DisplayState.Off)
            {
                RunInBackground(_service.OnDisplayOffAsync, "display off");
            } else if (e.State == DisplayState.Normal)
            {
                RunInBackground(_service.OnDisplayOnAsync, "display on");
            }
        }

        protected override void OnTerminate()
        {
            Display.StateChanged -= Display_StateChanged;
            try
            {
                _service.StopAsync().Wait(TimeSpan.FromSeconds(5));
            } catch (Exception ex)
            {
                _log.Error("Stopping the service failed", ex);
            }
            base.OnTerminate();
        }

        private void RunInBackground(Func<Task> action, string what)
        {
            Task.Run(async () =>
            {
                try
                {
                    await action();
                } catch (Exception ex)
                {
                    _log.Error("HyperTizen failed during " + what, ex);
                }
            });
        }

        static void Main(string[] args)
        {
            App app = new App();
            app.Run(args);
        }
    }
}
