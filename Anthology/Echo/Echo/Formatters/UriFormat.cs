// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class UriFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(Uri);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is Uri uri)
        {
            var compound = EchoObject.NewCompound();

            // Serialize URI as string with its kind
            compound["uri"] = new EchoObject(uri.OriginalString);
            compound["kind"] = new EchoObject(EchoType.Int, uri.IsAbsoluteUri ? 1 : 2); // 1 = Absolute, 2 = Relative

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by UriFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("uri", out var uriTag) && uriTag.TagType == EchoType.String &&
                value.TryGet("kind", out var kindTag) && kindTag.TagType == EchoType.Int)
            {
                string uriString = uriTag.StringValue;
                int kind = kindTag.IntValue;

                UriKind uriKind = kind == 1 ? UriKind.Absolute : UriKind.Relative;
                return new Uri(uriString, uriKind);
            }
            else
            {
                throw new InvalidOperationException("Invalid Uri format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by UriFormat.");
    }
}
