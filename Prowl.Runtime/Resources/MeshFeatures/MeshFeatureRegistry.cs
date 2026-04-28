// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Prowl.Echo;

namespace Prowl.Runtime.MeshFeatures;

/// <summary>
/// Discovers all <see cref="MeshFeatureSpec"/> subclasses via reflection. The
/// aggregate version across specs is part of every mesh-producing importer's
/// <c>Version</c>, so bumping any feature's version forces a global reimport.
/// </summary>
public static class MeshFeatureRegistry
{
    private static readonly Dictionary<string, MeshFeatureSpec> _specs = new(StringComparer.Ordinal);
    private static bool _initialized;
    private static int _aggregateVersion;

    /// <summary>All registered feature specs, in insertion order.</summary>
    public static IReadOnlyCollection<MeshFeatureSpec> Specs
    {
        get { Initialize(); return _specs.Values; }
    }

    /// <summary>Sum of every spec's <see cref="MeshFeatureSpec.Version"/>. Feeds importer versions.</summary>
    public static int AggregateVersion
    {
        get { Initialize(); return _aggregateVersion; }
    }

    public static MeshFeatureSpec? Find(string key)
    {
        Initialize();
        return _specs.TryGetValue(key, out var s) ? s : null;
    }

    public static void Reinitialize()
    {
        _initialized = false;
        _specs.Clear();
        _aggregateVersion = 0;
        Initialize();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Engine bootstrap: scans loaded assemblies for MeshFeatureSpec subclasses. Spec types must be preserved by the consuming application's trim configuration.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:DynamicallyAccessedMembers",
        Justification = "MeshFeatureSpec subclasses are required to expose a parameterless constructor by contract; the trimmer preserves them via the same trim configuration as above.")]
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(MeshFeatureSpec).IsAssignableFrom(type)) continue;

                try
                {
                    var spec = (MeshFeatureSpec)Activator.CreateInstance(type)!;
                    if (_specs.ContainsKey(spec.Key))
                    {
                        Debug.LogWarning($"Duplicate mesh feature key '{spec.Key}'; ignoring {type.FullName}.");
                        continue;
                    }
                    _specs.Add(spec.Key, spec);
                    _aggregateVersion += spec.Version;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to register MeshFeatureSpec {type.FullName}: {ex.Message}");
                }
            }
        }

        Debug.Log($"MeshFeatureRegistry: {_specs.Count} feature(s) registered (aggregate version {_aggregateVersion}).");
    }

    /// <summary>
    /// Call every spec's <see cref="MeshFeatureSpec.PopulateDefaults"/> into the given compound.
    /// Used by importers that emit meshes when building their default settings blob.
    /// </summary>
    public static void PopulateDefaultSettings(EchoObject settings)
    {
        foreach (var spec in Specs)
            spec.PopulateDefaults(settings);
    }
}
