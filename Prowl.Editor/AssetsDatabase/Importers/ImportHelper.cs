using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary> Provides common import operations for Echo-serialized assets, including deserialization with dependency tracking, dependency collection, and prefab link flattening. </summary>
public static class ImportHelper
{
    /// <summary>
    /// Creates a SerializationContext that records every asset a read or write reaches. Afterwards
    /// discoveredDependencies holds the GUIDs of the assets referenced by a plain field.
    /// </summary>
    public static SerializationContext CreateTrackingContext(out HashSet<Guid> discoveredDependencies)
    {
        var ctx = new DependencySerializationContext();
        discoveredDependencies = ctx.Dependencies;
        return ctx;
    }

    /// <summary> Import an Echo file whose concrete type is written in the file itself, for assets the editor has no specific importer for. Same as ImportEcho&lt;T&gt; without naming the type up front. </summary>
    public static bool ImportEchoObject(ImportContext ctx, string errorLabel)
        => ImportEcho<Asset>(ctx, errorLabel);

    /// <summary> Reads an Echo-serialized file, deserializes it as T with dependency tracking, sets the result as the main asset on ctx, and forwards all discovered dependencies. Returns false and logs on any error. </summary>
    public static bool ImportEcho<T>(ImportContext ctx, string errorLabel) where T : Asset
        => ImportEcho<T>(ctx, errorLabel, static path => EchoObject.ReadFromString(File.ReadAllText(path)));

    /// <summary>
    /// As <see cref="ImportEcho{T}(ImportContext, string)"/>, for assets written with Echo's
    /// binary format — the one to use when the payload is bulk bytes rather than something a
    /// human reads or diffs.
    /// </summary>
    public static bool ImportEchoBinary<T>(ImportContext ctx, string errorLabel) where T : Asset
        => ImportEcho<T>(ctx, errorLabel, static path => EchoObject.ReadFromBinary(new FileInfo(path)));

    private static bool ImportEcho<T>(ImportContext ctx, string errorLabel, Func<string, EchoObject> read) where T : Asset
    {
        try
        {
            var echo = read(ctx.AbsolutePath);
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
    /// Walks a stored tree for the assets it names without building anything: <c>$asset</c> stubs are hard edges,
    /// <c>$assetRef</c> soft ones, and prefab links editor ones. The walk is needed even when a tracking context
    /// was used, because prefab override blobs are serialized ahead of time without one (see
    /// PrefabUtility.CompareField), so what is inside them never reached it.
    /// </summary>
    public static void CollectAssetDependencies(EchoObject echo, ImportContext ctx)
        => CollectAssetDependencies(echo, ctx.Dependencies, ctx.SoftDependencies, ctx.EditorDependencies);

    public static void CollectAssetDependencies(EchoObject echo, HashSet<Guid> hard, HashSet<Guid>? soft = null, HashSet<Guid>? editor = null)
    {
        if (echo == null) return;

        if (echo.TagType == EchoType.Compound)
        {
            if (echo.TryGet("$asset", out var stub) && Guid.TryParse(stub.StringValue, out var assetGuid) && assetGuid != Guid.Empty)
                hard.Add(assetGuid);
            if (soft != null && echo.TryGet("$assetRef", out var lazy) && Guid.TryParse(lazy.StringValue, out var softGuid) && softGuid != Guid.Empty)
                soft.Add(softGuid);

            // A prefab instance keeps its link. Read from inside the link rather than matching a bare
            // "AssetId" anywhere, which would pick up unrelated fields of the same name.
            if (editor != null && echo.TryGet("Prefab", out var linkTag) && linkTag.TagType == EchoType.Compound
                && linkTag.TryGet("AssetId", out var prefabIdTag)
                && Guid.TryParse(prefabIdTag.StringValue, out var prefabGuid) && prefabGuid != Guid.Empty)
                editor.Add(prefabGuid);

            foreach (var kvp in echo.Tags)
                CollectAssetDependencies(kvp.Value, hard, soft, editor);
        }
        else if (echo.TagType == EchoType.List && echo.List != null)
        {
            foreach (var item in echo.List)
                CollectAssetDependencies(item, hard, soft, editor);
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
