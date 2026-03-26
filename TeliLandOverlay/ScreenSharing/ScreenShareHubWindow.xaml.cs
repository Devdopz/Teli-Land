using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TeliLandOverlay;

public partial class ScreenShareHubWindow : Window
{
    private readonly ObservableCollection<ScreenSharePeerInfo> _availablePeers = [];
    private readonly LanPeerDiscoveryService _discoveryService = new();
    private PeerSessionHostService? _hostService;
    private PeerSessionClient? _clientService;
    private ScreenViewerWindow? _viewerWindow;
    private bool _isClosingHubWindow;
    private bool _isHostAvailable;
    private bool _isHostConnected;
    private bool _isHostSharing;
    private bool _isViewerConnectionPending;
    private bool _isViewerConnected;
    private string? _connectedPeerName;

    public ScreenShareHubWindow()
    {
        InitializeComponent();
        PeerListBox.ItemsSource = _availablePeers;
        LocalDeviceNameText.Text = Environment.MachineName;
        LocalDeviceAddressText.Text = GetLocalAddressSummary();
        ManualAddressTextBox.Text = string.Empty;
        Loaded += ScreenShareHubWindow_OnLoaded;
        Closed += ScreenShareHubWindow_OnClosed;
        UpdateUiState();
    }

    private async void ScreenShareHubWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureHostServiceAsync();
        _discoveryService.SessionsUpdated += DiscoveryService_OnSessionsUpdated;
        _discoveryService.Start();
        ApplyPeerSnapshot(_discoveryService.GetSnapshot());
    }

    private async void ScreenShareHubWindow_OnClosed(object? sender, EventArgs e)
    {
        _isClosingHubWindow = true;
        _discoveryService.SessionsUpdated -= DiscoveryService_OnSessionsUpdated;
        await _discoveryService.DisposeAsync();
        await ShutdownClientAsync(restoreHostWhenIdle: false);
        await ShutdownHostAsync();

        if (_viewerWindow is not null)
        {
            _viewerWindow.Close();
            _viewerWindow = null;
        }
    }

    private async void AvailabilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isHostAvailable)
        {
            await ShutdownHostAsync();
            UpdateStatus("Host availability stopped.", "READY");
            return;
        }

        await ShutdownClientAsync(restoreHostWhenIdle: false);
        await EnsureHostServiceAsync();
        UpdateUiState();
    }

    private async void StartSharingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_hostService is null)
        {
            return;
        }

        await _hostService.StartSharingAsync();
    }

    private async void StopSharingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_hostService is null)
        {
            return;
        }

        await _hostService.StopSharingAsync();
        UpdateUiState();
    }

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isViewerConnectionPending || _isViewerConnected)
        {
            await ShutdownClientAsync();
            UpdateStatus(_isViewerConnectionPending ? "Connection request cancelled." : "Disconnected from the peer.", "READY");
            return;
        }

        if (_hostService is not null && _isHostConnected)
        {
            await _hostService.DisconnectPeerAsync();
            UpdateStatus("Peer disconnected.", _isHostAvailable ? "HOST READY" : "READY");
            return;
        }

        if (_isHostAvailable)
        {
            await ShutdownHostAsync();
        }
    }

    private void RefreshPeersButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyPeerSnapshot(_discoveryService.GetSnapshot());
    }

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PeerListBox.SelectedItem is not ScreenSharePeerInfo selectedPeer)
        {
            return;
        }

        await ConnectToPeerAsync(selectedPeer);
    }

    private void PeerListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUiState();
    }

    private async Task EnsureHostServiceAsync()
    {
        if (_hostService is not null)
        {
            return;
        }

        _hostService = new PeerSessionHostService();
        _hostService.ConnectionRequestReceived += HostService_OnConnectionRequestReceived;
        _hostService.ConnectionStateChanged += HostService_OnConnectionStateChanged;
        _hostService.SharingStateChanged += HostService_OnSharingStateChanged;
        _hostService.StatusChanged += HostService_OnStatusChanged;
        await _hostService.StartAsync();
        _isHostAvailable = true;
        _connectedPeerName = null;
        LocalDeviceAddressText.Text = GetLocalAddressSummary(_hostService.LocalAddresses);
        UpdateStatus("This PC is visible on the same WiFi and ready for incoming requests.", "READY");
    }

    private async Task ShutdownHostAsync()
    {
        if (_hostService is null)
        {
            _isHostAvailable = false;
            _isHostConnected = false;
            _isHostSharing = false;
            _connectedPeerName = null;
            UpdateUiState();
            return;
        }

        _hostService.ConnectionRequestReceived -= HostService_OnConnectionRequestReceived;
        _hostService.ConnectionStateChanged -= HostService_OnConnectionStateChanged;
        _hostService.SharingStateChanged -= HostService_OnSharingStateChanged;
        _hostService.StatusChanged -= HostService_OnStatusChanged;
        await _hostService.DisposeAsync();
        _hostService = null;
        _isHostAvailable = false;
        _isHostConnected = false;
        _isHostSharing = false;
        _connectedPeerName = null;
        LocalDeviceAddressText.Text = GetLocalAddressSummary();
        UpdateUiState();
    }

    private async Task ShutdownClientAsync(bool restoreHostWhenIdle = true)
    {
        if (_clientService is null)
        {
            _isViewerConnected = false;
            _isViewerConnectionPending = false;
            if (restoreHostWhenIdle && !_isClosingHubWindow && !_isHostAvailable)
            {
                await EnsureHostServiceAsync();
            }

            UpdateUiState();
            return;
        }

        _clientService.Connected -= ClientService_OnConnected;
        _clientService.ConnectionRejected -= ClientService_OnConnectionRejected;
        _clientService.SharingStarted -= ClientService_OnSharingStarted;
        _clientService.SharingStopped -= ClientService_OnSharingStopped;
        _clientService.FrameReceived -= ClientService_OnFrameReceived;
        _clientService.Disconnected -= ClientService_OnDisconnected;
        await _clientService.DisposeAsync();
        _clientService = null;
        _isViewerConnected = false;
        _isViewerConnectionPending = false;
        _connectedPeerName = null;

        if (_viewerWindow is not null)
        {
            _viewerWindow.SetStatus("Disconnected from the peer.");
            _viewerWindow.SetConnectedBadge("IDLE", "Not connected");
            _viewerWindow.ResetToWaiting();
        }

        if (restoreHostWhenIdle && !_isClosingHubWindow && !_isHostAvailable)
        {
            await EnsureHostServiceAsync();
        }

        UpdateUiState();
    }

    private void DiscoveryService_OnSessionsUpdated(IReadOnlyList<ScreenSharePeerInfo> sessions)
    {
        Dispatcher.Invoke(() => ApplyPeerSnapshot(sessions));
    }

    private void HostService_OnConnectionStateChanged(bool isConnected, string? peerName)
    {
        Dispatcher.Invoke(() =>
        {
            _isHostConnected = isConnected;
            _connectedPeerName = peerName;

            if (isConnected)
            {
                UpdateStatus($"Connected to {peerName ?? "peer"}. Starting live screen sharing...", "CONNECTED");
            }
            else
            {
                _isHostSharing = false;
                UpdateStatus(_isHostAvailable
                    ? "This PC is visible on the same WiFi and waiting for the next request."
                    : "Incoming requests are paused.", _isHostAvailable ? "READY" : "PAUSED");
            }

            UpdateUiState();
        });
    }

    private void HostService_OnSharingStateChanged(bool isSharing)
    {
        Dispatcher.Invoke(() =>
        {
            _isHostSharing = isSharing;
            UpdateStatus(isSharing
                ? $"Sharing live with {_connectedPeerName ?? "peer"}"
                : _isHostConnected
                    ? $"Connected to {_connectedPeerName ?? "peer"}. Sharing stopped."
                    : "Waiting for a connection request...", isSharing ? "SHARING" : _isHostConnected ? "CONNECTED" : "READY");
            UpdateUiState();
        });
    }

    private void HostService_OnStatusChanged(string status)
    {
        Dispatcher.Invoke(() =>
        {
            HostStatusText.Text = status;
            FooterStatusText.Text = status;
        });
    }

    private void ClientService_OnConnected(string peerName)
    {
        Dispatcher.Invoke(() =>
        {
            _isViewerConnectionPending = false;
            _isViewerConnected = true;
            _connectedPeerName = peerName;
            EnsureViewerWindow();
            _viewerWindow!.SetPeerName(peerName);
            _viewerWindow.SetStatus("Request accepted. Waiting for the live stream...");
            _viewerWindow.SetConnectedBadge("CONNECTED", "Peer connection is active");
            UpdateStatus($"Connected to {peerName}. Waiting for live sharing...", "CONNECTED");
            UpdateUiState();
        });
    }

    private void ClientService_OnConnectionRejected(string reason)
    {
        Dispatcher.Invoke(async () =>
        {
            UpdateStatus(reason, "DECLINED");
            await ShutdownClientAsync();
        });
    }

    private void ClientService_OnSharingStarted(string peerName)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureViewerWindow();
            _viewerWindow!.SetPeerName(peerName);
            _viewerWindow.SetStatus("Live sharing in progress");
            _viewerWindow.SetConnectedBadge("LIVE", "The peer is sharing the screen now");
            _viewerWindow.Show();
            _viewerWindow.Activate();
            UpdateStatus($"{peerName} started sharing the screen.", "LIVE");
        });
    }

    private void ClientService_OnSharingStopped()
    {
        Dispatcher.Invoke(() =>
        {
            if (_viewerWindow is not null)
            {
                _viewerWindow.SetStatus("The host stopped sharing.");
                _viewerWindow.SetConnectedBadge("CONNECTED", "Peer is still connected");
                _viewerWindow.ResetToWaiting();
            }

            UpdateStatus($"{_connectedPeerName ?? "Peer"} stopped sharing.", "CONNECTED");
        });
    }

    private void ClientService_OnFrameReceived(byte[] frameBytes)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureViewerWindow();
            _viewerWindow!.ShowFrame(frameBytes);
        });
    }

    private void ClientService_OnDisconnected(string peerName)
    {
        Dispatcher.Invoke(async () =>
        {
            _isViewerConnectionPending = false;

            if (_viewerWindow is not null)
            {
                _viewerWindow.SetStatus("Peer disconnected.");
                _viewerWindow.SetConnectedBadge("DISCONNECTED", "Connection closed");
                _viewerWindow.ResetToWaiting();
            }

            UpdateStatus($"{peerName} disconnected.", "READY");
            await ShutdownClientAsync();
        });
    }

    private void ApplyPeerSnapshot(IReadOnlyList<ScreenSharePeerInfo> sessions)
    {
        var ownSessionId = _hostService?.SessionId;
        var filteredPeers = sessions
            .Where(peer => peer.IsAvailable && !string.Equals(peer.SessionId, ownSessionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(peer => peer.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _availablePeers.Clear();

        foreach (var peer in filteredPeers)
        {
            _availablePeers.Add(peer);
        }

        DiscoveryStatusText.Text = filteredPeers.Length > 0
            ? $"Found {filteredPeers.Length} available peer{(filteredPeers.Length == 1 ? string.Empty : "s")} on the same WiFi."
            : "No other TeliLand devices were found on this WiFi yet.";

        EmptyPeersState.Visibility = filteredPeers.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateUiState();
    }

    private void EnsureViewerWindow()
    {
        if (_viewerWindow is not null)
        {
            return;
        }

        _viewerWindow = new ScreenViewerWindow();
        _viewerWindow.Closed += (_, _) => _viewerWindow = null;
    }

    private async void ManualConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await TryManualConnectAsync();
    }

    private async void ManualAddressTextBox_OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await TryManualConnectAsync();
    }

    private void UpdateStatus(string status, string badgeText)
    {
        HostStatusText.Text = _isHostAvailable
            ? status
            : "Incoming requests are paused right now.";
        FooterStatusText.Text = status;
        TopStatusBadgeText.Text = badgeText;
    }

    private void UpdateUiState()
    {
        AvailabilityButton.Content = _isHostAvailable ? "Pause Incoming Requests" : "Resume Incoming Requests";
        StartSharingButton.IsEnabled = _isHostAvailable && _isHostConnected && !_isHostSharing;
        StopSharingButton.IsEnabled = _isHostSharing;
        DisconnectButton.IsEnabled = _isHostConnected || _isViewerConnectionPending || _isViewerConnected || _isHostAvailable;
        ConnectButton.IsEnabled = PeerListBox.SelectedItem is ScreenSharePeerInfo && !_isViewerConnectionPending && !_isViewerConnected;
        PeerListBox.IsEnabled = !_isViewerConnectionPending && !_isViewerConnected;
        RefreshPeersButton.IsEnabled = !_isViewerConnectionPending && !_isViewerConnected;
        ManualAddressTextBox.IsEnabled = !_isViewerConnectionPending && !_isViewerConnected;
        ManualConnectButton.IsEnabled = !_isViewerConnectionPending && !_isViewerConnected;

        if (_isViewerConnectionPending)
        {
            DisconnectButton.Content = "Cancel Request";
        }
        else if (_isViewerConnected)
        {
            DisconnectButton.Content = "Disconnect Session";
        }
        else if (_isHostConnected)
        {
            DisconnectButton.Content = "Disconnect Peer";
        }
        else
        {
            DisconnectButton.Content = "Stop Current Session";
        }

        if (_isHostAvailable && !_isHostConnected)
        {
            TopStatusBadgeText.Text = "HOST READY";
        }
        else if (_isHostConnected && !_isHostSharing)
        {
            TopStatusBadgeText.Text = "CONNECTED";
        }
        else if (_isHostSharing)
        {
            TopStatusBadgeText.Text = "SHARING";
        }
        else if (_isViewerConnectionPending)
        {
            TopStatusBadgeText.Text = "PENDING";
        }
        else if (_isViewerConnected)
        {
            TopStatusBadgeText.Text = "CONNECTED";
        }
        else
        {
            TopStatusBadgeText.Text = _isHostAvailable ? "READY" : "PAUSED";
        }
    }

    private async Task TryManualConnectAsync()
    {
        var hostAddress = ManualAddressTextBox.Text.Trim();

        if (!IPAddress.TryParse(hostAddress, out var ipAddress))
        {
            UpdateStatus("Enter a valid WiFi IP address, for example 192.168.1.15.", "ERROR");
            return;
        }

        if (LanNetworkHelper.IsLocalAddress(ipAddress))
        {
            UpdateStatus("Use the other PC's WiFi IP here, not this same device.", "ERROR");
            return;
        }

        var manualPeer = new ScreenSharePeerInfo
        {
            SessionId = hostAddress,
            DeviceName = hostAddress,
            HostAddress = hostAddress,
            Port = PeerSessionHostService.DefaultControlPort,
            IsAvailable = true,
            LastSeenUtc = DateTime.UtcNow
        };

        await ConnectToPeerAsync(manualPeer);
    }

    private async Task ConnectToPeerAsync(ScreenSharePeerInfo selectedPeer)
    {
        await ShutdownHostAsync();
        await ShutdownClientAsync(restoreHostWhenIdle: false);
        _isViewerConnectionPending = true;

        _clientService = new PeerSessionClient();
        _clientService.Connected += ClientService_OnConnected;
        _clientService.ConnectionRejected += ClientService_OnConnectionRejected;
        _clientService.SharingStarted += ClientService_OnSharingStarted;
        _clientService.SharingStopped += ClientService_OnSharingStopped;
        _clientService.FrameReceived += ClientService_OnFrameReceived;
        _clientService.Disconnected += ClientService_OnDisconnected;

        UpdateStatus($"Request sent to {selectedPeer.DeviceName}. Waiting for approval...", "PENDING");

        try
        {
            await _clientService.ConnectAsync(selectedPeer);
            _connectedPeerName = selectedPeer.DeviceName;
            EnsureViewerWindow();
            _viewerWindow!.SetPeerName(selectedPeer.DeviceName);
            _viewerWindow.SetStatus("Connection request sent. Waiting for approval...");
            _viewerWindow.SetConnectedBadge("PENDING", "Waiting for the other person to approve");
            _viewerWindow.ResetToWaiting();
            _viewerWindow.Show();
            ManualAddressTextBox.Text = selectedPeer.HostAddress;
        }
        catch
        {
            _isViewerConnectionPending = false;
            UpdateStatus("Could not send the request. Make sure the other device has the app open on the same WiFi.", "ERROR");
            await ShutdownClientAsync();
        }

        UpdateUiState();
    }

    private void HostService_OnConnectionRequestReceived(string peerName)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            var result = System.Windows.MessageBox.Show(
                this,
                $"{peerName} wants to connect and view your screen. Do you want to approve this request?",
                "Incoming Screen Share Request",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (_hostService is null)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus($"Approved {peerName}. Starting live screen sharing...", "CONNECTING");
                await _hostService.ApprovePendingRequestAsync();
                await _hostService.StartSharingAsync();
                return;
            }

            await _hostService.RejectPendingRequestAsync("The other person declined your screen share request.");
            UpdateStatus($"Declined the request from {peerName}.", "READY");
            UpdateUiState();
        });
    }

    private static string GetLocalAddressSummary()
    {
        return GetLocalAddressSummary(LanNetworkHelper.GetLocalIpv4AddressStrings());
    }

    private static string GetLocalAddressSummary(IReadOnlyList<string> localAddresses)
    {
        return localAddresses.Count > 0
            ? $"WiFi address: {string.Join(", ", localAddresses)}"
            : "WiFi address: not detected yet";
    }
}
