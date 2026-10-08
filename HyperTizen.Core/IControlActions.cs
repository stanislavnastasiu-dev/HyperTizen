using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Everything the control server asks of the service on behalf of the UI.
    public interface IControlActions
    {
        Task<List<SSDPScanResultEvent.SSDPDevice>> ScanAsync();

        // False rejects a SetConfig before anything is stored.
        bool IsValidConfig(string key, string value);

        // Called after the value has been stored.
        Task OnConfigChangedAsync(string key, string value);

        // Called after the value has been removed.
        Task OnConfigDeletedAsync(string key);

        StatusResultEvent GetStatus();
        Task<TestLedsResultEvent> TestLedsAsync();
        Task<PreviewResultEvent> GetPreviewAsync();
    }
}
