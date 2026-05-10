using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .scene files Echo-serialized Scene objects (native Prowl format).
/// </summary>
[ImporterFor(".scene")]
public class SceneImporter : AssetImporter
{
    public override int Version => 2; // Bumped: now tracks PrefabAssetId deps

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var serCtx = ImportHelper.CreateTrackingContext(out var dependencies);

            var scene = Serializer.Deserialize<Scene>(echo, serCtx);
            if (scene != null)
            {
                scene.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
                ctx.SetMainAsset(scene);

                // Also walk the raw echo for PrefabAssetId references
                // (these are plain Guid strings, not AssetRef fields, so not auto-tracked)
                CollectPrefabDependencies(echo, dependencies);

                foreach (var dep in dependencies)
                    ctx.AddDependency(dep);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import scene: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }

    /// <summary>Walk EchoObject tree for PrefabAssetId Guid strings.</summary>
    private static void CollectPrefabDependencies(EchoObject echo, HashSet<Guid> deps)
    {
        if (echo == null) return;

        if (echo.TagType == EchoType.Compound)
        {
            if (echo.TryGet("PrefabAssetId", out var prefabIdTag))
            {
                if (Guid.TryParse(prefabIdTag.StringValue, out var prefabGuid) && prefabGuid != Guid.Empty)
                    deps.Add(prefabGuid);
            }
            foreach (var kvp in echo.Tags)
                CollectPrefabDependencies(kvp.Value, deps);
        }
        else if (echo.TagType == EchoType.List && echo.List != null)
        {
            foreach (var item in echo.List)
                CollectPrefabDependencies(item, deps);
        }
    }
}
