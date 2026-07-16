using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

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

    /// <summary>
    /// Full boilerplate for importers that simply deserialize a single Echo-serialized asset.
    /// Reads the file, deserializes as T with dependency tracking, sets it as the main asset,
    /// and forwards all discovered dependencies to ctx. Returns false and logs on any error.
    /// </summary>
    public static bool ImportEcho<T>(ImportContext ctx, string errorLabel) where T : EngineObject
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);
            var serCtx = CreateTrackingContext(out var dependencies);
            var asset = Serializer.Deserialize<T>(echo, serCtx);
            if (asset != null)
            {
                ctx.SetMainAsset(asset);
                foreach (var dep in dependencies)
                    ctx.AddDependency(dep);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import {errorLabel}: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Recursively walks an EchoObject tree collecting AssetID and PrefabAssetId Guid strings
    /// into deps. AssetID must be walked here even when a tracking context is active because
    /// prefab-instance PropertyOverride.Value blobs are pre-serialized without a context
    /// (see PrefabUtility.CompareField), so AssetRefs inside them never reach normal tracking.
    /// </summary>
    public static void CollectAssetDependencies(EchoObject echo, HashSet<Guid> deps)
    {
        if (echo == null) return;

        if (echo.TagType == EchoType.Compound)
        {
            if (echo.TryGet("AssetID", out var assetIdTag)
                && Guid.TryParse(assetIdTag.StringValue, out var assetGuid) && assetGuid != Guid.Empty)
                deps.Add(assetGuid);

            if (echo.TryGet("PrefabAssetId", out var prefabIdTag)
                && Guid.TryParse(prefabIdTag.StringValue, out var prefabGuid) && prefabGuid != Guid.Empty)
                deps.Add(prefabGuid);

            foreach (var kvp in echo.Tags)
                CollectAssetDependencies(kvp.Value, deps);
        }
        else if (echo.TagType == EchoType.List && echo.List != null)
        {
            foreach (var item in echo.List)
                CollectAssetDependencies(item, deps);
        }
    }
}
