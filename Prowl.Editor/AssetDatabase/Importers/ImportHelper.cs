using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Shared utilities for asset importers.
/// </summary>
public static class ImportHelper
{
    /// <summary>
    /// Creates a SerializationContext that tracks all AssetRef references encountered
    /// during deserialization. After deserialization, discoveredDependencies contains
    /// the GUIDs of all referenced assets.
    /// </summary>
    public static SerializationContext CreateTrackingContext(out HashSet<Guid> discoveredDependencies)
    {
        var ctx = new DependencySerializationContext();
        discoveredDependencies = ctx.Dependencies;
        return ctx;
    }
}
