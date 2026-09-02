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
    /// <summary>
    /// Import an Echo file whose concrete type is written in the file itself, for assets the editor
    /// has no specific importer for. Same as <see cref="ImportEcho{T}"/> without naming the type up front.
    /// </summary>
    public static bool ImportEchoObject(ImportContext ctx, string errorLabel)
        => ImportEcho<EngineObject>(ctx, errorLabel);

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
                            asset.Name = ctx.FileName;
                            ctx.SetMainAsset(asset);
                            foreach (var dep in dependencies)
                                ctx.AddDependency(dep);
                        }
                        else
                        {
                            Debug.LogError($"Failed to import {errorLabel}: {ctx.AbsolutePath} - deserialization returned null");
                            return false;
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

            // A nested prefab instance keeps its link, which is what the outer prefab depends on. Read
            // from inside the link rather than matching a bare "AssetId" anywhere, which would pick up
            // unrelated fields of the same name.
            if (echo.TryGet("Prefab", out var linkTag) && linkTag.TagType == EchoType.Compound
                && linkTag.TryGet("AssetId", out var prefabIdTag)
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

    /// <summary>
    /// Removes the prefab link from any GameObject sitting inside another prefab instance. Prefabs do
    /// not nest, so such a link is left over from an asset written before that was enforced, and
    /// keeping it would have a player disagree with the editor about what is an instance.
    /// </summary>
    public static bool FlattenNestedPrefabLinks(EchoObject echo, bool insideInstance = false)
    {
        bool removed = false;

        if (echo.TagType == EchoType.Compound)
        {
            bool isInstance = false;

            if (echo.TryGet("Prefab", out var link) && link.TagType == EchoType.Compound
                && link.TryGet("AssetId", out var idTag)
                && Guid.TryParse(idTag.StringValue, out Guid assetId) && assetId != Guid.Empty)
            {
                if (insideInstance)
                    removed |= echo.Remove("Prefab");
                else
                    isInstance = true;
            }

            foreach (var child in echo.Tags.Values)
                removed |= FlattenNestedPrefabLinks(child, insideInstance || isInstance);
        }
        else if (echo.TagType == EchoType.List && echo.List != null)
        {
            foreach (var item in echo.List)
                removed |= FlattenNestedPrefabLinks(item, insideInstance);
        }

        return removed;
    }

}
