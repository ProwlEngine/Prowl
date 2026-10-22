// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A type of yours that writes and reads itself on the network, instead of being sent member by member.
///     Prowl's script compiler registers it with <see cref="NetSerializer{T}" /> when it's used.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface INetSerializable<TSelf> where TSelf : INetSerializable<TSelf>
{
    static abstract void Write(NetWriter writer, TSelf value);

    static abstract TSelf Read(ref NetReader reader);
}

/// <summary>
///     How the serializers Prowl's script compiler generates for a game's own types are registered and run.
///     Not meant to be called by hand.
/// </summary>
/// <remarks>
///     <para>
///         A struct is its members, in order. A class value starts with a tag: null, the declared type, another
///         type (any registered type the declared one can hold, by its id), or an object already sent within
///         the same value (by its index), so references between objects, cycles included, arrive as they
///         were sent. An object is known before its members are read, so it can be referred to from them.
///     </para>
///     <para>
///         "The same value" is one [Sync] value, RPC argument or collection item: what one outermost
///         serializer call writes.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NetTypeSerializer
{
    public delegate void MemberReader<T>(ref NetReader reader, ref T value);

    public delegate void MemberWriter<T>(NetWriter writer, ref T value);

    private const byte Null = 0, Declared = 1, Other = 2, Reference = 3;

    // Deeper than this is most likely a peer sending garbage, and would run out of stack before long.
    private const int MaximumDepth = 256;

    // Weak, so a script assembly that hot reload replaced can still unload; the newer one registers the same ids again.
    private static readonly ConditionalWeakTable<Type, Shape> s_byType = new();
    private static readonly Dictionary<uint, WeakReference<Shape>> s_byId = new();
    private static readonly object s_lock = new();

    // The objects of the value being written or read on this thread, and how deep in it we are.
    [ThreadStatic] private static Dictionary<object, int>? t_written;
    [ThreadStatic] private static List<object>? t_read;
    [ThreadStatic] private static int t_writeDepth;
    [ThreadStatic] private static int t_readDepth;

    public static void RegisterSelf<T>() where T : INetSerializable<T> => NetSerializer<T>.Register(T.Write, T.Read);

    public static void RegisterStruct<T>(Func<T> create, MemberWriter<T> write, MemberReader<T> read)
        where T : struct =>
        NetSerializer<T>.Register(
            (w, v) =>
            {
                EnterWrite();
                try
                {
                    write(w, ref v);
                }
                finally
                {
                    ExitWrite();
                }
            },
            (ref r) =>
            {
                EnterRead();
                try
                {
                    T v = create();
                    read(ref r, ref v);
                    return v;
                }
                finally
                {
                    ExitRead();
                }
            });

    /// <summary>A class that can be sent as itself, or as any of its bases or interfaces that are registered too.</summary>
    public static void RegisterClass<T>(uint id, Func<T>? create, MemberWriter<T> write, MemberReader<T> read)
        where T : class
    {
        Shape shape = new(typeof(T), id, create, (w, o) =>
        {
            T value = (T)o;
            write(w, ref value);
        }, (ref r, o) =>
        {
            T value = (T)o;
            read(ref r, ref value);
        });

        lock (s_lock)
        {
            s_byType.AddOrUpdate(typeof(T), shape);
            s_byId[id] = new WeakReference<Shape>(shape);
        }

        RegisterBase<T>();
    }

    /// <summary>A class or interface that values are sent as, whatever registered type they really are.</summary>
    public static void RegisterBase<T>() where T : class => NetSerializer<T>.Register(WriteObject, ReadObject<T>);

    private static void WriteObject<T>(NetWriter writer, T? value) where T : class
    {
        if (value is null)
        {
            writer.WriteByte(Null);
            return;
        }

        EnterWrite();
        try
        {
            if (t_written!.TryGetValue(value, out int index))
            {
                writer.WriteByte(Reference);
                writer.WriteVarUInt32((uint)index);
                return;
            }

            Type type = value.GetType();
            if (!s_byType.TryGetValue(type, out Shape? shape))
            {
                throw new NotSupportedException(
                    $"{type} was sent as a {typeof(T)}, but has no network serializer. Only types declared in game scripts get one, and not generic ones.");
            }

            t_written.Add(value, t_written.Count);
            if (type == typeof(T))
            {
                writer.WriteByte(Declared);
            }
            else
            {
                writer.WriteByte(Other);
                writer.WriteUInt32(shape.Id);
            }

            shape.Write(writer, value);
        }
        finally
        {
            ExitWrite();
        }
    }

    // Null is a value like any other here; [Sync] class properties are as nullable as the game declares them.
    private static T ReadObject<T>(ref NetReader reader) where T : class
    {
        byte tag = reader.ReadByte();
        if (tag == Null)
        {
            return null!;
        }

        EnterRead();
        try
        {
            if (tag == Reference)
            {
                int index = (int)reader.ReadVarUInt32();
                return index < t_read!.Count && t_read[index] is T known
                    ? known
                    : throw new InvalidDataException(
                        $"A {typeof(T)} referred to an object that wasn't sent, or isn't one.");
            }

            Shape? shape = tag switch
            {
                Declared => s_byType.TryGetValue(typeof(T), out Shape? declared) ? declared : null,
                Other => Find(reader.ReadUInt32()),
                _ => throw new InvalidDataException($"Unknown object tag {tag}.")
            };

            if (shape?.Create == null || !typeof(T).IsAssignableFrom(shape.Type))
            {
                throw new InvalidDataException($"A {typeof(T)} came as a type this peer doesn't have, or can't make.");
            }

            object value = shape.Create();
            t_read!.Add(value);
            shape.Read(ref reader, value);
            return (T)value;
        }
        finally
        {
            ExitRead();
        }
    }

    private static Shape? Find(uint id)
    {
        lock (s_lock)
        {
            return s_byId.TryGetValue(id, out WeakReference<Shape>? weak) && weak.TryGetTarget(out Shape? shape)
                ? shape
                : null;
        }
    }

    private static void EnterWrite()
    {
        if (++t_writeDepth > MaximumDepth)
        {
            t_writeDepth--;
            throw new InvalidOperationException(
                $"A value nests objects more than {MaximumDepth} deep, which is too deep to send.");
        }

        t_written ??= new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
    }

    private static void ExitWrite()
    {
        if (--t_writeDepth == 0)
        {
            t_written!.Clear();
        }
    }

    private static void EnterRead()
    {
        if (++t_readDepth > MaximumDepth)
        {
            t_readDepth--;
            throw new InvalidDataException($"A received value nests objects more than {MaximumDepth} deep.");
        }

        t_read ??= new List<object>();
    }

    private static void ExitRead()
    {
        if (--t_readDepth == 0)
        {
            t_read!.Clear();
        }
    }

    /// <summary>A class the generated code can make and fill, keyed by its type and by its id on the wire.</summary>
    private sealed record Shape(
        Type Type,
        uint Id,
        Func<object>? Create,
        Action<NetWriter, object> Write,
        ShapeReader Read);

    private delegate void ShapeReader(ref NetReader reader, object value);
}
