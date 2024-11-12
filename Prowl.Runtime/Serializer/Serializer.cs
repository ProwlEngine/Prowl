// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Serialization.Formatters;

namespace Prowl.Runtime;
// Classes used to test serialization
//public class TestSerialize
//{
//    public object NullField = null;
//    public Guid GuidField = Guid.NewGuid();
//    public byte ByteField = 123;
//    public sbyte SByteField = -123;
//    public short ShortField = 12345;
//    public int IntField = 123456789;
//    public long LongField = 1234567890123456789;
//    public ushort UShortField = 12345;
//    public uint UIntField = 123456789;
//    public ulong ULongField = 1234567890123456789;
//    public float FloatField = 123.456f;
//    public double DoubleField = 123.456789;
//    public decimal DecimalField = 123.456789m;
//    public string StringField = "Hello World!";
//    public byte[] ByteArrayField = new byte[] { 1, 2, 3, 4, 5 };
//    public bool BoolField = true;
//    public TestSimpleSerialize ObjectField = new();
//    public List<TestSimpleSerialize> ListField = new() { new(), new() };
//    public Dictionary<string, TestSimpleSerialize> DictionaryField = new() { { "1", new() }, { "2", new() } };
//
//}
//
//public class TestSimpleSerialize
//{
//    public string StringField = "Hello World!";
//    public byte[] ByteArrayField = new byte[] { 1, 2, 3, 4, 5 };
//}
public interface ISerializationFormat
{
    bool CanHandle(Type type);
    SerializedProperty Serialize(object value, Serializer.SerializationContext context);
    object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context);
}

public static class Serializer
{
    public class SerializationContext
    {
        private class NullKey { }

        public Dictionary<object, int> objectToId = new(ReferenceEqualityComparer.Instance);
        public Dictionary<int, object> idToObject = [];
        public int nextId = 1;
        private int dependencyCounter = 0;
        public HashSet<Guid> dependencies = [];

        public SerializationContext()
        {
            objectToId.Clear();
            objectToId.Add(new NullKey(), 0);
            idToObject.Clear();
            idToObject.Add(0, new NullKey());
            nextId = 1;
            dependencies.Clear();
        }

        public void AddDependency(Guid guid)
        {
            if (dependencyCounter > 0)
                dependencies.Add(guid);
            else throw new InvalidOperationException("Cannot add a dependency outside of a BeginDependencies/EndDependencies block.");
        }

        public void BeginDependencies()
        {
            dependencyCounter++;
        }

        public HashSet<Guid> EndDependencies()
        {
            dependencyCounter--;
            if (dependencyCounter == 0)
                return dependencies;
            return [];
        }
    }

    private static readonly List<ISerializationFormat> formats = [];

    static Serializer()
    {
        // Register built-in formats in order of precedence
        formats.Add(new PrimitiveFormat());
        formats.Add(new NullableFormat());
        formats.Add(new DateTimeFormat());
        formats.Add(new GuidFormat());
        formats.Add(new EnumFormat());
        formats.Add(new HashSetFormat());
        formats.Add(new CollectionFormat());
        formats.Add(new DictionaryFormat());
        formats.Add(new AnyObjectFormat()); // Fallback format - must be last
    }

    public static void RegisterFormat(ISerializationFormat format)
    {
        formats.Insert(0, format); // Add to start for precedence - Also ensures ObjectFormat is last
    }

    public static SerializedProperty Serialize(object? value) => Serialize(value, new SerializationContext());

    public static SerializedProperty Serialize(object? value, SerializationContext context)
    {
        if (value == null) return new SerializedProperty(PropertyType.Null, null);

        if (value is SerializedProperty property)
        {
            SerializedProperty clone = property.Clone();
            HashSet<Guid> deps = [];
            clone.GetAllAssetRefs(ref deps);
            foreach (Guid dep in deps)
                context.AddDependency(dep);
            return clone;
        }

        ISerializationFormat? format = formats.FirstOrDefault(f => f.CanHandle(value.GetType()))
            ?? throw new NotSupportedException($"No format handler found for type {value.GetType()}");

        return format.Serialize(value, context);
    }

    public static T? Deserialize<T>(SerializedProperty value) => (T?)Deserialize(value, typeof(T));
    public static object? Deserialize(SerializedProperty value, Type targetType) => Deserialize(value, targetType, new SerializationContext());
    public static T? Deserialize<T>(SerializedProperty value, SerializationContext context) => (T?)Deserialize(value, typeof(T), context);
    public static object? Deserialize(SerializedProperty value, Type targetType, SerializationContext context)
    {
        if (value == null || value.TagType == PropertyType.Null) return null;

        if (value.GetType() == targetType) return value;

        ISerializationFormat format = formats.FirstOrDefault(f => f.CanHandle(targetType))
            ?? throw new NotSupportedException($"No format handler found for type {targetType}");

        return format.Deserialize(value, targetType, context);
    }
}
