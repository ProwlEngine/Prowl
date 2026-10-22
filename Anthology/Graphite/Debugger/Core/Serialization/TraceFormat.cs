using System;
using Prowl.Echo;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class TraceFormat : ISerializationFormat
{
    public bool CanHandle(Type type)
        => type == typeof(ProgramKey) || type == typeof(PropertyID) || type == typeof(VertexAttributeID) || type == typeof(GraphKind);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        switch (value)
        {
            case ProgramKey key:
                return new EchoObject(key.ToArray());
            case PropertyID property:
                return new EchoObject(PropertyID.ToString(property) ?? "");
            case VertexAttributeID attribute:
                return new EchoObject(VertexAttributeID.ToString(attribute) ?? "");
            case GraphKind kind:
                return new EchoObject(kind.Name);
            default:
                throw new NotSupportedException(value.GetType().ToString());
        }
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (targetType == typeof(ProgramKey))
            return ProgramKey.FromBytes(value.ByteArrayValue);

        if (targetType == typeof(PropertyID))
            return PropertyID.Intern(value.StringValue);

        if (targetType == typeof(GraphKind))
            return string.IsNullOrEmpty(value.StringValue) ? default(GraphKind) : GraphKind.Intern(value.StringValue);

        return VertexAttributeID.Intern(value.StringValue);
    }
}
