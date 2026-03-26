using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TeliLandOverlay;

public sealed class LanPeerDiscoveryService : IAsyncDisposable
{
    public const int DiscoveryPort = 46231;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, ScreenSharePeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncRoot = new();
    private UdpClient? _listenerClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenerTask;
    private Task? _cleanupTask;

    public event Action<IReadOnlyList<ScreenSharePeerInfo>>? SessionsUpdated;

    public void Start()
    {
        if (_cancellationTokenSource is not null)
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _listenerClient = CreateListenerClient();
        _listenerTask = ListenLoopAsync(_cancellationTokenSource.Token);
        _cleanupTask = CleanupLoopAsync(_cancellationTokenSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        await _cancellationTokenSource.CancelAsync();
        _listenerClient?.Dispose();

        if (_listenerTask is not null)
        {
            await SafeAwaitAsync(_listenerTask);
        }

        if (_cleanupTask is not null)
        {
            await SafeAwaitAsync(_cleanupTask);
        }

        _listenerClient = null;
        _listenerTask = null;
        _cleanupTask = null;
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;

        lock (_syncRoot)
        {
            _peers.Clear();
        }
    }

    public IReadOnlyList<ScreenSharePeerInfo> GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _peers.Values
                .OrderBy(peer => peer.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(peer => peer.HostAddress, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _listenerClient!.ReceiveAsync(cancellationToken);
                var announcement = JsonSerializer.Deserialize<DiscoveryAnnouncement>(result.Buffer);

                if (announcement is null || string.IsNullOrWhiteSpace(announcement.SessionId))
                {
                    continue;
                }

                if (LanNetworkHelper.IsLocalAddress(result.RemoteEndPoint.Address))
                {
                    continue;
                }

                var peer = new ScreenSharePeerInfo
                {
                    SessionId = announcement.SessionId,
                    DeviceName = string.IsNullOrWhiteSpace(announcement.DeviceName) ? "Unknown device" : announcement.DeviceName,
                    HostAddress = result.RemoteEndPoint.Address.ToString(),
                    Port = announcement.Port,
                    IsAvailable = announcement.IsAvailable,
                    LastSeenUtc = DateTime.UtcNow
                };

                var shouldNotify = false;

                lock (_syncRoot)
                {
                    if (!_peers.TryGetValue(peer.SessionId, out var existingPeer) ||
                        existingPeer.HostAddress != peer.HostAddress ||
                        existingPeer.Port != peer.Port ||
                        existingPeer.DeviceName != peer.DeviceName ||
                        existingPeer.IsAvailable != peer.IsAvailable)
                    {
                        shouldNotify = true;
                    }

                    _peers[peer.SessionId] = peer;
                }

                if (shouldNotify)
                {
                    RaiseSessionsUpdated();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Ignore malformed packets or transient network issues in the first version.
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var cutoffUtc = DateTime.UtcNow - PeerTimeout;
            var removedAny = false;

            lock (_syncRoot)
            {
                var stalePeerKeys = _peers.Values
                    .Where(peer => peer.LastSeenUtc < cutoffUtc)
                    .Select(peer => peer.SessionId)
                    .ToArray();

                foreach (var stalePeerKey in stalePeerKeys)
                {
                    removedAny = _peers.Remove(stalePeerKey) || removedAny;
                }
            }

            if (removedAny)
            {
                RaiseSessionsUpdated();
            }
        }
    }

    private void RaiseSessionsUpdated()
    {
        SessionsUpdated?.Invoke(GetSnapshot());
    }

    private static UdpClient CreateListenerClient()
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
            ExclusiveAddressUse = false
        };

        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        return udpClient;
    }

    private static async Task SafeAwaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Ignore teardown races for now.
        }
    }

    private sealed class DiscoveryAnnouncement
    {
        public string SessionId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public int Port { get; set; }

        public bool IsAvailable { get; set; }
    }
}
