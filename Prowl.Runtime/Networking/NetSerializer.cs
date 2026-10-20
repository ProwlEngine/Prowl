// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Ember;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime;

/// <summary>
/// Writes and reads values of type <typeparamref name="T"/> for <see cref="SyncAttribute"/> properties
/// and RPC arguments.
/// </summary>
/// <remarks>
/// Supported: <see cref="bool"/>, the integer types, <see cref="float"/>, <see cref="double"/>,
/// <see cref="string"/>, <see cref="Guid"/>, enums, the <c>Float</c>/<c>Double</c>/<c>Int</c> vectors,
/// <see cref="Quaternion"/>, <see cref="Color"/>, and references to a <see cref="GameObject"/> or
/// <see cref="Component"/> (sent by identifier, null if the receiver doesn't have it). Anything else
/// throws <see cref="NotSupportedException"/> on first use.
/// </remarks>
public static class NetSerializer<T>
{
    public delegate void Writer(NetWriter writer, T value);

    public delegate T Reader(ref NetReader reader);

    // Rebuilt for each type rather than carried over a hot reload.
    [ReloadIgnore] private static readonly Writer? s_write;
    [ReloadIgnore] private static readonly Reader? s_read;

    static NetSerializer() => (s_write, s_read) = NetSerializers.Create<T>();

    public static bool IsSupported => s_write != null;

    public static void Write(NetWriter writer, T value)
    {
        if (s_write == null)
            throw Unsupported();
        s_write(writer, value);
    }

    public static T Read(ref NetReader reader)
    {
        if (s_read == null)
            throw Unsupported();
        return s_read(ref reader);
    }

    private static NotSupportedException Unsupported() =>
        new($"{typeof(T)} can't be sent over the network. Sync and RPC values must be one of the types listed on NetSerializer<T>.");
}

