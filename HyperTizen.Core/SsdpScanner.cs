using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Rssdp;
using Rssdp.Infrastructure;

namespace HyperTizen.Core
{
    // Finds Hyperion and HyperHDR instances on the local network.
    public sealed class SsdpScanner
    {
        private const string HyperionType = "urn:hyperion-project.org:device:basic:1";
        private const string HyperHdrType = "urn:hyperhdr.eu:device:basic:1";
        private const int DefaultPort = 8090;

        private readonly ILog _log;

        public SsdpScanner(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<List<SSDPScanResultEvent.SSDPDevice>> ScanAsync()
        {
            // One search per network address of this device: a search that is not tied to an
            // address leaves through whichever network the system prefers, which on a device
            // with several is not always the one the server is on.
            List<string> addresses = LocalAddresses();
            IEnumerable<Task<List<DiscoveredSsdpDevice>>> searches = addresses.Count == 0
                ? new[] { SearchAsync(null) }
                : addresses.Select(SearchAsync);

            var devices = new List<SSDPScanResultEvent.SSDPDevice>();
            foreach (List<DiscoveredSsdpDevice> found in await Task.WhenAll(searches).ConfigureAwait(false))
            {
                foreach (DiscoveredSsdpDevice foundDevice in found)
                {
                    string url = ServerUrl(foundDevice.DescriptionLocation);
                    if (url == null || devices.Any(device => device.UrlBase == url)) continue;

                    devices.Add(new SSDPScanResultEvent.SSDPDevice(await NameOfAsync(foundDevice).ConfigureAwait(false), url));
                }
            }
            return devices;
        }

        // Where the server listens, from where it says its description is. HyperHDR 21 names
        // port 0 there; its real port is then taken to be the usual one.
        public static string ServerUrl(Uri descriptionLocation)
        {
            if (descriptionLocation == null || !descriptionLocation.IsAbsoluteUri) return null;
            if (descriptionLocation.Scheme != "http" && descriptionLocation.Scheme != "https") return null;

            int port = descriptionLocation.Port > 0 ? descriptionLocation.Port : DefaultPort;
            return descriptionLocation.Scheme + "://" + descriptionLocation.Host + ":" + port;
        }

        // What to call a server whose description cannot be read.
        public static string DefaultName(string notificationType)
        {
            return notificationType == HyperHdrType ? "HyperHDR" : "Hyperion";
        }

        private async Task<string> NameOfAsync(DiscoveredSsdpDevice device)
        {
            // The name is a nicety; a server whose description cannot be read is still listed.
            if (device.DescriptionLocation.Port <= 0) return DefaultName(device.NotificationType);
            try
            {
                SsdpDevice description = await device.GetDeviceInfo().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(description.FriendlyName) ? DefaultName(device.NotificationType) : description.FriendlyName;
            }
            catch (Exception ex)
            {
                _log.Info("Reading the description of " + device.DescriptionLocation + " failed: " + ex.Message);
                return DefaultName(device.NotificationType);
            }
        }

        private async Task<List<DiscoveredSsdpDevice>> SearchAsync(string localAddress)
        {
            try
            {
                using (SsdpDeviceLocator locator = localAddress == null
                    ? new SsdpDeviceLocator()
                    : new SsdpDeviceLocator(new SsdpCommunicationsServer(new SocketFactory(localAddress))))
                {
                    var found = await locator.SearchAsync().ConfigureAwait(false);
                    return found
                        .Where(device => device.NotificationType == HyperionType || device.NotificationType == HyperHdrType)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _log.Info("Searching from " + (localAddress ?? "the default address") + " failed: " + ex.Message);
                return new List<DiscoveredSsdpDevice>();
            }
        }

        private List<string> LocalAddresses()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(network => network.OperationalStatus == OperationalStatus.Up
                        && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                _log.Info("Listing the network addresses failed: " + ex.Message);
                return new List<string>();
            }
        }
    }
}
