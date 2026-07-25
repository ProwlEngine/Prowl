// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

/// <summary>
/// Lets a caller mark object references that live outside the graph being serialized so Echo links
/// to them by a stable key instead of inlining (and, on deserialize, deep-copying) them.
/// The classic case is copy/paste of a scene object: references to other objects in the selection
/// should be duplicated, but references pointing out of the selection should resolve back to the
/// original live instances rather than spawning copies.
/// Set one on <see cref="SerializationContext.ExternalReferences"/>.
/// </summary>
public interface IExternalReferenceResolver
{
    /// <summary>
    /// Called during serialization for every reference-type value. Return a stable key when
    /// <paramref name="value"/> is external and should be linked rather than serialized; return null
    /// to serialize it normally. Keys should be simple, self-contained values (a Guid, string, int,
    /// or small struct) so they round-trip on their own.
    /// </summary>
    object? GetReferenceKey(object value);

    /// <summary>
    /// Called during deserialization to turn a key produced by <see cref="GetReferenceKey"/> back into
    /// the live instance. <paramref name="targetType"/> is the reference's declared/serialized type.
    /// Return null if the reference can no longer be resolved.
    /// </summary>
    object? ResolveReference(object key, Type targetType);
}
