namespace Prowl.Wicked.Network.Serialization;

using Prowl.Echo;
using Prowl.Wicked.Network.Messages;

/// <summary>
/// Static helper for network serialization using Prowl.Echo.
/// Provides simple methods for serializing/deserializing objects to/from byte arrays.
/// </summary>
public static class NetworkSerializer
{
    /// <summary>
    /// Serializes any object to a byte array using Echo.
    /// </summary>
    public static byte[] Serialize<T>(T value)
    {
        var echo = Serializer.Serialize(value);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        echo.WriteToBinary(bw);
        return ms.ToArray();
    }

    /// <summary>
    /// Serializes any object to a byte array using Echo (non-generic version).
    /// </summary>
    public static byte[] Serialize(object? value)
    {
        var echo = Serializer.Serialize(value);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        echo.WriteToBinary(bw);
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a byte array to an object of type T using Echo.
    /// </summary>
    public static T? Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
            return default;

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        var echo = EchoObject.ReadFromBinary(br);
        return Serializer.Deserialize<T>(echo);
    }

    /// <summary>
    /// Deserializes a byte array to an object of type T using Echo.
    /// </summary>
    public static T? Deserialize<T>(ArraySegment<byte> data)
    {
        if (data.Array == null || data.Count == 0)
            return default;

        using var ms = new MemoryStream(data.Array, data.Offset, data.Count);
        using var br = new BinaryReader(ms);
        var echo = EchoObject.ReadFromBinary(br);
        return Serializer.Deserialize<T>(echo);
    }

    /// <summary>
    /// Deserializes a byte array to an object using Echo (non-generic version).
    /// </summary>
    public static object? Deserialize(byte[] data, Type type)
    {
        if (data == null || data.Length == 0)
            return null;

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        var echo = EchoObject.ReadFromBinary(br);
        return Serializer.Deserialize(echo, type);
    }

    /// <summary>
    /// Packs a network message with its ID for transmission.
    /// Format: [ushort messageId][Echo-serialized message]
    /// </summary>
    public static byte[] PackMessage<T>(T message) where T : INetworkMessage
    {
        var messageId = MessageRegistry.GetMessageId<T>();
        var echo = Serializer.Serialize(message);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Write message ID first
        bw.Write(messageId);

        // Write Echo-serialized message
        echo.WriteToBinary(bw);

        return ms.ToArray();
    }

    /// <summary>
    /// Unpacks a network message, returning the message ID and creating the message object.
    /// </summary>
    public static (ushort messageId, T message) UnpackMessage<T>(ArraySegment<byte> data) where T : INetworkMessage
    {
        using var ms = new MemoryStream(data.Array!, data.Offset, data.Count);
        using var br = new BinaryReader(ms);

        // Read message ID
        var messageId = br.ReadUInt16();

        // Read Echo-serialized message
        var echo = EchoObject.ReadFromBinary(br);
        var message = Serializer.Deserialize<T>(echo);

        return (messageId, message!);
    }

    /// <summary>
    /// Reads just the message ID from data without deserializing the message.
    /// </summary>
    public static ushort ReadMessageId(ArraySegment<byte> data)
    {
        if (data.Array == null || data.Count < 2)
            throw new InvalidOperationException("Data too short to contain message ID");

        return (ushort)(data.Array[data.Offset] | (data.Array[data.Offset + 1] << 8));
    }

    /// <summary>
    /// Deserializes a message from data (after message ID has been read).
    /// </summary>
    public static T DeserializeMessage<T>(ArraySegment<byte> data, int offset = 2) where T : INetworkMessage
    {
        using var ms = new MemoryStream(data.Array!, data.Offset + offset, data.Count - offset);
        using var br = new BinaryReader(ms);
        var echo = EchoObject.ReadFromBinary(br);
        return Serializer.Deserialize<T>(echo)!;
    }

    /// <summary>
    /// Deserializes a message from data using a BinaryReader positioned after the message ID.
    /// </summary>
    public static T DeserializeMessage<T>(BinaryReader reader) where T : INetworkMessage
    {
        var echo = EchoObject.ReadFromBinary(reader);
        return Serializer.Deserialize<T>(echo)!;
    }

    /// <summary>
    /// Deserializes a message from data using a BinaryReader positioned after the message ID (non-generic).
    /// </summary>
    public static object DeserializeMessage(BinaryReader reader, Type messageType)
    {
        var echo = EchoObject.ReadFromBinary(reader);
        return Serializer.Deserialize(echo, messageType)!;
    }
}
