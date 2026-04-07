using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Linq;

namespace BlenderRenderQueue.Helpers;

public static class NetworkHelper
{
    public static string GetLocalNetworkIpAddress()
    {
        try
        {
            // Method 1: Obtain it through the network interface, and prefer the LAN address
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var networkInterface in networkInterfaces)
            {
                // Only network interfaces that are enabled and not loopback are processed
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                var ipProperties = networkInterface.GetIPProperties();

                foreach (var ipAddress in ipProperties.UnicastAddresses)
                {
                    // Find IPv4 addresses that are not loopback addresses
                    if (ipAddress.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(ipAddress.Address)) continue;
                    var ipString = ipAddress.Address.ToString();

                    // Prefer LAN addresses
                    if (IsLocalNetworkAddress(ipString))
                    {
                        return ipString;
                    }
                }
            }

            // 方法2: 如果没有找到局域网地址，尝试通过主机名解析
            var hostName = Dns.GetHostName();
            var hostEntry = Dns.GetHostEntry(hostName);

            foreach (var ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(ip)) continue;
                var ipString = ip.ToString();
                if (IsLocalNetworkAddress(ipString))
                {
                    return ipString;
                }
            }

            return "localhost";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NetworkHelper] ❌ Error getting local network IP: {ex.Message}");
            return "localhost";
        }
    }


    private static bool IsLocalNetworkAddress(string ipAddress)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress, out var ip))
                return false;

            var bytes = ip.GetAddressBytes();

            switch (bytes[0])
            {
                // (RFC 1918)
                // 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
                case 10:
                // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
                case 172 when bytes[1] >= 16 && bytes[1] <= 31:
                    return true;
            }

            // 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            //  (169.254.0.0/16)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            switch (bytes[0])
            {
                // (224.0.0.0/4)
                case >= 224 and <= 239:
                //  (255.255.255.255)
                case 255 when bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255:
                    return false;
                default:
                    // 其他地址都认为是公网地址
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}