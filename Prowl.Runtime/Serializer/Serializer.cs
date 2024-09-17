// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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

public static class Serializer
{
    public class SerializationContext
    {
        public readonly Dictionary<object, int> objectToId = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<int, object> idToObject = [];
        public int nextId = 1;
        private int dependencyCounter = 0;
        public readonly HashSet<Guid> dependencies = [];

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

    private class NullKey { }

    private static bool IsPrimitive(Type t) => t.IsPrimitive || t.IsAssignableTo(typeof(string)) || t.IsAssignableTo(typeof(decimal)) || t.IsAssignableTo(typeof(Guid)) || t.IsAssignableTo(typeof(DateTime)) || t.IsEnum || t.IsAssignableTo(typeof(byte[]));


    #region Serialize

    public static SerializedProperty Serialize(object? value) => Serialize(value, new());
    public static SerializedProperty Serialize(object? value, SerializationContext ctx)
    {
        if (value == null)
            return new SerializedProperty(PropertyType.Null, null);

        if (value is SerializedProperty t)
        {
            var clone = t.Clone();
            HashSet<Guid> deps = [];
            clone.GetAllAssetRefs(ref deps);
            foreach (var dep in deps)
                ctx.AddDependency(dep);
            return clone;
        }

        var type = value.GetType();
        if (IsPrimitive(type))
            return PrimitiveToTag(value);

        if (type.IsArray && value is Array array)
            return ArrayToListTag(array, ctx);

        var tag = DictionaryToTag(value, ctx);
        if (tag != null) return tag;

        if (value is IList iList)
            return IListToTag(iList, ctx);

        return SerializeObject(value, ctx);
    }

    private static SerializedProperty PrimitiveToTag(object p)
    {
        if (p is byte b) return new(PropertyType.Byte, b);
        else if (p is sbyte sb) return new(PropertyType.sByte, sb);
        else if (p is short s) return new(PropertyType.Short, s);
        else if (p is int i) return new(PropertyType.Int, i);
        else if (p is long l) return new(PropertyType.Long, l);
        else if (p is uint ui) return new(PropertyType.UInt, ui);
        else if (p is ulong ul) return new(PropertyType.ULong, ul);
        else if (p is ushort us) return new(PropertyType.UShort, us);
        else if (p is float f) return new(PropertyType.Float, f);
        else if (p is double d) return new(PropertyType.Double, d);
        else if (p is decimal dec) return new(PropertyType.Decimal, dec);
        else if (p is string str) return new(PropertyType.String, str);
        else if (p is byte[] bArr) return new(PropertyType.ByteArray, bArr);
        else if (p is bool bo) return new(PropertyType.Bool, bo);
        else if (p is DateTime date) return new(PropertyType.Long, date.ToBinary());
        else if (p is Guid g) return new(PropertyType.String, g.ToString());
        else if (p.GetType().IsEnum) return new(PropertyType.Int, Convert.ToInt32((Enum)p)); // Serialize as integers
        else throw new NotSupportedException("The type '" + p.GetType() + "' is not a supported primitive.");
    }

    private static SerializedProperty ArrayToListTag(Array array, SerializationContext ctx)
    {
        List<SerializedProperty> tags = [];
        for (int i = 0; i < array.Length; i++)
            tags.Add(Serialize(array.GetValue(i), ctx));
        return new SerializedProperty(tags);
    }

    private static SerializedProperty? DictionaryToTag(object obj, SerializationContext ctx)
    {
        var t = obj.GetType();
        if (obj is IDictionary dict &&
            t.IsGenericType &&
            t.GetGenericArguments()[0] == typeof(string))
        {
            SerializedProperty tag = new(PropertyType.Compound, null);
            foreach (DictionaryEntry kvp in dict)
                tag.Add((string)kvp.Key, Serialize(kvp.Value, ctx));
            return tag;
        }
        return null;
    }

    private static SerializedProperty IListToTag(IList iList, SerializationContext ctx)
    {
        List<SerializedProperty> tags = [];
        foreach (var item in iList)
            tags.Add(Serialize(item, ctx));
        return new SerializedProperty(tags);
    }

