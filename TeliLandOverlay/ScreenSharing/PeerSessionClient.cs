using System.Net.Sockets;

namespace TeliLandOverlay;

public sealed class PeerSessionClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveLoopTask;
    private string? _connectedPeerName;
    private bool _isApprovedConnection;

    public event Action<string>? Connected;
    public event Action<string>? ConnectionRejected;
    public event Action<string>? SharingStarted;
    public event Action? SharingStopped;
    public event Action<byte[]>? FrameReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected => _isApprovedConnection && _tcpClient?.Connected == true && _networkStream is not null;

    public async Task ConnectAsync(ScreenSharePeerInfo peerInfo)
    {
        await DisposeAsync();

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(peerInfo.HostAddress, peerInfo.Port);
        _networkStream = _tcpClient.GetStream();
        _cancellationTokenSource = new CancellationTokenSource();
        _isApprovedConnection = false;

        await PeerConnectionProtocol.WriteJsonPacketAsync(
            _networkStream,
            PeerPacketType.Handshake,
            new PeerHandshakePacket
            {
                DeviceName = Environment.MachineName
            },
            _cancellationTokenSource.Token);

        await PeerConnectionProtocol.WriteJsonPacketAsync(
            _networkStream,
            PeerPacketType.ConnectionRequest,
            new ConnectionRequestPacket
            {
                DeviceName = Environment.MachineName
            },
            _cancellationTokenSource.Token);

        _connectedPeerName = peerInfo.DeviceName;
        _receiveLoopTask = ReceiveLoopAsync(_cancellationTokenSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _isApprovedConnection = false;

        if (_cancellationTokenSource is not null)
        {
            await _cancellationTokenSource.CancelAsync();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        if (_networkStream is not null)
        {
            try
            {
                if (_tcpClient?.Connected == true)
                {
                    await PeerConnectionProtocol.WritePacketAsync(_networkStream, PeerPacketType.Disconnect, null, CancellationToken.None);
                }
            }
            catch
            {
                // Ignore shutdown races.
            }
        }

        _networkStream?.Dispose();
        _tcpClient?.Dispose();
        _networkStream = null;
        _tcpClient = null;

        if (_receiveLoopTask is not null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch
            {
                // Ignore teardown races.
            }

            _receiveLoopTask = null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _networkStream is not null)
            {
                var packet = await PeerConnectionProtocol.ReadPacketAsync(_networkStream, cancellationToken);

                switch (packet.PacketType)
                {
                    case PeerPacketType.Handshake:
                        var handshake = PeerConnectionProtocol.DeserializeJson<PeerHandshakePacket>(packet.Payload);
                        _connectedPeerName = string.IsNullOrWhiteSpace(handshake?.DeviceName) ? _connectedPeerName ?? "Peer" : handshake.DeviceName;
                        break;
                    case PeerPacketType.ConnectionAccepted:
                        _isApprovedConnection = true;
                        Connected?.Invoke(_connectedPeerName ?? "Peer");
                        break;
                    case PeerPacketType.ConnectionRejected:
                        var rejection = PeerConnectionProtocol.DeserializeJson<ConnectionRejectedPacket>(packet.Payload);
                        ConnectionRejected?.Invoke(rejection?.Reason ?? $"{_connectedPeerName ?? "Peer"} declined the request.");
                        return;
                    case PeerPacketType.ShareStarted:
                        SharingStarted?.Invoke(_connectedPeerName ?? "Peer");
                        break;
                    case PeerPacketType.ShareStopped:
                        SharingStopped?.Invoke();
                        break;
                    case PeerPacketType.Frame:
                        FrameReceived?.Invoke(packet.Payload);
                        break;
                    case PeerPacketType.Disconnect:
                        Disconnected?.Invoke(_connectedPeerName ?? "Peer");
                        return;
                }
            }
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Disconnected?.Invoke(_connectedPeerName ?? "Peer");
            }
        }
        finally
        {
            _isApprovedConnection = false;
        }
    }

    private sealed class PeerHandshakePacket
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    private sealed class ConnectionRequestPacket
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    private sealed class ConnectionRejectedPacket
    {
        public string Reason { get; set; } = string.Empty;
    }
}
