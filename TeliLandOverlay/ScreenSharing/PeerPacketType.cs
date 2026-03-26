namespace TeliLandOverlay;

public enum PeerPacketType : byte
{
    Handshake = 1,
    ConnectionRequest = 2,
    ConnectionAccepted = 3,
    ConnectionRejected = 4,
    ShareStarted = 5,
    ShareStopped = 6,
    Frame = 7,
    Disconnect = 8
}