    private static SerializedProperty SerializeObject(object? value, SerializationContext ctx)
    {
        if (value == null) return new(PropertyType.Null, null); // ID defaults to 0 which is null or an Empty Compound

        var type = value.GetType();

        var compound = SerializedProperty.NewCompound();

        if (ctx.objectToId.TryGetValue(value, out int id))
        {
            compound["$id"] = new(PropertyType.Int, id);
            // Don't need to write compound data, its already been serialized at some point earlier
            return compound;
        }

        id = ctx.nextId++;
        ctx.objectToId[value] = id;
        ctx.idToObject[id] = value;
        ctx.BeginDependencies();

        if (value is ISerializationCallbackReceiver callback)
            callback.OnBeforeSerialize();

        if (value is ISerializable serializable)
        {
            // Manual Serialization
            compound = serializable.Serialize(ctx);
        }
        else
        {
            // Auto Serialization
            foreach (var field in value.GetSerializableFields())
            {
                string name = field.Name;

                var propValue = field.GetValue(value);
                if (propValue == null)
                {
                    if (Attribute.GetCustomAttribute(field, typeof(IgnoreOnNullAttribute)) != null) continue;
                    compound.Add(name, new(PropertyType.Null, null));
                }
                else
                {
                    SerializedProperty tag = Serialize(propValue, ctx);
                    compound.Add(name, tag);
                }
            }
        }

        compound["$id"] = new(PropertyType.Int, id);
        compound["$type"] = new(PropertyType.String, type.FullName);
        var dependencies = ctx.EndDependencies();
        //if(dependencies.Count > 0)
        //    compound["$dependencies"] = new(PropertyType.List, dependencies.Select(d => new SerializedProperty(PropertyType.String, d.ToString())).ToList());

        return compound;
    }

    #endregion

    #region Deserialize

    public static T? Deserialize<T>(SerializedProperty value) => (T?)Deserialize(value, typeof(T));

    public static object? Deserialize(SerializedProperty value, Type type) => Deserialize(value, type, new SerializationContext());

    public static T? Deserialize<T>(SerializedProperty value, SerializationContext ctx) => (T?)Deserialize(value, typeof(T), ctx);
    public static object? Deserialize(SerializedProperty value, Type targetType, SerializationContext ctx)
    {
        if (value.TagType == PropertyType.Null) return null;

        //if (value.GetType().IsAssignableTo(targetType)) return value; - Too loose, Will prevent type 'object' from being deserialized into its actual type
        if (value.GetType() == targetType) return value;

        if (IsPrimitive(targetType))
        {
            // Special Cases
            if (targetType.IsEnum)
                if (value.TagType == PropertyType.Int)
                    return Enum.ToObject(targetType, value.IntValue);

            if (targetType == typeof(DateTime))
                if (value.TagType == PropertyType.Long)
                    return DateTime.FromBinary(value.LongValue);

            if (targetType == typeof(Guid))
                if (value.TagType == PropertyType.String)
                    return Guid.Parse(value.StringValue);

            try
            {
                return Convert.ChangeType(value.Value, targetType);
            }
            catch
            {
                throw new Exception($"Failed to deserialize primitive '{targetType}' with value: {value.Value}");
            }
        }

        if (value.TagType == PropertyType.List)
        {
            if (targetType.IsArray)
            {
                // Deserialize List into Array
                Type type = targetType.GetElementType() ?? throw new InvalidOperationException("Array type is null");
                var array = Array.CreateInstance(type, value.Count);
                for (int idx = 0; idx < array.Length; idx++)
                    array.SetValue(Deserialize(value[idx], type, ctx), idx);
                return array;
            }
            else if (targetType.IsAssignableTo(typeof(IList)))
            {
                // IEnumerable covers many types, we need to find the type of element in the IEnumerable
                // For now just assume its the first generic argument
                Type type = targetType.GetGenericArguments()[0];
                var list2 = CreateInstance(targetType) as IList ?? throw new InvalidOperationException("Failed to create instance of type: " + targetType);
                foreach (var tag in value.List)
                    list2.Add(Deserialize(tag, type, ctx));
                return list2;

            }

            throw new InvalidCastException("ListTag cannot deserialize into type of: '" + targetType + "'");
        }
        else if (value.TagType == PropertyType.Compound)
        {
            if (targetType.IsAssignableTo(typeof(IDictionary)) &&
                targetType.IsGenericType &&
                targetType.GetGenericArguments()[0] == typeof(string))
            {
                var dict = CreateInstance(targetType) as IDictionary ?? throw new InvalidOperationException("Failed to create instance of type: " + targetType);
                var valueType = targetType.GetGenericArguments()[1];
                foreach (var tag in value.Tags)
                    dict.Add(tag.Key, Deserialize(tag.Value, valueType, ctx));
                return dict;
            }

            return DeserializeObject(value, ctx);
        }

