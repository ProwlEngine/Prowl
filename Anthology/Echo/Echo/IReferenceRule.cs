// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

/// <summary>
/// A global rule that writes some objects as a key instead of their content, for objects that live in
/// a store of their own (an asset database). The value passed to the outermost Serialize call is always
/// written in full, so an object can still be saved as itself. Set one on <see cref="Serializer.ReferenceRule"/>.
/// </summary>
public interface IReferenceRule
{
    /// <summary>The reserved compound key stubs are written under, for example "$asset".</summary>
    string Key { get; }

    /// <summary>Asked for every reference type value except the root of the write. Return true to write a stub.</summary>
    bool TryGetReference(object value, SerializationContext context, out string reference);

    /// <summary>Turns a stub back into an object. declaredType is the stub's $type when present, else the field type.</summary>
    object? Resolve(string reference, Type declaredType, SerializationContext context);
}