internal static class NetSerializers
{
    public static (NetSerializer<T>.Writer?, NetSerializer<T>.Reader?) Create<T>()
    {
        Type t = typeof(T);

        if (t == typeof(bool)) return Pair<T, bool>((w, v) => w.WriteBool(v), (ref NetReader r) => r.ReadBool());
        if (t == typeof(byte)) return Pair<T, byte>((w, v) => w.WriteByte(v), (ref NetReader r) => r.ReadByte());
        if (t == typeof(sbyte)) return Pair<T, sbyte>((w, v) => w.WriteSByte(v), (ref NetReader r) => r.ReadSByte());
        if (t == typeof(short)) return Pair<T, short>((w, v) => w.WriteVarInt32(v), (ref NetReader r) => checked((short)r.ReadVarInt32()));
        if (t == typeof(ushort)) return Pair<T, ushort>((w, v) => w.WriteVarUInt32(v), (ref NetReader r) => checked((ushort)r.ReadVarUInt32()));
        if (t == typeof(int)) return Pair<T, int>((w, v) => w.WriteVarInt32(v), (ref NetReader r) => r.ReadVarInt32());
        if (t == typeof(uint)) return Pair<T, uint>((w, v) => w.WriteVarUInt32(v), (ref NetReader r) => r.ReadVarUInt32());
        if (t == typeof(long)) return Pair<T, long>((w, v) => w.WriteVarInt64(v), (ref NetReader r) => r.ReadVarInt64());
        if (t == typeof(ulong)) return Pair<T, ulong>((w, v) => w.WriteVarUInt64(v), (ref NetReader r) => r.ReadVarUInt64());
        if (t == typeof(float)) return Pair<T, float>((w, v) => w.WriteSingle(v), (ref NetReader r) => r.ReadSingle());
        if (t == typeof(double)) return Pair<T, double>((w, v) => w.WriteDouble(v), (ref NetReader r) => r.ReadDouble());
        if (t == typeof(string)) return Pair<T, string>((w, v) => w.WriteString(v), (ref NetReader r) => r.ReadString());
        if (t == typeof(Guid)) return Pair<T, Guid>((w, v) => w.WriteGuid(v), (ref NetReader r) => r.ReadGuid());

        if (t == typeof(Float2)) return Pair<T, Float2>((w, v) => { w.WriteSingle(v.X); w.WriteSingle(v.Y); }, (ref NetReader r) => new Float2(r.ReadSingle(), r.ReadSingle()));
        if (t == typeof(Float3)) return Pair<T, Float3>(WriteFloat3, ReadFloat3);
        if (t == typeof(Float4)) return Pair<T, Float4>((w, v) => { w.WriteSingle(v.X); w.WriteSingle(v.Y); w.WriteSingle(v.Z); w.WriteSingle(v.W); },
            (ref NetReader r) => new Float4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        if (t == typeof(Double2)) return Pair<T, Double2>((w, v) => { w.WriteDouble(v.X); w.WriteDouble(v.Y); }, (ref NetReader r) => new Double2(r.ReadDouble(), r.ReadDouble()));
        if (t == typeof(Double3)) return Pair<T, Double3>((w, v) => { w.WriteDouble(v.X); w.WriteDouble(v.Y); w.WriteDouble(v.Z); },
            (ref NetReader r) => new Double3(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
        if (t == typeof(Int2)) return Pair<T, Int2>((w, v) => { w.WriteVarInt32(v.X); w.WriteVarInt32(v.Y); }, (ref NetReader r) => new Int2(r.ReadVarInt32(), r.ReadVarInt32()));
        if (t == typeof(Int3)) return Pair<T, Int3>((w, v) => { w.WriteVarInt32(v.X); w.WriteVarInt32(v.Y); w.WriteVarInt32(v.Z); },
            (ref NetReader r) => new Int3(r.ReadVarInt32(), r.ReadVarInt32(), r.ReadVarInt32()));
        if (t == typeof(Quaternion)) return Pair<T, Quaternion>(WriteQuaternion, ReadQuaternion);
        if (t == typeof(Color)) return Pair<T, Color>((w, v) => { w.WriteSingle(v.R); w.WriteSingle(v.G); w.WriteSingle(v.B); w.WriteSingle(v.A); },
            (ref NetReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));

        if (t.IsEnum)
            return ((w, v) => w.WriteVarInt64(EnumToInt64(v)), (ref NetReader r) => Int64ToEnum<T>(r.ReadVarInt64()));

        // Engine objects travel as their identifier, and come out as whatever the receiver has under it.
        if (typeof(GameObject).IsAssignableFrom(t) || typeof(Component).IsAssignableFrom(t))
        {
            return ((w, v) => w.WriteGuid(v switch { GameObject go => go.Identifier, Component c => c.Identifier, _ => Guid.Empty }),
                (ref NetReader r) => Networking.FindByIdentifier(r.ReadGuid()) is T found ? found : default!);
        }

        return (null, null);
    }

    internal static void WriteFloat3(NetWriter w, Float3 v)
    {
        w.WriteSingle(v.X);
        w.WriteSingle(v.Y);
        w.WriteSingle(v.Z);
    }

    internal static Float3 ReadFloat3(ref NetReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    internal static void WriteQuaternion(NetWriter w, Quaternion v)
    {
        w.WriteSingle(v.X);
        w.WriteSingle(v.Y);
        w.WriteSingle(v.Z);
        w.WriteSingle(v.W);
    }

    internal static Quaternion ReadQuaternion(ref NetReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    // The delegates are built for U and handed out as T's; the caller has checked that U is T.
    private static (NetSerializer<T>.Writer, NetSerializer<T>.Reader) Pair<T, U>(NetSerializer<U>.Writer write, NetSerializer<U>.Reader read) =>
        ((NetSerializer<T>.Writer)(object)write, (NetSerializer<T>.Reader)(object)read);

    private static long EnumToInt64<T>(T value) => Unsafe.SizeOf<T>() switch
    {
        1 => Unsafe.As<T, sbyte>(ref value),
        2 => Unsafe.As<T, short>(ref value),
        4 => Unsafe.As<T, int>(ref value),
        _ => Unsafe.As<T, long>(ref value),
    };

    private static T Int64ToEnum<T>(long value)
    {
        switch (Unsafe.SizeOf<T>())
        {
            case 1: { sbyte v = (sbyte)value; return Unsafe.As<sbyte, T>(ref v); }
            case 2: { short v = (short)value; return Unsafe.As<short, T>(ref v); }
            case 4: { int v = (int)value; return Unsafe.As<int, T>(ref v); }
            default: return Unsafe.As<long, T>(ref value);
        }
    }
}
