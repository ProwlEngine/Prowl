// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.MeshFeatures;

/// <summary>
/// Registration contract for a mesh feature. Describes how to generate one and how
/// its settings slot into the mesh/model importer settings blob.
/// </summary>
/// <remarks>
/// A concrete <see cref="MeshFeatureSpec"/> subclass is discovered via reflection by
/// <see cref="MeshFeatureRegistry"/> and instantiated once. Bumping <see cref="Version"/>
/// causes every mesh importer to reimport (cache invalidation rides on the aggregate
/// version exposed by <see cref="MeshFeatureRegistry.AggregateVersion"/>).
/// </remarks>
public abstract class MeshFeatureSpec
{
    /// <summary>Short stable key (e.g. "sdf"). Used for sub-asset naming and settings keys.</summary>
    public abstract string Key { get; }

    /// <summary>Human-readable name shown in inspector UI (e.g. "Signed Distance Field").</summary>
    public abstract string DisplayName { get; }

    /// <summary>The runtime type produced by this feature.</summary>
    public abstract Type FeatureType { get; }

    /// <summary>Bump when generation logic changes — forces reimport of all meshes.</summary>
    public abstract int Version { get; }

    /// <summary>
    /// Populate defaults under the given importer-settings compound. Implementations
    /// should write a nested compound at <c>settings[Key]</c> with their knobs
    /// (including an <c>enabled</c> flag, default false).
    /// </summary>
    public abstract void PopulateDefaults(EchoObject settings);

    /// <summary>
    /// Read settings, decide whether to generate, and return the feature or null.
    /// Return null when disabled or when generation is skipped (no error).
    /// </summary>
    public abstract EngineObject? TryGenerate(Mesh mesh, EchoObject? settings);
}
