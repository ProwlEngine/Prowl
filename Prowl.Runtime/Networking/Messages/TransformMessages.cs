// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A moving position goes as a grid position, or with <see cref="BaselineAge" /> as the step from the one this
///     receiver had for the object that many snapshot ticks before. A settled pose goes exact.
/// </summary>
internal readonly record struct TransformRecord(
    uint NetId,
    uint Generation,
    TransformUpdate Update,
    uint BaselineAge = 0,
    GridPosition Grid = default)
{
    // A moving position takes at most 1 + 3 * 7 bytes, so with the compact rotation it fits in an exact pose's 12 + 16.
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
            if ((channels & TransformChannels.Exact) != 0)
            {
                NetSerializers.WriteFloat3(writer, Update.Position);
            }
            else
            {
                writer.WriteVarUInt32(BaselineAge);
                writer.WriteVarInt64(Grid.X);
                writer.WriteVarInt64(Grid.Y);
                writer.WriteVarInt64(Grid.Z);
            }
        }

        if ((channels & TransformChannels.Rotation) != 0)
        {
            if ((channels & TransformChannels.Exact) != 0)
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
        Float3 position = default;
        uint baselineAge = 0;
        GridPosition grid = default;
        if ((channels & (TransformChannels.Position | TransformChannels.Exact)) ==
            (TransformChannels.Position | TransformChannels.Exact))
        {
            position = NetSerializers.ReadFloat3(ref reader);
        }
        else if ((channels & TransformChannels.Position) != 0)
        {
            baselineAge = reader.ReadVarUInt32();
            grid = new GridPosition(reader.ReadVarInt64(), reader.ReadVarInt64(), reader.ReadVarInt64());
        }

        Quaternion rotation = (channels & TransformChannels.Rotation) == 0 ? Quaternion.Identity :
            (channels & TransformChannels.Exact) != 0 ? NetSerializers.ReadQuaternion(ref reader) :
            NetSerializers.ReadCompactQuaternion(ref reader);
        Float3 scale = (channels & TransformChannels.Scale) != 0 ? NetSerializers.ReadFloat3(ref reader) : default;

        NetworkValidation.Transform(stillFor, position, rotation, scale);

        return new TransformRecord(netId, generation,
            new TransformUpdate(channels, stillFor, position, rotation, scale), baselineAge, grid);
    }

    internal bool HasGridPosition =>
        (Update.Channels & (TransformChannels.Position | TransformChannels.Exact)) == TransformChannels.Position;
}

/// <summary>A position on the <see cref="NetworkConfiguration.PositionPrecision" /> grid, or a step between two.</summary>
internal readonly record struct GridPosition(long X, long Y, long Z)
{
    // Far beyond any real world, and a step between two takes at most 7 varint bytes.
    private const double Limit = 1L << 46;

    /// <summary>The precision as the decimal it was written as, so whole units land exactly on the grid.</summary>
    internal static double Step(float precision) => (double)(decimal)precision;

    internal static GridPosition Of(Float3 position, double step) =>
        new(Snap(position.X, step), Snap(position.Y, step), Snap(position.Z, step));

    internal Float3 ToFloat3(double step) => new((float)(X * step), (float)(Y * step), (float)(Z * step));

    private static long Snap(float value, double step) => (long)Math.Clamp(Math.Round(value / step), -Limit, Limit);

    public static GridPosition operator +(GridPosition a, GridPosition b) =>
        new(unchecked(a.X + b.X), unchecked(a.Y + b.Y), unchecked(a.Z + b.Z));

    public static GridPosition operator -(GridPosition a, GridPosition b) =>
        new(unchecked(a.X - b.X), unchecked(a.Y - b.Y), unchecked(a.Z - b.Z));
}

[Flags]
internal enum TransformChannels : byte
{
    Position = 1,
    Rotation = 2,
    Scale = 4,

    // The pose it settled at, exact: position as floats and the full 16 byte rotation rather than the compact 4.
    Exact = 8,

    // It was still until the previous tick: receivers hold it at rest until then, then blend from there.
    Resumed = 16,

    Pose = Position | Rotation | Scale,
    All = Pose | Exact | Resumed
}

/// <summary>One transform record: the channels it carries and their values. Channels left out are unchanged.</summary>
internal readonly record struct TransformUpdate(
    TransformChannels Channels,
    float StillFor,
    Float3 Position,
    Quaternion Rotation,
    Float3 Scale);
