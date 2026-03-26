namespace TeliLandOverlay;

public sealed class ScreenSharePeerInfo
{
    public required string SessionId { get; init; }

    public required string DeviceName { get; init; }

    public required string HostAddress { get; init; }

    public required int Port { get; init; }

    public bool IsAvailable { get; init; }

    public DateTime LastSeenUtc { get; init; }
}
