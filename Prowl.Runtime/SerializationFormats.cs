// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;

namespace Prowl.Runtime;

/// <summary>
/// The set of Echo serialization formats Prowl registers by default. These cover the Graphite
/// interned-ID types (<see cref="PropertyID"/>, <see cref="VertexAttributeID"/>, <see cref="Keyword"/>)
/// which would otherwise serialize their process-local integer instead of a stable string, so they are
/// required to round-trip any serialized shader, not just the GUI shaders. Registration runs from a
/// module initializer so the formats are available before any deserialization happens.
/// </summary>
public static class SerializationFormats
{
    private static readonly object s_lock = new();
    private static bool s_registered;

    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute is only intended to be used in application code or advanced source generator scenarios",
        Justification = "Registers built-in serialization formats before any deserialization occurs.")]
    [ModuleInitializer]
    public static void RegisterDefaults()
    {
        lock (s_lock)
        {
            if (s_registered)
                return;
            s_registered = true;

            Serializer.RegisterFormat(new PropertyIDFormat());
            Serializer.RegisterFormat(new VertexAttributeIDFormat());
            Serializer.RegisterFormat(new KeywordFormat());
            Serializer.RegisterFormat(new VariantSpaceFormat());
        }
    }

    private sealed class PropertyIDFormat : ISerializationFormat
    {
        public bool CanHandle(Type type) => type == typeof(PropertyID);

        public EchoObject Serialize(Type targetType, object value, SerializationContext context)
            => new(PropertyID.ToString((PropertyID)value) ?? "");

        public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
        {
            string name = value.StringValue;
            return string.IsNullOrEmpty(name) ? default : (PropertyID)name;
        }
    }

    private sealed class VertexAttributeIDFormat : ISerializationFormat
    {
        public bool CanHandle(Type type) => type == typeof(VertexAttributeID);

        public EchoObject Serialize(Type targetType, object value, SerializationContext context)
            => new(VertexAttributeID.ToString((VertexAttributeID)value) ?? "");

        public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
        {
            string name = value.StringValue;
            return string.IsNullOrEmpty(name) ? default : (VertexAttributeID)name;
        }
    }

    private sealed class KeywordFormat : ISerializationFormat
    {
        public bool CanHandle(Type type) => type == typeof(Keyword);

        public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        {
            var keyword = (Keyword)value;
            EchoObject compound = EchoObject.NewCompound();
            compound.Add("Name", new EchoObject(keyword.Name ?? ""));
            compound.Add("Value", new EchoObject(keyword.Value ?? ""));
            return compound;
        }

        public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
            => new Keyword(value["Name"].StringValue, value["Value"].StringValue);
    }

    /// <summary>
    /// <see cref="VariantSpace"/> exposes its data as get-only auto-properties (no public fields), which
    /// Echo's default reflection format can't see - it only serializes fields. Without this it silently
    /// round-trips as an empty struct, and <c>VariantCombos.Generate</c> then NREs on the null
    /// <see cref="VariantSpace.Values"/> of the "restored" instance.
    /// </summary>
    private sealed class VariantSpaceFormat : ISerializationFormat
    {
        public bool CanHandle(Type type) => type == typeof(VariantSpace);

        public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        {
            var space = (VariantSpace)value;
            EchoObject compound = EchoObject.NewCompound();
            compound.Add("Name", new EchoObject(space.Name ?? ""));
            compound.Add("DeclType", new EchoObject(space.DeclType ?? ""));

            EchoObject values = EchoObject.NewList();
            foreach (string v in space.Values ?? [])
                values.ListAdd(new EchoObject(v));
            compound.Add("Values", values);

            compound.Add("IsEnum", new EchoObject(space.IsEnum));
            compound.Add("TypeModule", new EchoObject(space.TypeModule ?? ""));
            return compound;
        }

        public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
        {
            List<string> values = [.. value["Values"].List.Select(v => v.StringValue)];
            string? typeModule = value["TypeModule"].StringValue;

            return new VariantSpace(
                value["Name"].StringValue,
                value["DeclType"].StringValue,
                values,
                value["IsEnum"].BoolValue,
                string.IsNullOrEmpty(typeModule) ? null : typeModule);
        }
    }
}
