using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .prefab files serialized GameObject hierarchies wrapped in PrefabAsset.
/// Dependencies are discovered by walking the raw EchoObject tree for $assetId tags
/// and PrefabAssetId references, without deserializing the full GO hierarchy.
/// </summary>
[ImporterFor(".prefab")]
public class PrefabImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var goEcho = EchoObject.ReadFromString(text);

            // Walk the EchoObject tree for dependencies no deserialization needed
            var dependencies = new HashSet<Guid>();
            CollectDependencies(goEcho, dependencies);

            // Wrap in PrefabAsset
            var prefab = new PrefabAsset();
            prefab.GameObjectData = goEcho;
            prefab.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);

            ctx.SetMainAsset(prefab);
            foreach (var dep in dependencies)
                ctx.AddDependency(dep);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import prefab: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Recursively walks an EchoObject tree to find all asset references ($assetId tags)
    /// and prefab references (PrefabAssetId strings).
    /// </summary>
    private static void CollectDependencies(EchoObject echo, HashSet<Guid> deps)
    {
        if (echo == null) return;

        if (echo.TagType == EchoType.Compound)
        {
            // Check for $assetId references (materials, textures, meshes, etc.)
            if (echo.TryGet("$assetId", out var assetIdTag))
            {
                if (Guid.TryParse(assetIdTag.StringValue, out var assetGuid) && assetGuid != Guid.Empty)
                    deps.Add(assetGuid);
            }

            // Check for PrefabAssetId (nested prefab references)
            if (echo.TryGet("PrefabAssetId", out var prefabIdTag))
            {
                if (Guid.TryParse(prefabIdTag.StringValue, out var prefabGuid) && prefabGuid != Guid.Empty)
                    deps.Add(prefabGuid);
            }

            // Recurse into all compound children
            foreach (var kvp in echo.Tags)
                CollectDependencies(kvp.Value, deps);
        }
        else if (echo.TagType == EchoType.List && echo.List != null)
        {
            foreach (var item in echo.List)
                CollectDependencies(item, deps);
        }
    }
}
