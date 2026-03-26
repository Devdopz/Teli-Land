using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TeliLandOverlay;

public static class LanNetworkHelper
{
    public static IReadOnlyList<string> GetLocalIpv4AddressStrings()
    {
        return GetLocalIpv4Addresses()
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return GetLocalIpv4Addresses().Any(localAddress => localAddress.Equals(address));
    }

    public static IReadOnlyList<IPEndPoint> GetBroadcastEndpoints(int port)
    {
        var endpoints = new List<IPEndPoint>();
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(unicastAddress.Address) ||
                    unicastAddress.IPv4Mask is null)
                {
                    continue;
                }

                var broadcastAddress = GetBroadcastAddress(unicastAddress.Address, unicastAddress.IPv4Mask);
                var broadcastKey = broadcastAddress.ToString();

                if (seenAddresses.Add(broadcastKey))
                {
                    endpoints.Add(new IPEndPoint(broadcastAddress, port));
                }
            }
        }

        if (seenAddresses.Add(IPAddress.Broadcast.ToString()))
        {
            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, port));
        }

        return endpoints;
    }

    private static IReadOnlyList<IPAddress> GetLocalIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                !networkInterface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(unicastAddress => unicastAddress.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Distinct()
            .ToArray();
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        var broadcastBytes = new byte[addressBytes.Length];

        for (var index = 0; index < addressBytes.Length; index++)
        {
            broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);
        }

        return new IPAddress(broadcastBytes);
    }
}
