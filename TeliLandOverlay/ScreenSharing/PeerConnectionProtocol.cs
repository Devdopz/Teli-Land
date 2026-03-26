using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace TeliLandOverlay;

public static class PeerConnectionProtocol
{
    public static async Task WritePacketAsync(Stream stream, PeerPacketType packetType, byte[]? payload, CancellationToken cancellationToken)
    {
        var content = payload ?? [];
        var headerBuffer = new byte[5];
        headerBuffer[0] = (byte)packetType;
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(1), content.Length);

        await stream.WriteAsync(headerBuffer, cancellationToken);

        if (content.Length > 0)
        {
            await stream.WriteAsync(content, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    public static async Task WriteJsonPacketAsync<T>(Stream stream, PeerPacketType packetType, T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await WritePacketAsync(stream, packetType, bytes, cancellationToken);
    }

    public static async Task<(PeerPacketType PacketType, byte[] Payload)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[5];
        await FillBufferAsync(stream, headerBuffer, cancellationToken);

        var packetType = (PeerPacketType)headerBuffer[0];
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(1));
        var payload = payloadLength > 0 ? new byte[payloadLength] : [];

        if (payloadLength > 0)
        {
            await FillBufferAsync(stream, payload, cancellationToken);
        }

        return (packetType, payload);
    }

    public static T? DeserializeJson<T>(byte[] payload)
    {
        return payload.Length == 0 ? default : JsonSerializer.Deserialize<T>(payload);
    }

    private static async Task FillBufferAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var readCount = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);

            if (readCount == 0)
            {
                throw new EndOfStreamException("The peer closed the connection.");
            }

            totalRead += readCount;
        }
    }
}
