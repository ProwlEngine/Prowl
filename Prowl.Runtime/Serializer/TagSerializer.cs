using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.Serialization
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


        public static Tag Serialize(object? value, string tagName = "") => Internal_Serialize(value, tagName, new());

        private static bool IsPrimitive(object value)
        {
            Type t = value.GetType();
            return t.IsPrimitive || value is string || value is Guid || t.IsEnum;
        }

        private static Tag PrimitiveToTag(string tagName, object p)
        {
            if (p is byte b)             return new ByteTag(tagName, b);
            else if (p is sbyte sb)      return new ByteTag(tagName, (byte)sb);
            else if (p is byte[] bArr)   return new ByteArrayTag(tagName, bArr);
            else if (p is bool bo)       return new ByteTag(tagName, (byte)(bo ? 1 : 0));
            else if (p is float f)       return new FloatTag(tagName, f);
            else if (p is double d)      return new DoubleTag(tagName, d);
            else if (p is int i)         return new IntTag(tagName, i);
            else if (p is uint ui)       return new IntTag(tagName, (int)ui);
            else if (p is long l)        return new LongTag(tagName, l);
            else if (p is ulong ul)      return new LongTag(tagName, (long)ul);
            else if (p is short s)       return new ShortTag(tagName, s);
            else if (p is ushort us)     return new ShortTag(tagName, (short)us);
            else if (p is string str)    return new StringTag(tagName, str);
            else if (p is Guid g)        return new StringTag(tagName, g.ToString());
            else if (p.GetType().IsEnum) return new IntTag(tagName, (int)p); // Serialize enums as integers
            else throw new NotSupportedException("The type '" + p.GetType() + "' is not a supported primitive.");
        }

        private static ListTag ArrayToListTag(string tagName, object p, SerializationContext ctx)
        {
            var elementType = p.GetType().GetElementType();
            var array = p as Array;
            var listType = TagType.Compound;
            if (elementType == typeof(byte) || elementType == typeof(sbyte))
                listType = TagType.Byte;
            else if (elementType == typeof(bool))
                listType = TagType.Byte;
            else if (elementType == typeof(double))
                listType = TagType.Double;
            else if (elementType == typeof(float))
                listType = TagType.Float;
            else if (elementType == typeof(int) || elementType == typeof(uint))
                listType = TagType.Int;
            else if (elementType == typeof(long) || elementType == typeof(ulong))
                listType = TagType.Long;
            else if (elementType == typeof(short) || elementType == typeof(ushort))
                listType = TagType.Short;
            else if (elementType == typeof(string) || elementType == typeof(Guid))
                listType = TagType.String;
            else if (elementType == typeof(byte[]))
                listType = TagType.ByteArray;
            List<Tag> tags = new();
            for (int i = 0; i < array.Length; i++)
                tags.Add(Internal_Serialize(array.GetValue(i), "", ctx));
            return new ListTag(tagName, tags, listType);
        }

        private static CompoundTag DictionaryToTag(string tagName, object p, SerializationContext ctx)
        {
            CompoundTag tag = new(tagName)
            {
                SerializedID = -1, // Mark as a dictionary
                SerializedType = p.GetType().FullName!
            };
            foreach (DictionaryEntry kvp in (IDictionary)p)
            {
                if (kvp.Key is string str)
                    tag.Add(Internal_Serialize(kvp.Value, str, ctx));
                else
                    throw new InvalidCastException("Dictionary keys must be strings!");
            }
            return tag;
        }

        private static Tag Internal_Serialize(object? value, string tagName, SerializationContext ctx)
        {
            if (value == null) return new NullTag(tagName);

            if (value is Tag t)
            {
                var clone = t.Clone();
                clone.Name = tagName;
                return clone;
            }

            if (IsPrimitive(value)) 
                return PrimitiveToTag(tagName, value);

            if (value.GetType().IsArray)
                return ArrayToListTag(tagName, value, ctx);

            if (value is IDictionary)
                return DictionaryToTag(tagName, value, ctx);

            return SerializeObject(value, tagName, ctx);
        }

        public static Tag SerializeObject(object? value, string tagName, SerializationContext ctx)
        {
            if (value == null) return new NullTag(tagName); // ID defaults to 0 which is null or an Empty Compound

            var type = value.GetType();

            var nameAttribute = Attribute.GetCustomAttribute(type, typeof(SerializeAsAttribute)) as SerializeAsAttribute;
            var compound = new CompoundTag(nameAttribute?.Name ?? tagName);

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
                compound = serializable.Serialize(tagName, ctx);
            }
            else
            {
                // Automatic Serializer
                var properties = GetAllFields(type).Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeAsAttribute>() != null) &&
                                                       field.GetCustomAttribute<SerializeIgnoreAttribute>() == null);

                foreach (var field in properties)
                {
                    var attribute = field.GetCustomAttribute<SerializeAsAttribute>();
                    string name = attribute != null ? attribute.Name : field.Name;

                    var propValue = field.GetValue(value);
                    if (propValue == null)
                    {
                        if (Attribute.GetCustomAttribute(field, typeof(IgnoreOnNullAttribute)) != null) continue;
                        compound.Add(new NullTag(name));
                    }
                    else
                    {
                        Tag tag = Internal_Serialize(propValue, name, ctx);
                        if (string.IsNullOrEmpty(tag.Name)) throw new NullReferenceException("Data Tag Name is missing! Cannot finish Serialization!");
                        compound.Add(tag);
                    }
                }
            }

            compound.SerializedID = id;
            compound.SerializedType = type.FullName;

            if (value is ISerializeCallbacks callback2)
                callback2.PostSerialize();

            return compound;
        }

        public static T Deserialize<T>(Tag value) => (T)Deserialize(typeof(T), value);

        public static object Deserialize(Type type, Tag value)
            => Internal_Deserialize(type, value, new SerializationContext());

        private static object Internal_Deserialize(Type targetType, Tag value, SerializationContext ctx)
        {
            if (targetType.IsAssignableTo(typeof(System.Collections.IDictionary)) && value is CompoundTag dictTag)
            {
                // tag is dictionary
                var dict = (System.Collections.IDictionary)Activator.CreateInstance(targetType);
                var valueType = targetType.GetGenericArguments()[1];
                foreach (var tag in dictTag.AllTags)
                {
                    var key = tag.Name;
                    var val = Internal_Deserialize(valueType, tag, ctx);
                    dict.Add(key, val);
                }
                return dict;
            }
            else if (targetType.IsEnum && value is IntTag intTag)
            {
                return Enum.ToObject(targetType, intTag.Value); // Deserialize integers as enums
            }
            else if (targetType == typeof(Guid) && value is StringTag strGuid)
            {
                return Guid.Parse(strGuid.Value);
            }
            else if (value is ByteTag b) return b.Value;
            else if (value is DoubleTag dou) return dou.Value;
            else if (value is FloatTag flo) return flo.Value;
            else if (value is IntTag i) return i.Value;
            else if (value is LongTag lo) return lo.Value;
            else if (value is ShortTag sh) return sh.Value;
            else if (value is StringTag str) return str.Value;
            else if (value is ByteArrayTag barr) return barr.Value;
            else if (value is ListTag list)
            {
                Type type;
                if (list.ListType == TagType.Byte) type = typeof(byte);
                else if (list.ListType == TagType.Compound)
                {
                    if (targetType.IsArray) type = targetType.GetElementType();
                    else type = typeof(object);
                }
                else if (list.ListType == TagType.Double) type = typeof(double);
                else if (list.ListType == TagType.Float) type = typeof(float);
                else if (list.ListType == TagType.Int) type = typeof(int);
                else if (list.ListType == TagType.Long) type = typeof(long);
                else if (list.ListType == TagType.Short) type = typeof(short);
                else if (list.ListType == TagType.String) type = typeof(string);
                else throw new NotSupportedException("The Tag List type '" + list.ListType + "' is not supported.");

                var array = Array.CreateInstance(type, list.Count);
                for (int idx = 0; idx < array.Length; idx++)
                    array.SetValue(Internal_Deserialize(type, list[idx], ctx), idx);
                return array;
            }
            else if (value is CompoundTag compound)
            {
                return DeserializeObject(compound, ctx);
            }

            throw new NotSupportedException("The node type '" + value.GetType() + "' is not supported.");
        }

        public static T DeserializeObject<T>(CompoundTag compound, SerializationContext ctx)
            => (T)DeserializeObject(compound, ctx);

        public static object DeserializeObject(CompoundTag compound, SerializationContext ctx)
        {
            if (compound.SerializedID == 0)
                return null;

            if (compound.SerializedID > 0 && ctx.idToObject.TryGetValue(compound.SerializedID, out object existingObj))
                return existingObj;

            if (string.IsNullOrWhiteSpace(compound.SerializedType))
                return null;

            Type oType = Type.GetType(compound.SerializedType);

            object resultObject = CreateInstance(oType);

            ctx.idToObject[compound.SerializedID] = resultObject;

            if (resultObject is ISerializable serializable)
            {
                serializable.Deserialize(compound, ctx);
                resultObject = serializable;
            }
            else
            {

                FieldInfo[] fields = GetAllFields(oType).ToArray();

                var properties = fields.Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeAsAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null);
                foreach (var field in properties)
                {
                    string name = field.Name;

                    var nameAttributes = Attribute.GetCustomAttributes(field, typeof(SerializeAsAttribute));
                    if (nameAttributes.Length != 0)
                        name = ((SerializeAsAttribute)nameAttributes[0]).Name;

                    if (!compound.TryGet<Tag>(name, out var node))
                    {
                        // Before we completely give up, a field can have FormerlySerializedAs Attributes
                        // This allows backwards compatibility
                        var formerNames = Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute));
                        foreach (SerializeAsAttribute formerName in formerNames)
                        {
                            //if (compound.Tags.Any(a => a.Name == formerName.Name))
                            if (compound.TryGet<Tag>(formerName.Name, out node))
                            {
                                name = formerName.Name;
                                break;
                            }
                        }
                        if (node == null) // Continue onto the next field
                            continue;
                    }

                    object data = null;

                    if (node is CompoundTag compoundNode)
                    {
                        if (ctx.idToObject.TryGetValue(compoundNode.SerializedID, out object existingObj2))
                            data = existingObj2;
                        if (typeof(ISerializable).IsAssignableFrom(field.FieldType))
                        {
                            data = CreateInstance(field.FieldType);
                            ctx.idToObject[compoundNode.SerializedID] = data;
                            ((ISerializable)data).Deserialize(compoundNode, ctx);
                        }
                    }

                    if(data == null) // If we didn't deserialize it as an ISerializable, deserialize it normally
                        data = Internal_Deserialize(field.FieldType, node, ctx);

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

            return resultObject;
        }


        static object CreateInstance(Type type)
        {
            object data = Activator.CreateInstance(type);

            if (data is ISerializeCallbacks callback2)
                callback2.PostCreation();

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
    }
}
