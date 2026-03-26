using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text.Json;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace TeliLandOverlay;

public sealed class PeerSessionHostService : IAsyncDisposable
{
    public const int DefaultControlPort = 46232;
    private static readonly TimeSpan DiscoveryBroadcastInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(220);

    private readonly Lock _syncRoot = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _deviceName = Environment.MachineName;
    private TcpListener? _listener;
    private TcpClient? _connectedClient;
    private NetworkStream? _connectedStream;
    private UdpClient? _announcementClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _acceptLoopTask;
    private Task? _discoveryLoopTask;
    private Task? _receiveLoopTask;
    private Task? _shareLoopTask;
    private string? _connectedPeerName;
    private bool _isSharing;
    private bool _isConnectionApproved;
    private bool _hasPendingConnectionRequest;

    public event Action<bool, string?>? ConnectionStateChanged;
    public event Action<string>? ConnectionRequestReceived;
    public event Action<bool>? SharingStateChanged;
    public event Action<string>? StatusChanged;

    public string SessionId => _sessionId;

    public bool IsRunning => _cancellationTokenSource is not null;

    public bool IsConnected => _isConnectionApproved && _connectedClient?.Connected == true && _connectedStream is not null;

    public bool IsSharing => _isSharing;

    public bool HasPendingConnectionRequest => _hasPendingConnectionRequest;

    public int Port => DefaultControlPort;

    public IReadOnlyList<string> LocalAddresses => LanNetworkHelper.GetLocalIpv4AddressStrings();

    public async Task StartAsync()
    {
        if (_cancellationTokenSource is not null)
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, DefaultControlPort);
        _listener.Start();

