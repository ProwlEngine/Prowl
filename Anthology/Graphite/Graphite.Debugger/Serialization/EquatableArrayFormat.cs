using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class EquatableArrayFormat : ISerializationFormat
{
    private static readonly ConcurrentDictionary<Type, IArrayCodec> Codecs = new();

    public bool CanHandle(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EquatableArray<>);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        => Codec(value.GetType()).Write(value, context);

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
        => Codec(targetType).Read(value, context);

    private static IArrayCodec Codec(Type type)
        => Codecs.GetOrAdd(type, t => (IArrayCodec)Activator.CreateInstance(typeof(ArrayCodec<>).MakeGenericType(t.GetGenericArguments()[0]))!);

    private interface IArrayCodec
    {
        EchoObject Write(object value, SerializationContext context);

        object Read(EchoObject value, SerializationContext context);
    }

    private sealed class ArrayCodec<T> : IArrayCodec
    {
        public EchoObject Write(object value, SerializationContext context)
        {
            ImmutableArray<T> items = ((EquatableArray<T>)value).Items;
            if (typeof(T) == typeof(byte))
                return new EchoObject(((ImmutableArray<byte>)(object)items).ToArray());

            EchoObject list = EchoObject.NewList();
            foreach (T item in items)
                list.ListAdd(item is null ? new EchoObject(EchoType.Null, null) : Serializer.Serialize(typeof(T), item, context));
            return list;
        }

        public object Read(EchoObject value, SerializationContext context)
        {
            if (value.TagType == EchoType.Null)
                return EquatableArray<T>.Empty;

            if (typeof(T) == typeof(byte))
                return new EquatableArray<byte>(ImmutableArray.Create(value.ByteArrayValue));

            ImmutableArray<T>.Builder builder = ImmutableArray.CreateBuilder<T>(value.List.Count);
            foreach (EchoObject item in value.List)
                builder.Add((T)Serializer.Deserialize(item, typeof(T), context)!);
            return new EquatableArray<T>(builder.MoveToImmutable());
        }
    }
}
