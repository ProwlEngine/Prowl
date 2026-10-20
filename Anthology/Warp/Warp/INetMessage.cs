// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>
/// A message sent through a <see cref="NetSession"/>. Each message type carries its own id, so it can be
/// sent without registering anything; register a handler with <see cref="NetSession.On{T}"/> to receive it.
/// </summary>
/// <remarks>Structs avoid an allocation per received message.</remarks>
public interface INetMessage
{
    /// <summary>
    /// Identifies this message type on the wire. Must be unique among your messages and below
    /// <see cref="NetSession.ReservedMessageIds"/>, which Warp uses itself.
    /// </summary>
    static abstract ushort Id { get; }

    void Write(NetWriter writer);

    /// <summary>Reads the fields written by <see cref="Write"/>. Throwing here (as <see cref="NetReader"/> does on bad data) counts as a protocol violation by the sender.</summary>
    void Read(ref NetReader reader);
}

/// <summary>Handles a received <see cref="INetMessage"/>.</summary>
public delegate void MessageHandler<T>(NetPeer sender, T message) where T : INetMessage;

/// <summary>Handles a message registered by id with <see cref="NetSession.OnRaw"/>, reading its payload directly.</summary>
public delegate void RawMessageHandler(NetPeer sender, ref NetReader reader, Delivery delivery);