        _announcementClient = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        _acceptLoopTask = AcceptLoopAsync(_cancellationTokenSource.Token);
        _discoveryLoopTask = BroadcastAvailabilityLoopAsync(_cancellationTokenSource.Token);
        await BroadcastAvailabilityAsync(_cancellationTokenSource.Token);
        StatusChanged?.Invoke("Waiting for a peer on the same WiFi...");
    }

    public async Task StartSharingAsync()
    {
        if (!IsConnected || _isSharing || _connectedStream is null || _cancellationTokenSource is null)
        {
            return;
        }

        _isSharing = true;
        SharingStateChanged?.Invoke(true);
        StatusChanged?.Invoke($"Sharing screen with {_connectedPeerName ?? "peer"}");
        await PeerConnectionProtocol.WriteJsonPacketAsync(
            _connectedStream,
            PeerPacketType.ShareStarted,
            new ScreenShareStatusPacket
            {
                HostName = _deviceName
            },
            _cancellationTokenSource.Token);

        _shareLoopTask = ShareFramesLoopAsync(_cancellationTokenSource.Token);
    }

    public async Task StopSharingAsync()
    {
        if (!_isSharing)
        {
            return;
        }

        _isSharing = false;
        SharingStateChanged?.Invoke(false);
        StatusChanged?.Invoke(IsConnected
            ? $"Connected to {_connectedPeerName ?? "peer"}"
            : "Waiting for a peer on the same WiFi...");

        if (_connectedStream is not null && _cancellationTokenSource is not null)
        {
            try
            {
                await PeerConnectionProtocol.WritePacketAsync(_connectedStream, PeerPacketType.ShareStopped, null, _cancellationTokenSource.Token);
            }
            catch
            {
                // Ignore if the peer already disconnected.
            }
        }
    }

    public async Task DisconnectPeerAsync()
    {
        if (_hasPendingConnectionRequest)
        {
            await RejectPendingRequestAsync("The connection request was declined.");
            StatusChanged?.Invoke("Connection request declined.");
            return;
        }

        if (_connectedStream is not null && _cancellationTokenSource is not null)
        {
            try
            {
                await PeerConnectionProtocol.WritePacketAsync(_connectedStream, PeerPacketType.Disconnect, null, _cancellationTokenSource.Token);
            }
            catch
            {
                // Ignore abrupt connection loss.
            }
        }

        await ResetConnectedPeerAsync();
        StatusChanged?.Invoke("Waiting for a peer on the same WiFi...");
    }

    public async Task ApprovePendingRequestAsync()
    {
        if (!_hasPendingConnectionRequest || _connectedStream is null || _cancellationTokenSource is null)
        {
            return;
        }

        await PeerConnectionProtocol.WriteJsonPacketAsync(
            _connectedStream,
            PeerPacketType.ConnectionAccepted,
            new ConnectionAcceptedPacket
            {
                DeviceName = _deviceName
            },
            _cancellationTokenSource.Token);

        _hasPendingConnectionRequest = false;
        _isConnectionApproved = true;
        ConnectionStateChanged?.Invoke(true, _connectedPeerName);
        StatusChanged?.Invoke($"Connected to {_connectedPeerName ?? "peer"}");
    }

    public async Task RejectPendingRequestAsync(string reason)
    {
        if (!_hasPendingConnectionRequest || _connectedStream is null || _cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            await PeerConnectionProtocol.WriteJsonPacketAsync(
                _connectedStream,
                PeerPacketType.ConnectionRejected,
                new ConnectionRejectedPacket
                {
                    Reason = reason
                },
                _cancellationTokenSource.Token);
        }
        catch
        {
            // Ignore network teardown races while rejecting.
        }

        await ResetConnectedPeerAsync(raiseDisconnectedEvent: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        await StopSharingAsync();
        await _cancellationTokenSource.CancelAsync();
        _listener?.Stop();
        _announcementClient?.Dispose();
        _connectedStream?.Dispose();
        _connectedClient?.Dispose();

        await AwaitIfNeededAsync(_acceptLoopTask);
        await AwaitIfNeededAsync(_discoveryLoopTask);
        await AwaitIfNeededAsync(_receiveLoopTask);
        await AwaitIfNeededAsync(_shareLoopTask);

        _acceptLoopTask = null;
        _discoveryLoopTask = null;
        _receiveLoopTask = null;
        _shareLoopTask = null;
        _listener = null;
        _announcementClient = null;
        _connectedStream = null;
        _connectedClient = null;
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? incomingClient = null;

            try
            {
                incomingClient = await _listener!.AcceptTcpClientAsync(cancellationToken);

                if (HasPeerTransport())
                {
                    incomingClient.Dispose();
                    continue;
                }

                lock (_syncRoot)
                {
                    _connectedClient = incomingClient;
                    _connectedStream = incomingClient.GetStream();
                }

                await PeerConnectionProtocol.WriteJsonPacketAsync(
                    _connectedStream,
                    PeerPacketType.Handshake,
                    new PeerHandshakePacket
                    {
                        DeviceName = _deviceName
                    },
                    cancellationToken);

                _receiveLoopTask = ReceivePeerLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                incomingClient?.Dispose();
                await ResetConnectedPeerAsync();
            }
        }
    }

    private async Task ReceivePeerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _connectedStream is not null)
            {
                var packet = await PeerConnectionProtocol.ReadPacketAsync(_connectedStream, cancellationToken);

                switch (packet.PacketType)
                {
                    case PeerPacketType.Handshake:
                        var handshake = PeerConnectionProtocol.DeserializeJson<PeerHandshakePacket>(packet.Payload);
                        _connectedPeerName = string.IsNullOrWhiteSpace(handshake?.DeviceName) ? "Peer" : handshake.DeviceName;
                        break;
                    case PeerPacketType.ConnectionRequest:
                        if (_hasPendingConnectionRequest || _isConnectionApproved)
                        {
                            break;
                        }

                        var connectionRequest = PeerConnectionProtocol.DeserializeJson<ConnectionRequestPacket>(packet.Payload);
                        _connectedPeerName = string.IsNullOrWhiteSpace(connectionRequest?.DeviceName)
                            ? _connectedPeerName ?? "Peer"
                            : connectionRequest.DeviceName;
                        _hasPendingConnectionRequest = true;
                        StatusChanged?.Invoke($"{_connectedPeerName} wants to connect. Waiting for your approval...");
                        ConnectionRequestReceived?.Invoke(_connectedPeerName);
                        break;
                    case PeerPacketType.Disconnect:
                        await ResetConnectedPeerAsync();
                        StatusChanged?.Invoke("Peer disconnected. Waiting for another peer...");
                        return;
                }
            }
        }
        catch
        {
            await ResetConnectedPeerAsync();
            StatusChanged?.Invoke("Peer disconnected. Waiting for another peer...");
        }
    }

    private async Task ShareFramesLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FrameInterval);

        while (_isSharing && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }

                if (_connectedStream is null)
                {
                    continue;
                }

                var frameBytes = CapturePrimaryScreenFrame();
                await PeerConnectionProtocol.WritePacketAsync(_connectedStream, PeerPacketType.Frame, frameBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                _isSharing = false;
                SharingStateChanged?.Invoke(false);
                await ResetConnectedPeerAsync();
                break;
            }
        }
    }

    private async Task BroadcastAvailabilityLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(DiscoveryBroadcastInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await BroadcastAvailabilityAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient broadcast failures.
            }
        }
    }

    private async Task ResetConnectedPeerAsync(bool raiseDisconnectedEvent = true)
    {
        await StopSharingAsync();
        var wasApprovedConnection = _isConnectionApproved;

        lock (_syncRoot)
        {
            _connectedStream?.Dispose();
            _connectedClient?.Dispose();
            _connectedStream = null;
            _connectedClient = null;
            _connectedPeerName = null;
            _isConnectionApproved = false;
            _hasPendingConnectionRequest = false;
        }

        if (raiseDisconnectedEvent && wasApprovedConnection)
        {
            ConnectionStateChanged?.Invoke(false, null);
        }
    }

    private async Task BroadcastAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (_announcementClient is null)
        {
            return;
        }

        var announcement = new DiscoveryAnnouncement
        {
            SessionId = _sessionId,
            DeviceName = _deviceName,
            Port = DefaultControlPort,
            IsAvailable = !HasPeerTransport()
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(announcement);
        var broadcastEndpoints = LanNetworkHelper.GetBroadcastEndpoints(LanPeerDiscoveryService.DiscoveryPort);

        foreach (var broadcastEndpoint in broadcastEndpoints)
        {
            await _announcementClient.SendAsync(payload, broadcastEndpoint, cancellationToken);
        }
    }

    private static byte[] CapturePrimaryScreenFrame()
    {
        var primaryScreen = WinFormsScreen.PrimaryScreen
            ?? throw new InvalidOperationException("A primary screen is required for live sharing.");
        var screenBounds = primaryScreen.Bounds;
        using var bitmap = new DrawingBitmap(screenBounds.Width, screenBounds.Height);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.CopyFromScreen(screenBounds.Location, DrawingPoint.Empty, new DrawingSize(screenBounds.Width, screenBounds.Height));

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
        return memoryStream.ToArray();
    }

    private static async Task AwaitIfNeededAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch
        {
            // Ignore teardown races.
        }
    }

    private sealed class DiscoveryAnnouncement
    {
        public string SessionId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public int Port { get; set; }

        public bool IsAvailable { get; set; }
    }

    private sealed class PeerHandshakePacket
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    private sealed class ConnectionRequestPacket
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    private sealed class ConnectionAcceptedPacket
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    private sealed class ConnectionRejectedPacket
    {
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class ScreenShareStatusPacket
    {
        public string HostName { get; set; } = string.Empty;
    }

    private bool HasPeerTransport()
    {
        return _connectedClient?.Connected == true && _connectedStream is not null;
    }
}
