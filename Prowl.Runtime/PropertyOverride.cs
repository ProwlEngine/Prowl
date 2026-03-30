// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Stores a single per-instance property override for a prefab instance.
/// </summary>
[Serializable]
public class PropertyOverride
{
    /// <summary>
    /// Path to the overridden field.
    /// Format: "{componentIdentifier}.{fieldPath}" for component fields,
    /// or "$.{fieldName}" for GameObject-level fields.
    /// </summary>
    [SerializeField]
    public string Path = "";

    /// <summary>
    /// The overridden value, serialized as an EchoObject.
    /// </summary>
    [SerializeField]
    public EchoObject Value = EchoObject.NewCompound();
}
