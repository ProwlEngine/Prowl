// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class VersionFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(Version);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is Version version)
        {
            var compound = EchoObject.NewCompound();

            // Serialize version components
            compound["major"] = new EchoObject(EchoType.Int, version.Major);
            compound["minor"] = new EchoObject(EchoType.Int, version.Minor);

            // Build and Revision can be -1 if not specified
            if (version.Build >= 0)
                compound["build"] = new EchoObject(EchoType.Int, version.Build);

            if (version.Revision >= 0)
                compound["revision"] = new EchoObject(EchoType.Int, version.Revision);

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by VersionFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("major", out var majorTag) && majorTag.TagType == EchoType.Int &&
                value.TryGet("minor", out var minorTag) && minorTag.TagType == EchoType.Int)
            {
                int major = majorTag.IntValue;
                int minor = minorTag.IntValue;

                // Check for build and revision
                bool hasBuild = value.TryGet("build", out var buildTag) && buildTag.TagType == EchoType.Int;
                bool hasRevision = value.TryGet("revision", out var revisionTag) && revisionTag.TagType == EchoType.Int;

                if (hasRevision && hasBuild)
                {
                    return new Version(major, minor, buildTag.IntValue, revisionTag.IntValue);
                }
                else if (hasBuild)
                {
                    return new Version(major, minor, buildTag.IntValue);
                }
                else
                {
                    return new Version(major, minor);
                }
            }
            else
            {
                throw new InvalidOperationException("Invalid Version format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by VersionFormat.");
    }
}
