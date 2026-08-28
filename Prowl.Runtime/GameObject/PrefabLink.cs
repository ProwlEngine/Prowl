// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// What ties a GameObject to the prefab it came from. Held by reference and only allocated for
/// objects that actually are prefab instances, so an ordinary GameObject carries one null field
/// rather than a copy of every prefab-related value.
/// <para/>
/// Internal, and its fields stay public inside that: these values only mean anything as a set, and
/// nothing here can check one against another. Changed through <c>PrefabUtility</c> and the instantiate
/// path, read through the properties on <see cref="GameObject"/>.
/// <para/>
/// Only what belongs to the object as a whole. Which component of the prefab a component came from is on
/// <see cref="MonoBehaviour.SourceIdentifier"/>.
/// </summary>
internal sealed class PrefabLink
{
    /// <summary>The prefab asset this object is an instance of.</summary>
    public Guid AssetId;

    /// <summary>
    /// The identifier of the object in the prefab that this one was built from. Identifiers are
    /// handed out fresh on every load, so this is what survives to say which source object this is.
    /// </summary>
    public Guid SourceIdentifier;

    /// <summary>Per-instance changes, stored on the instance root with root-relative paths.</summary>
    public List<PropertyOverride> Overrides = new();

    /// <summary>
    /// Drop what makes this an instance, keeping what says where the objects came from. Writing a
    /// prefab strips the instance data but must keep the source identities, or the asset would be
    /// written with brand new ones and every override in every scene would stop resolving.
    /// </summary>
    public void ClearInstanceData()
    {
        AssetId = Guid.Empty;
        Overrides.Clear();
    }

    public PrefabLink Clone() => new()
    {
        AssetId = AssetId,
        SourceIdentifier = SourceIdentifier,
        Overrides = new List<PropertyOverride>(Overrides)
    };

    /// <summary>
    /// Take on another link's state without becoming it, so whatever holds this one keeps holding it.
    /// </summary>
    public void CopyFrom(PrefabLink other)
    {
        AssetId = other.AssetId;
        SourceIdentifier = other.SourceIdentifier;
        Overrides = new List<PropertyOverride>(other.Overrides);
    }
}
