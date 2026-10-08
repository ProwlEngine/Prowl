using System;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class TraceFormat : ISerializationFormat
{
    public bool CanHandle(Type type)
        => type == typeof(TraceResourceId) || type == typeof(TraceVersion) || type == typeof(BlobRef) || type == typeof(ProgramKey)
            || type == typeof(PropertyID) || type == typeof(VertexAttributeID);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        switch (value)
        {
            case TraceResourceId id:
                return new EchoObject(id.Value);
            case ProgramKey key:
                return new EchoObject(key.ToArray());
            case PropertyID property:
                return new EchoObject(PropertyID.ToString(property) ?? "");
            case VertexAttributeID attribute:
                return new EchoObject(VertexAttributeID.ToString(attribute) ?? "");
            case TraceVersion version:
            {
                EchoObject list = EchoObject.NewList();
                list.ListAdd(new EchoObject(version.Resource.Value));
                list.ListAdd(new EchoObject(version.Version));
                return list;
            }
            case BlobRef blob:
            {
                EchoObject list = EchoObject.NewList();
                list.ListAdd(new EchoObject(blob.Hash.Items.ToArray()));
                list.ListAdd(new EchoObject(blob.Length));
                return list;
            }
            default:
                throw new NotSupportedException(value.GetType().ToString());
        }
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (targetType == typeof(TraceResourceId))
            return new TraceResourceId(value.UIntValue);

        if (targetType == typeof(ProgramKey))
            return ProgramKey.FromBytes(value.ByteArrayValue);

        if (targetType == typeof(PropertyID))
            return PropertyID.Intern(value.StringValue);

        if (targetType == typeof(VertexAttributeID))
            return VertexAttributeID.Intern(value.StringValue);

        if (targetType == typeof(TraceVersion))
            return new TraceVersion(new TraceResourceId(value.List[0].UIntValue), value.List[1].UIntValue);

        return new BlobRef(EquatableArray.Create<byte>(value.List[0].ByteArrayValue), value.List[1].ULongValue);
    }
}
