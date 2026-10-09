using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class RecordFormat : ISerializationFormat
{
    private static readonly ConcurrentDictionary<Type, Layout> Layouts = new();

    private static readonly HashSet<Type> ValueTypes =
    [typeof(PassStats), typeof(GpuVertexStats), typeof(MemoryBudgetInfo), typeof(ResourceRange), typeof(CopyRegion), typeof(TraceResourceId), typeof(TraceVersion), typeof(BlobRef)];

    public bool CanHandle(Type type)
        => ValueTypes.Contains(type) || (type.IsClass && !type.IsAbstract && type.Assembly == typeof(RecordFormat).Assembly && type.GetMethod("<Clone>$") != null);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        Layout layout = LayoutOf(value.GetType());
        EchoObject compound = EchoObject.NewCompound();
        for (int i = 0; i < layout.Parameters.Length; i++)
        {
            object? member = layout.Properties[i].GetValue(value);
            compound.Add(layout.Properties[i].Name, member == null ? new EchoObject(EchoType.Null, null) : Serializer.Serialize(layout.Parameters[i].ParameterType, member, context));
        }

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Layout layout = LayoutOf(targetType);
        object?[] arguments = new object?[layout.Parameters.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            Type type = layout.Parameters[i].ParameterType;
            object? member = value.TryGet(layout.Properties[i].Name, out EchoObject? field) ? Serializer.Deserialize(field, type, context) : null;
            arguments[i] = member ?? (type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null);
        }

        return layout.Constructor.Invoke(arguments);
    }

    private static Layout LayoutOf(Type type) => Layouts.GetOrAdd(type, t =>
    {
        ConstructorInfo constructor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        ParameterInfo[] parameters = constructor.GetParameters();
        PropertyInfo[] properties = parameters.Select(p => t.GetProperty(p.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!).ToArray();
        return new Layout(constructor, parameters, properties);
    });

    private sealed record Layout(ConstructorInfo Constructor, ParameterInfo[] Parameters, PropertyInfo[] Properties);
}
