using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json;
using SimpleWifi.Win32;
using System.Diagnostics;

namespace Internet_Monitor
{
    public class EventEventArgs : EventArgs
    {
        public EventEventArgs(string json)
        {
            Json = json;
        }

        public string Json { get; set; }
    }

    public class Monitor
    {
        public event EventHandler<EventEventArgs> RaiseEvent;

        public Monitor()
        {
        }

        public void Execute()
        {
            LoadNetworkData();

            var ips = new HashSet<IPAddress>();
            ips.Add(IPAddress.Parse("1.0.0.1")); // Cloudflare DNS
            ips.Add(IPAddress.Parse("1.1.1.1")); // Cloudflare DNS
            ips.Add(IPAddress.Parse("208.67.220.220")); // OpenDNS
            ips.Add(IPAddress.Parse("208.67.222.222")); // OpenDNS
            ips.Add(IPAddress.Parse("64.6.64.6")); // Verisign DNS
            ips.Add(IPAddress.Parse("64.6.65.6")); // Verisign DNS
            ips.Add(IPAddress.Parse("8.20.247.20")); // Comodo Secure DNS
            ips.Add(IPAddress.Parse("8.26.56.26")); // Comodo Secure DNS
            ips.Add(IPAddress.Parse("8.8.4.4")); // Google DNS
            ips.Add(IPAddress.Parse("8.8.8.8")); // Google DNS
            ips.Add(IPAddress.Parse("84.200.69.80")); // DNS.Watch
            ips.Add(IPAddress.Parse("84.200.70.40")); // DNS.Watch
            ips.Add(IPAddress.Parse("9.9.9.9")); // Quad9 DNS
            foreach (var inter in Networks)
            {
                var ipInfo = inter.GetIPProperties();
                foreach (var ip in ipInfo.DhcpServerAddresses)
                    ips.Add(ip);
                foreach (var ip in ipInfo.DnsAddresses)
                    ips.Add(ip);
                foreach (var ip in ipInfo.GatewayAddresses)
                    ips.Add(ip.Address);
            }
            foreach (var ip in ips)
            {
                var network = GetNetwork(ip);
                var data = new Dictionary<string, object> {
                        { "meta.local_hostname", Dns.GetHostName() },
                        { "meta.local_network", network?.Name },
                        { "meta.local_connection", GetNetworkName(network) },
                        { "meta.local_channel", GetNetworkChannel(network) },
                        { "meta.local_strength", GetNetworkStrength(network) },
                        { "meta.local_bssid", GetNetworkBSSID(network)?.ToString() },
                        { "service_name", "ping" },
                        { "route", ip.ToString() },
                    };
                var ping = new Ping();
                try
                {
                    var reply = ping.Send(ip, 5000);
                    data["status"] = reply.Status.ToString();
                    data["duration_ms"] = reply.RoundtripTime;
                }
                catch (PingException error)
                {
                    data["error"] = error.GetBaseException().Message;
                }
                RaiseEvent(this, new EventEventArgs(JsonSerializer.Serialize(data)));
            }
        }

        NetworkInterface[] Networks;
        Dictionary<IPAddress, NetworkInterface> NetworkForIp;
        Dictionary<string, WifiNetworkInterface> WifiNetworkForNetwork;

        void LoadNetworkData()
        {
            Networks = NetworkInterface.GetAllNetworkInterfaces();
            NetworkForIp = new Dictionary<IPAddress, NetworkInterface>(
                Networks
                    .SelectMany(network => network
                        .GetIPProperties()
                        .UnicastAddresses
                        .Select(address => new KeyValuePair<IPAddress, NetworkInterface>(
                            address.Address,
                            network
                        ))
                    )
            );
            WifiNetworkForNetwork = new Dictionary<string, WifiNetworkInterface>(
                new WlanClient().Interfaces
                    .Select(wifiNetwork => new KeyValuePair<string, WifiNetworkInterface>(
                        wifiNetwork.NetworkInterface.Id,
                        new WifiNetworkInterface(wifiNetwork)
                    ))
            );
        }

        NetworkInterface GetNetwork(IPAddress ip)
        {
            var networkIp = GetNetworkIpForIp(ip);
            var network = networkIp != null ? NetworkForIp[networkIp] : null;
            Debug.WriteLine($"GetNetwork({ip}) = {network?.Name}");
            return network;
        }

        WifiNetworkInterface GetWifiNetwork(NetworkInterface network)
        {
            if (network == null || !WifiNetworkForNetwork.ContainsKey(network.Id)) return null;
            return WifiNetworkForNetwork[network.Id];
        }

        string GetNetworkName(NetworkInterface network)
        {
            return GetWifiNetwork(network)?.SSID ?? network?.Name;
        }

        int? GetNetworkChannel(NetworkInterface network)
        {
            return GetWifiNetwork(network)?.Channel;
        }

        uint? GetNetworkStrength(NetworkInterface network)
        {
            return GetWifiNetwork(network)?.SignalStrength;
        }

        PhysicalAddress GetNetworkBSSID(NetworkInterface network)
        {
            return GetWifiNetwork(network)?.BSSID;
        }

        class WifiNetworkInterface
        {
            public readonly string SSID;
            public readonly PhysicalAddress BSSID;
            public readonly uint SignalStrength;
            public readonly int Channel;

            public WifiNetworkInterface(WlanInterface wlan)
            {
                var connection = wlan.CurrentConnection;

                SSID = connection.profileName;
                BSSID = connection.wlanAssociationAttributes.Dot11Bssid;
                SignalStrength = connection.wlanAssociationAttributes.wlanSignalQuality;
                Channel = wlan.Channel;
            }
        }

        IPAddress GetNetworkIpForIp(IPAddress ip)
        {
            try
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

                var endPoint = new IPEndPoint(ip, 0);
                var sockAddr = endPoint.Serialize();
                var inBytes = new byte[sockAddr.Size];
                var outBytes = new byte[sockAddr.Size];

                for (var i = 0; i < sockAddr.Size; i++)
                    inBytes[i] = sockAddr[i];

                socket.IOControl(IOControlCode.RoutingInterfaceQuery, inBytes, outBytes);

                for (var i = 0; i < sockAddr.Size; i++)
                    sockAddr[i] = outBytes[i];

                var networkIp = endPoint.Create(sockAddr) as IPEndPoint;
                Debug.WriteLine($"GetNetworkIpForIp({ip}) = {networkIp.Address}");
                return networkIp.Address;
            }
            catch (SocketException error)
            {
                Debug.WriteLine($"GetNetworkIpForIp({ip}) {error.Message}");
                return null;
            }
        }
    }
}