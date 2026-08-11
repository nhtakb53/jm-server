using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace JmServer.GameIntegration;

public sealed class D2RTcpIpRelay : IAsyncDisposable
{
    public const int LocalGamePort = 4000;
    public const int PublicGamePort = 15571;

    private readonly TcpListener _listener;
    private readonly string _destinationHost;
    private readonly int _destinationPort;
    private readonly Action<string>? _warning;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly Task _acceptLoop;
    private long _connectionSequence;
    private int _disposed;

    private D2RTcpIpRelay(
        IPAddress listenAddress,
        int listenPort,
        string destinationHost,
        int destinationPort,
        Action<string>? warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationHost);
        ArgumentOutOfRangeException.ThrowIfLessThan(listenPort, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(listenPort, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(destinationPort, ushort.MaxValue);

        _destinationHost = destinationHost;
        _destinationPort = destinationPort;
        _warning = warning;
        _listener = new TcpListener(listenAddress, listenPort);
        _listener.Start();
        LocalEndpoint = (IPEndPoint)_listener.LocalEndpoint;
        _acceptLoop = AcceptLoopAsync();
    }

    public IPEndPoint LocalEndpoint { get; }

    public static D2RTcpIpRelay StartHost(
        int publicPort = PublicGamePort,
        Action<string>? warning = null) =>
        new(
            IPAddress.Any,
            publicPort,
            IPAddress.Loopback.ToString(),
            LocalGamePort,
            warning);

    public static D2RTcpIpRelay StartGuest(
        string hostAddress,
        int hostPort,
        Action<string>? warning = null) =>
        new(
            IPAddress.Loopback,
            LocalGamePort,
            hostAddress,
            hostPort,
            warning);

    internal static D2RTcpIpRelay StartForTest(
        IPAddress listenAddress,
        int listenPort,
        string destinationHost,
        int destinationPort,
        Action<string>? warning = null) =>
        new(listenAddress, listenPort, destinationHost, destinationPort, warning);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }

        var activeConnections = _connections.Values.ToArray();
        if (activeConnections.Length != 0)
        {
            try
            {
                await Task.WhenAll(activeConnections).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _warning?.Invoke("TCP/IP 게임 중계 연결 종료가 지연되어 강제로 정리했습니다.");
            }
        }

        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient source;
            try
            {
                source = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (_stopping.IsCancellationRequested)
            {
                return;
            }

            var connectionId = Interlocked.Increment(ref _connectionSequence);
            var connection = ForwardAsync(source, _stopping.Token);
            _connections[connectionId] = connection;
            _ = connection.ContinueWith(
                completedTask => _connections.TryRemove(connectionId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ForwardAsync(TcpClient source, CancellationToken cancellationToken)
    {
        using (source)
        using (var destination = new TcpClient(AddressFamily.InterNetwork))
        {
            try
            {
                source.NoDelay = true;
                destination.NoDelay = true;
                await destination.ConnectAsync(
                    _destinationHost,
                    _destinationPort,
                    cancellationToken);

                await using var sourceStream = source.GetStream();
                await using var destinationStream = destination.GetStream();
                var outbound = CopyUntilClosedAsync(
                    sourceStream,
                    destinationStream,
                    cancellationToken);
                var inbound = CopyUntilClosedAsync(
                    destinationStream,
                    sourceStream,
                    cancellationToken);
                await Task.WhenAny(outbound, inbound);
                source.Close();
                destination.Close();
                try
                {
                    await Task.WhenAll(outbound, inbound);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                _warning?.Invoke(
                    $"TCP/IP 게임 중계 연결 실패: {_destinationHost}:{_destinationPort} · " +
                    exception.Message);
            }
        }
    }

    private static async Task CopyUntilClosedAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
