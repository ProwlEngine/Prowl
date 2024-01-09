using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime
{
    public static class TagSerializer
    {
        public class SerializationContext
        {
            public Dictionary<object, int> objectToId = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            public Dictionary<int, object> idToObject = new Dictionary<int, object>();
            public int nextId = 1;
            public List<Guid> dependencies = new List<Guid>();

            public SerializationContext()
            {
                objectToId.Clear();
                objectToId.Add(new NullKey(), 0);
                idToObject.Clear();
                idToObject.Add(0, new NullKey());
                nextId = 1;
                dependencies.Clear();
            }

        }

        private class NullKey { }

        private static bool IsPrimitive(Type t) => t.IsPrimitive || t.IsAssignableTo(typeof(string)) || t.IsAssignableTo(typeof(Guid)) || t.IsAssignableTo(typeof(DateTime)) || t.IsEnum || t.IsAssignableTo(typeof(byte[]));


        #region Serialize

        public static Tag Serialize(object? value) => Serialize(value, new());
        public static Tag Serialize(object? value, SerializationContext ctx)
        {
            if (value == null)
                return new NullTag();

            if (value is Tag t)
            {
                var clone = t.Clone();
                return clone;
            }

            var type = value.GetType();
            if (IsPrimitive(type))
                return PrimitiveToTag(value);

            if (type.IsArray && value is Array array)
                return ArrayToListTag(array, ctx);

            var tag = DictionaryToTag(value, ctx);
            if (tag != null) return tag;

            if(value is IList iList)
                return IListToTag(iList, ctx);

            return SerializeObject(value, ctx);
        }

        private static Tag PrimitiveToTag(object p)
        {
            if (p is byte b) return new ByteTag(b);
            else if (p is sbyte sb) return new sByteTag(sb);
            else if (p is short s) return new ShortTag(s);
            else if (p is int i) return new IntTag(i);
            else if (p is long l) return new LongTag(l);
            else if (p is uint ui) return new UIntTag(ui);
            else if (p is ulong ul) return new ULongTag(ul);
            else if (p is ushort us) return new UShortTag(us);
            else if (p is float f) return new FloatTag(f);
            else if (p is double d) return new DoubleTag(d);
            else if (p is decimal dec) return new DecimalTag(dec);
            else if (p is string str) return new StringTag(str);
            else if (p is byte[] bArr) return new ByteArrayTag(bArr);
            else if (p is bool bo) return new BoolTag(bo);
            else if (p is DateTime date) return new LongTag(date.ToBinary());
            else if (p is Guid g) return new StringTag(g.ToString());
            else if (p.GetType().IsEnum) return new IntTag((int)p); // Serialize enums as integers
            else throw new NotSupportedException("The type '" + p.GetType() + "' is not a supported primitive.");
        }

        private static Type TagTypeToType(TagType tagType)
        {
            if (tagType == TagType.Byte) return typeof(byte);
            else if (tagType == TagType.sByte) return typeof(sbyte);
            else if (tagType == TagType.Int) return typeof(int);
            else if (tagType == TagType.Long) return typeof(long);
            else if (tagType == TagType.Short) return typeof(short);
            else if (tagType == TagType.UInt) return typeof(uint);
            else if (tagType == TagType.ULong) return typeof(ulong);
            else if (tagType == TagType.UShort) return typeof(ushort);
            else if (tagType == TagType.Float) return typeof(float);
            else if (tagType == TagType.Double) return typeof(double);
            else if (tagType == TagType.Decimal) return typeof(decimal);
            else if (tagType == TagType.String) return typeof(string);
            else if (tagType == TagType.ByteArray) return typeof(byte[]);
            else if (tagType == TagType.Bool) return typeof(bool);
            return typeof(object);
        }

        private static ListTag ArrayToListTag(Array array, SerializationContext ctx)
        {
            List<Tag> tags = [];
            for (int i = 0; i < array.Length; i++)
                tags.Add(Serialize(array.GetValue(i), ctx));
            return new ListTag(tags);
        }

        private static CompoundTag? DictionaryToTag(object obj, SerializationContext ctx)
        {
            var t = obj.GetType();
            if (obj is IDictionary dict &&
                 t.IsGenericType &&
                 t.GetGenericArguments()[0] == typeof(string))
            {
                CompoundTag tag = new();
                foreach (DictionaryEntry kvp in dict)
                    tag.Add((string)kvp.Key, Serialize(kvp.Value, ctx));
                return tag;
            }
            return null;
        }

        private static ListTag IListToTag(IList iList, SerializationContext ctx)
        {
            List<Tag> tags = [];
            foreach (var item in iList)
                tags.Add(Serialize(item, ctx));
            return new ListTag(tags);
        }

        private static Tag SerializeObject(object? value, SerializationContext ctx)
        {
            if (value == null) return new NullTag(); // ID defaults to 0 which is null or an Empty Compound

            var type = value.GetType();

            var compound = new CompoundTag();

            if (ctx.objectToId.TryGetValue(value, out int id))
            {
                compound.SerializedID = id;
                // Dont need to write compound data, its already been serialized at some point earlier
                return compound;
            }

            id = ctx.nextId++;
            ctx.objectToId[value] = id;
            ctx.idToObject[id] = value;

            if (value is ISerializeCallbacks callback)
                callback.PreSerialize();

            if (value is ISerializable serializable)
            {
                // Manual Serialization
                compound = serializable.Serialize(ctx);
            }
            else
            {
                // Automatic Serializer
                var properties = GetAllFields(type).Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null);
                // If public and has System.NonSerializedAttribute then ignore
                properties = properties.Where(field => !field.IsPublic || field.GetCustomAttribute<NonSerializedAttribute>() == null);

                foreach (var field in properties)
                {
                    string name = field.Name;

                    var propValue = field.GetValue(value);
                    if (propValue == null)
                    {
                        if (Attribute.GetCustomAttribute(field, typeof(IgnoreOnNullAttribute)) != null) continue;
                        compound.Add(name, new NullTag());
                    }
                    else
                    {
                        Tag tag = Serialize(propValue, ctx);
                        compound.Add(name, tag);
                    }
                }
            }

            compound.SerializedID = id;
            compound.SerializedType = type.AssemblyQualifiedName;

            if (value is ISerializeCallbacks callback2)
                callback2.PostSerialize();

            return compound;
        }

        #endregion

        #region Deserialize

        // TODO: Dont hard ever Crash on deserialization, instead return default and log an error
        // TODO: make a CopyInto method to copy a tag into an instance of an object use that for deserialization, Then we can use that for things like Copy/Paste

        public static T? Deserialize<T>(Tag value) => (T?)Deserialize(value, typeof(T));

        public static object? Deserialize(Tag value, Type type) => Deserialize(value, type, new SerializationContext());

        public static T? Deserialize<T>(Tag value, SerializationContext ctx) => (T?)Deserialize(value, typeof(T), ctx);
        public static object? Deserialize(Tag value, Type targetType, SerializationContext ctx)
        {
            if (value is NullTag) return null;

            if (value.GetType().IsAssignableTo(targetType)) return value;

            if (IsPrimitive(targetType))
            {
                if (value is ByteTag b) return b.Value;
                if (value is sByteTag sb) return sb.Value;
                else if (value is ShortTag sh) return sh.Value;
                else if (value is IntTag i)
                {
                    if (targetType.IsEnum)
                        return Enum.ToObject(targetType, i.Value);
                    return i.Value;
                }
                else if (value is LongTag lo)
                {
                    if (targetType == typeof(DateTime))
                        return DateTime.FromBinary(lo.Value);
                    return lo.Value;
                }
                else if (value is UShortTag ush) return ush.Value;
                else if (value is UIntTag ui) return ui.Value;
                else if (value is ULongTag ul) return ul.Value;
                else if (value is FloatTag flo) return flo.Value;
                else if (value is DoubleTag dou) return dou.Value;
                else if (value is DecimalTag dec) return dec.Value;
                else if (value is StringTag str)
                {
                    if (targetType == typeof(Guid))
                        return Guid.Parse(str.Value);
                    return str.Value;
                }
                else if (value is ByteArrayTag barr) return barr.Value;
                else if (value is BoolTag bo) return bo.Value;
                else throw new NotSupportedException("The Tag type '" + value.GetType() + "' is not supported.");
            }

            if (value is ListTag list)
            {
                Type type;
                if (targetType.IsArray)
                {
                    // Deserialize List into Array
                    type = targetType.GetElementType();
                    var array = Array.CreateInstance(type, list.Count);
                    for (int idx = 0; idx < array.Length; idx++)
                        array.SetValue(Deserialize(list[idx], type, ctx), idx);
                    return array;
                }
                else if (targetType.IsAssignableTo(typeof(IList)))
                {
                    // IEnumerable covers many types, we need to find the type of element in the IEnumrable
                    // For now just assume its the first generic argument
                    type = targetType.GetGenericArguments()[0];
                    var list2 = (IList)Activator.CreateInstance(targetType);
                    foreach (var tag in list.Tags)
                        list2.Add(Deserialize(tag, type, ctx));
                    return list2;

                }

                throw new InvalidCastException("ListTag cannot deserialize into type of: '" + targetType + "'");
            }

            if (value is CompoundTag compound)
            {
                if (targetType.IsAssignableTo(typeof(IDictionary)) &&
                                          targetType.IsGenericType &&
                                          targetType.GetGenericArguments()[0] == typeof(string))
                {
                    var dict = (IDictionary)Activator.CreateInstance(targetType);
                    var valueType = targetType.GetGenericArguments()[1];
                    foreach (var tag in compound.Tags)
                        dict.Add(tag.Key, Deserialize(tag.Value, valueType, ctx));
                    return dict;
                }

                return DeserializeObject(compound, ctx);
            }

            throw new NotSupportedException("The node type '" + value.GetType() + "' is not supported.");
        }

        private static object? DeserializeObject(CompoundTag compound, SerializationContext ctx)
        {
            if (ctx.idToObject.TryGetValue(compound.SerializedID, out object? existingObj))
                return existingObj;

            if (string.IsNullOrWhiteSpace(compound.SerializedType))
                return null;

            Type oType = Type.GetType(compound.SerializedType);
            if (oType == null)
            {
                Debug.LogError("[TagSerializer] Couldn't find type: " + compound.SerializedType);
                return null;
            }

            object resultObject = CreateInstance(oType);

            ctx.idToObject[compound.SerializedID] = resultObject;

            if (resultObject is ISerializeCallbacks callback1)
                callback1.PreDeserialize();

            if (resultObject is ISerializable serializable)
            {
                serializable.Deserialize(compound, ctx);
                resultObject = serializable;
            }
            else
            {
                FieldInfo[] fields = GetAllFields(oType).ToArray();

                var properties = fields.Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null);
                // If public and has System.NonSerializedAttribute then ignore
                properties = properties.Where(field => !field.IsPublic || field.GetCustomAttribute<NonSerializedAttribute>() == null);
                foreach (var field in properties)
                {
                    string name = field.Name;

                    if (!compound.TryGet<Tag>(name, out var node))
                    {
                        // Before we completely give up, a field can have FormerlySerializedAs Attributes
                        // This allows backwards compatibility
                        var formerNames = Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute));
                        foreach (FormerlySerializedAsAttribute formerName in formerNames)
                        {
                            if (compound.TryGet<Tag>(formerName.oldName, out node))
                            {
                                name = formerName.oldName;
                                break;
                            }
                        }
                        if (node == null) // Continue onto the next field
                            continue;
                    }

                    object data = Deserialize(node, field.FieldType, ctx);

                    // Some manual casting for edge cases
                    if (data is byte @byte)
                    {
                        if (field.FieldType == typeof(bool))
                            data = @byte != 0;
                        if (field.FieldType == typeof(sbyte))
                            data = (sbyte)@byte;
                    }

                    field.SetValue(resultObject, data);
                }
            }

            if (resultObject is ISerializeCallbacks callback2)
                callback2.PostDeserialize();

            return resultObject;
        }


        static object CreateInstance(Type type)
        {
            object data = Activator.CreateInstance(type);
            return data;
        }

        static IEnumerable<FieldInfo> GetAllFields(Type t)
        {
            if (t == null)
                return Enumerable.Empty<FieldInfo>();

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.DeclaredOnly;

            return t.GetFields(flags).Concat(GetAllFields(t.BaseType));
        }

        #endregion
    }
}
