using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace BlenderRenderQueue.Services.Business.Blender.Extensions;

public sealed class BlenderWorkerConnectionInfo
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Token { get; init; }

    public string Endpoint => $"{Host}:{Port}";

    public static BlenderWorkerConnectionInfo CreateLocal()
    {
        return new BlenderWorkerConnectionInfo
        {
            Host = IPAddress.Loopback.ToString(),
            Port = GetAvailablePort(),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
        };
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
