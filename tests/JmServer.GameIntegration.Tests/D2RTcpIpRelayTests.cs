using System.Net;
using System.Net.Sockets;
using System.Text;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2RTcpIpRelayTests
{
    [Fact]
    public async Task Relay_ForwardsBothDirections()
    {
        var destinationListener = new TcpListener(IPAddress.Loopback, 0);
        destinationListener.Start();
        var destinationEndpoint = (IPEndPoint)destinationListener.LocalEndpoint;
        await using var relay = D2RTcpIpRelay.StartForTest(
            IPAddress.Loopback,
            0,
            destinationEndpoint.Address.ToString(),
            destinationEndpoint.Port);

        var destinationTask = Task.Run(async () =>
        {
            using var destination = await destinationListener.AcceptTcpClientAsync();
            await using var stream = destination.GetStream();
            var request = new byte[4];
            await stream.ReadExactlyAsync(request);
            Assert.Equal("ping", Encoding.ASCII.GetString(request));
            await stream.WriteAsync("pong"u8.ToArray());
        });

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(relay.LocalEndpoint.Address, relay.LocalEndpoint.Port);
        await using var clientStream = client.GetStream();
        await clientStream.WriteAsync("ping"u8.ToArray());
        var response = new byte[4];
        await clientStream.ReadExactlyAsync(response);

        Assert.Equal("pong", Encoding.ASCII.GetString(response));
        await destinationTask;
        destinationListener.Stop();
    }

    [Fact]
    public void Ports_UseConsecutivePublicRangeAndKeepD2RInternalPortPrivate()
    {
        Assert.Equal(15571, D2RTcpIpRelay.PublicGamePort);
        Assert.Equal(4000, D2RTcpIpRelay.LocalGamePort);
    }
}
