using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rssdp;

namespace HyperTizen.Core
{
    // Finds Hyperion and HyperHDR instances on the local network.
    public sealed class SsdpScanner
    {
        private static readonly string[] NotificationTypes =
        {
            "urn:hyperion-project.org:device:basic:1",
            "urn:hyperhdr.eu:device:basic:1"
        };

        private readonly ILog _log;

        public SsdpScanner(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<List<SSDPScanResultEvent.SSDPDevice>> ScanAsync()
        {
            var devices = new List<SSDPScanResultEvent.SSDPDevice>();
            using (var deviceLocator = new SsdpDeviceLocator())
            {
                var foundDevices = await deviceLocator.SearchAsync().ConfigureAwait(false);
                foreach (var foundDevice in foundDevices)
                {
                    if (!NotificationTypes.Contains(foundDevice.NotificationType)) continue;

                    try
                    {
                        var fullDevice = await foundDevice.GetDeviceInfo().ConfigureAwait(false);
                        Uri descLocation = foundDevice.DescriptionLocation;
                        devices.Add(new SSDPScanResultEvent.SSDPDevice(
                            fullDevice.FriendlyName,
                            descLocation.OriginalString.Replace(descLocation.PathAndQuery, "")));
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Reading the description of " + foundDevice.DescriptionLocation + " failed", ex);
                    }
                }
            }
            return devices;
        }
    }
}
