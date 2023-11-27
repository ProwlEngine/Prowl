using Prowl.Runtime.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;

namespace Prowl.Runtime.Utils
{
    public class ProwlContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type,
                                    MemberSerialization memberSerialization)
        {
            List<MemberInfo> members = GetSerializableMembers(type);
            if (members == null)
                throw new JsonSerializationException("Null collection of serializable members returned.");
        
            // Remove properties
            //members = members.Where(m => m.MemberType != MemberTypes.Property).ToList();
        
            members.AddRange(type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => !f.CustomAttributes.Any(x => x.AttributeType
                    == typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))));

            // Remove duplicates
            members = members.Distinct().ToList();
        
            JsonPropertyCollection properties = new JsonPropertyCollection(type);
            members.ForEach(member =>
            {
                JsonProperty property = CreateProperty(member, memberSerialization);
                if (property != null)
                {
                    property.Writable = true;
                    property.Readable = true;
                    properties.AddProperty(property);
                }
            });
            return properties;
        }
        
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
        
            bool doSerialize = true;

            // AssetRef's Instance will only be serialized if assetID is Guid.Empty
            if (member.DeclaringType.IsAssignableTo(typeof(IAssetRef)) && member.Name == "instance")
            {
                property.ShouldSerialize = obj =>
                {
                    var assetRef = (IAssetRef)obj;
                    return assetRef.AssetID == Guid.Empty;
                };
                return property;
            }

            // Is Field - Should always be a Field
            if (member is FieldInfo field)
            {
                // Is public, or has a SerializeField attribute
                doSerialize &= field.IsPublic || member.GetCustomAttributes(typeof(SerializeFieldAttribute), true).Length > 0;
        
                // isn’t static
                doSerialize &= !field.IsStatic;
        
                // isn’t const
                doSerialize &= !field.IsLiteral;
        
                // isn’t readonly
                doSerialize &= !field.IsInitOnly;
            }
            else
            {
                return null;
            }
        
            property.ShouldSerialize = instance => { return doSerialize; };
            return property;
        }

        //protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        //{
        //    var properties = base.CreateProperties(type, memberSerialization);
        //
        //    // Set private fields to be serialized
        //    foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        //    {
        //        var property = properties.FirstOrDefault(p => p.UnderlyingName == field.Name);
        //        if (property != null)
        //        {
        //            property.Writable = true;
        //            property.Readable = true;
        //        }
        //    }
        //
        //    return properties;
        //}
        //
        //protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        //{
        //    if (member is PropertyInfo)
        //    {
        //        // Ignore properties and only serialize fields
        //        return null;
        //    }
        //
        //    var property = base.CreateProperty(member, memberSerialization);
        //
        //    property.ShouldSerialize = instance => true;
        //
        //    if (member is FieldInfo fieldInfo && !fieldInfo.IsPublic)
        //    {
        //        var serializeFieldAttribute = fieldInfo.GetCustomAttribute<SerializeFieldAttribute>();
        //        if (serializeFieldAttribute == null)
        //            property.ShouldSerialize = instance => false;
        //    }
        //
        //    return property;
        //}
    }

    public static class JsonUtility
    {
        static JsonSerializerSettings settings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new ProwlContractResolver(),
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        };


        public static string Serialize(object obj) => JsonConvert.SerializeObject(obj, settings);
        public static string Serialize(object obj, Type type) => JsonConvert.SerializeObject(obj, type, settings);

        public static void SerializeTo(string path, object obj)
        {
            using TextWriter textWriter = File.CreateText(path);
            JsonSerializer.Create(settings).Serialize(textWriter, obj);
        }

        public static T? Deserialize<T>(string json)
        {
            //using (var streamReader = new StreamReader(json))
            //{
            //    using (var reader = new JsonTextReader(streamReader))
            //    {
            //        return JsonSerializer.Create(settings).Deserialize<T>(reader);
            //    }
            //}
            return JsonConvert.DeserializeObject<T>(json, settings);
        }

        public static T? Deserialize<T>(StreamReader s)
        {
            using var reader = new JsonTextReader(s);
            return JsonSerializer.Create(settings).Deserialize<T>(reader);
        }

        public static T? DeserializeFromPath<T>(string path)
        {
            using var streamReader = new StreamReader(path);
            using var reader = new JsonTextReader(streamReader);
            return JsonSerializer.Create(settings).Deserialize<T>(reader);
        }

        public static object? Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type, settings);
    }
}
