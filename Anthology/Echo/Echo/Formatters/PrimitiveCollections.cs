// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Formatters;

// Reads and writes one dimensional arrays and List<T> of primitives without boxing each element.
internal static class PrimitiveCollections
{
    private abstract class Codec
    {
        public abstract List<EchoObject>? Write(object collection);
        public abstract Array? ReadArray(List<EchoObject> tags);
        public abstract bool ReadList(object list, List<EchoObject> tags);
    }

    private sealed class Codec<T> : Codec
    {
        private readonly EchoType _tagType;
        private readonly Func<T, EchoObject> _create;
        private readonly Func<EchoObject, T> _read;

        public Codec(EchoType tagType, Func<T, EchoObject> create, Func<EchoObject, T> read)
        {
            _tagType = tagType;
            _create = create;
            _read = read;
        }

        public override List<EchoObject>? Write(object collection)
        {
            if (collection is T[] array)
            {
                var tags = new List<EchoObject>(array.Length);
                for (int i = 0; i < array.Length; i++)
                    tags.Add(_create(array[i]));
                return tags;
            }

            if (collection is List<T> list)
            {
                var tags = new List<EchoObject>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    tags.Add(_create(list[i]));
                return tags;
            }

            return null;
        }

        public override Array? ReadArray(List<EchoObject> tags)
        {
            if (!AllOfType(tags)) return null;

            var array = new T[tags.Count];
            for (int i = 0; i < array.Length; i++)
                array[i] = _read(tags[i]);
            return array;
        }

        public override bool ReadList(object list, List<EchoObject> tags)
        {
            if (list is not List<T> typed || !AllOfType(tags)) return false;

            if (typed.Capacity < typed.Count + tags.Count)
                typed.Capacity = typed.Count + tags.Count;
            for (int i = 0; i < tags.Count; i++)
                typed.Add(_read(tags[i]));
            return true;
        }

        // Only tags already of the element's own EchoType skip the serializer, so conversions behave as before.
        private bool AllOfType(List<EchoObject> tags)
        {
            for (int i = 0; i < tags.Count; i++)
                if (tags[i].TagType != _tagType)
                    return false;
            return true;
        }
    }

    private static readonly Dictionary<Type, Codec> Codecs = new()
    {
        [typeof(bool)] = new Codec<bool>(EchoType.Bool, v => new EchoObject(v), t => t.BoolValue),
        [typeof(char)] = new Codec<char>(EchoType.UShort, v => new EchoObject((ushort)v), t => (char)t.UShortValue),
        [typeof(byte)] = new Codec<byte>(EchoType.Byte, v => new EchoObject(v), t => t.ByteValue),
        [typeof(sbyte)] = new Codec<sbyte>(EchoType.sByte, v => new EchoObject(v), t => t.sByteValue),
        [typeof(short)] = new Codec<short>(EchoType.Short, v => new EchoObject(v), t => t.ShortValue),
        [typeof(ushort)] = new Codec<ushort>(EchoType.UShort, v => new EchoObject(v), t => t.UShortValue),
        [typeof(int)] = new Codec<int>(EchoType.Int, v => new EchoObject(v), t => t.IntValue),
        [typeof(uint)] = new Codec<uint>(EchoType.UInt, v => new EchoObject(v), t => t.UIntValue),
        [typeof(long)] = new Codec<long>(EchoType.Long, v => new EchoObject(v), t => t.LongValue),
        [typeof(ulong)] = new Codec<ulong>(EchoType.ULong, v => new EchoObject(v), t => t.ULongValue),
        [typeof(float)] = new Codec<float>(EchoType.Float, v => new EchoObject(v), t => t.FloatValue),
        [typeof(double)] = new Codec<double>(EchoType.Double, v => new EchoObject(v), t => t.DoubleValue),
        [typeof(decimal)] = new Codec<decimal>(EchoType.Decimal, v => new EchoObject(v), t => t.DecimalValue),
        [typeof(string)] = new Codec<string?>(EchoType.String, v => v == null ? new EchoObject(EchoType.Null, null) : new EchoObject(v), t => (string?)t.Value),
    };

    public static List<EchoObject>? TryWrite(object collection, Type elementType) =>
        Codecs.TryGetValue(elementType, out var codec) ? codec.Write(collection) : null;

    public static Array? TryReadArray(List<EchoObject> tags, Type elementType, SerializationContext context) =>
        context.OnDeserialize == null && Codecs.TryGetValue(elementType, out var codec) ? codec.ReadArray(tags) : null;

    public static bool TryReadList(object list, List<EchoObject> tags, Type elementType, SerializationContext context) =>
        context.OnDeserialize == null && Codecs.TryGetValue(elementType, out var codec) && codec.ReadList(list, tags);
}
