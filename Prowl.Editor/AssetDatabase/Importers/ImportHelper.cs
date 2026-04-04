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
    /// Creates a SerializationContext that tracks all $assetId references encountered
    /// during deserialization. After deserialization, discoveredDependencies contains
    /// the GUIDs of all referenced assets.
    /// </summary>
    public static SerializationContext CreateTrackingContext(out HashSet<Guid> discoveredDependencies)
    {
        var deps = new HashSet<Guid>();
        discoveredDependencies = deps;

        var ctx = new SerializationContext();

        // Get the default OnDeserialize from AssetDatabase.ConfigureContext
        AssetDatabase.ConfigureContext(ctx);
        var originalDeserialize = ctx.OnDeserialize;

        // Wrap it to also track the GUIDs
        ctx.OnDeserialize = (data, type, c) =>
        {
            if (typeof(EngineObject).IsAssignableFrom(type)
                && data.TryGet("$assetId", out var assetIdTag))
            {
                if (Guid.TryParse(assetIdTag.StringValue, out var assetId) && assetId != Guid.Empty)
                    deps.Add(assetId);
            }

            // Call the original handler to actually resolve the reference
            return originalDeserialize?.Invoke(data, type, c) ?? (false, null);
        };

        return ctx;
    }
}
