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
    /// Which member of which object this overrides, relative to the instance root.
    /// <para/>
    /// Format: <c>{objectSourceId}/{componentSourceId}/{memberPath}</c>, or
    /// <c>{objectSourceId}/$/{memberPath}</c> for a member of the GameObject itself. The source
    /// identifiers say which prefab object each part addresses, so the path survives objects being
    /// added, removed or reordered on either side.
    /// </summary>
    [SerializeField]
    public string Path = "";

    /// <summary>
    /// The overridden value, serialized as an EchoObject.
    /// </summary>
    [SerializeField]
    public EchoObject Value = EchoObject.NewCompound();
}
