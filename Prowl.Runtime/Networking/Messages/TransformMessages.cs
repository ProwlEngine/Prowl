// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Header followed by TransformRecord values until the end of the message.</summary>
internal readonly record struct TransformMessage(uint Tick, double SentAt)
{
    internal const int HeaderSize = 5 + 8;
    internal const int MinMessageSize = sizeof(ushort) + HeaderSize + TransformRecord.MaxSize;

    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(Tick);
        writer.WriteDouble(SentAt);
    }

    internal static TransformMessage Read(ref NetReader reader) => new(
        reader.ReadVarUInt32(),
        reader.ReadDouble());
}

internal readonly record struct TransformRecord(uint NetId, uint Generation, TransformUpdate Update)
{
    internal const int MaxSize = 5 + 5 + 1 + 4 + 12 + 16 + 12;

    internal void Write(NetWriter writer)
    {
        TransformChannels channels = Update.Channels;
        writer.WriteVarUInt32(NetId);
        writer.WriteVarUInt32(Generation);
        writer.WriteByte((byte)channels);
        if ((channels & TransformChannels.Resumed) != 0)
        {
            writer.WriteSingle(Update.StillFor);
        }

        if ((channels & TransformChannels.Position) != 0)
        {
            NetSerializers.WriteFloat3(writer, Update.Position);
        }

        if ((channels & TransformChannels.Rotation) != 0)
        {
            if ((channels & TransformChannels.ExactRotation) != 0)
            {
                NetSerializers.WriteQuaternion(writer, Update.Rotation);
            }
            else
            {
                NetSerializers.WriteCompactQuaternion(writer, Update.Rotation);
            }
        }

        if ((channels & TransformChannels.Scale) != 0)
        {
            NetSerializers.WriteFloat3(writer, Update.Scale);
        }
    }

    internal static TransformRecord Read(ref NetReader reader)
    {
        uint netId = reader.ReadVarUInt32();
        uint generation = reader.ReadVarUInt32();
        TransformChannels channels = (TransformChannels)reader.ReadByte();
        NetworkValidation.ValidateTransformChannels(channels);

        float stillFor = (channels & TransformChannels.Resumed) != 0 ? reader.ReadSingle() : 0;
        Float3 position = (channels & TransformChannels.Position) != 0
            ? NetSerializers.ReadFloat3(ref reader)
            : default;
        Quaternion rotation = (channels & TransformChannels.Rotation) == 0 ? Quaternion.Identity :
            (channels & TransformChannels.ExactRotation) != 0 ? NetSerializers.ReadQuaternion(ref reader) :
            NetSerializers.ReadCompactQuaternion(ref reader);
        Float3 scale = (channels & TransformChannels.Scale) != 0 ? NetSerializers.ReadFloat3(ref reader) : default;

        NetworkValidation.Transform(stillFor, position, rotation, scale);

        return new TransformRecord(netId, generation,
            new TransformUpdate(channels, stillFor, position, rotation, scale));
    }
}

[Flags]
internal enum TransformChannels : byte
{
    Position = 1,
    Rotation = 2,
    Scale = 4,

    // The rotation is the full 16 bytes, as a settled pose sends it, rather than the compact 4.
    ExactRotation = 8,

    // It was still until the previous tick: receivers hold it at rest until then, then blend from there.
    Resumed = 16,

    Pose = Position | Rotation | Scale,
    All = Pose | ExactRotation | Resumed
}

/// <summary>One transform record: the channels it carries and their values. Channels left out are unchanged.</summary>
internal readonly record struct TransformUpdate(
    TransformChannels Channels,
    float StillFor,
    Float3 Position,
    Quaternion Rotation,
    Float3 Scale);