        throw new NotSupportedException("The node type '" + value.GetType() + "' is not supported.");
    }

    private static object? DeserializeObject(SerializedProperty compound, SerializationContext ctx)
    {
        SerializedProperty? id = compound.Get("$id");
        if (id != null)
            if (ctx.idToObject.TryGetValue(id.IntValue, out object? existingObj))
                return existingObj;

        SerializedProperty? typeProperty = compound.Get("$type");
        if (typeProperty == null)
        {
            Debug.LogError($"Failed to deserialize object, missing object ID: {id?.IntValue}, Did the order of deserialization change?");
            return null;
        }

        string type = typeProperty.StringValue;
        if (string.IsNullOrWhiteSpace(type))
            return null;

        Type oType = RuntimeUtils.FindType(type);
        if (oType == null)
        {
            Debug.LogError($"Couldn't find Type: {type}, skipping...");

            // Its possible this type has other types inside it that got serialized
            // They may still be valid and other things may have references to it
            // If this fails it may delete all those as well since they can no longer deserialize
            // so lets try to skip past this type and see if we can deserialize the rest

            void Search(SerializedProperty prop)
            {
                foreach (var tag in prop.Tags)
                {
                    if (tag.Value.TagType == PropertyType.Compound)
                        _ = DeserializeObject(tag.Value, ctx);
                    else if (tag.Value.TagType == PropertyType.List)
                        foreach (var listTag in tag.Value.List)
                            Search(listTag);
                }
            }

            Search(compound);

            return null;
        }

        object resultObject = CreateInstance(oType);

        if (id != null)
            ctx.idToObject[id.IntValue] = resultObject;
        resultObject = DeserializeInto(compound, resultObject, ctx);

        return resultObject;
    }

    public static object DeserializeInto(SerializedProperty tag, object into) => DeserializeInto(tag, into, new SerializationContext());
    public static object DeserializeInto(SerializedProperty tag, object into, SerializationContext ctx)
    {
        if (into is ISerializable serializable)
        {
            serializable.Deserialize(tag, ctx);
            into = serializable;
        }
        else
        {
            foreach (var field in into.GetSerializableFields())
            {
                string name = field.Name;

                if (!tag.TryGet(name, out var node))
                {
                    // Before we completely give up, a field can have FormerlySerializedAs Attributes
                    // This allows backwards compatibility
                    var formerNames = Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute));
                    foreach (FormerlySerializedAsAttribute formerName in formerNames.Cast<FormerlySerializedAsAttribute>())
                    {
                        if (tag.TryGet(formerName.oldName, out node))
                        {
                            name = formerName.oldName;
                            break;
                        }
                    }
                }

                if (node == null) // Continue onto the next field
                    continue;

                object? data = null;

                try
                {
                    data = Deserialize(node, field.FieldType, ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogException(new Exception("Failed to deserialize field", ex));
                }

                // Some manual casting for edge cases
                if (data is byte @byte)
                {
                    if (field.FieldType == typeof(bool))
                        data = @byte != 0;
                    if (field.FieldType == typeof(sbyte))
                        data = (sbyte)@byte;
                }

                field.SetValue(into, data);
            }
        }

        if (into is ISerializationCallbackReceiver callback2)
            callback2.OnAfterDeserialize();
        return into;
    }

    static object CreateInstance(Type type)
    {
        return Activator.CreateInstance(type, true) ?? throw new InvalidOperationException("Failed to create instance of type: " + type);
    }

    #endregion
}
