using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .prefab files serialized GameObject hierarchies wrapped in PrefabAsset.
/// Dependencies are discovered by walking the raw EchoObject tree for AssetID tags
/// (from AssetRef serialization) and PrefabAssetId references, without deserializing the full GO hierarchy.
/// </summary>
[ImporterFor(".prefab")]
public class PrefabImporter : AssetImporter
{
    // 2: PrefabAsset stores its tree in a backing field, so cached payloads from v1 no longer bind.
    public override int Version => 2;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var goEcho = EchoObject.ReadFromString(text);

            if (!DescribesGameObject(goEcho))
            {
                Debug.LogError($"Prefab does not contain a GameObject hierarchy: {ctx.AbsolutePath}");
                return false;
            }

            // Prefabs do not nest. An asset written before that was enforced still carries the link,
            // so it is dropped here and those objects load as ordinary content of this prefab. Every
            // link in a prefab file is nested by definition: the asset's own root carries none.
            ImportHelper.FlattenNestedPrefabLinks(goEcho, insideInstance: true);

            var dependencies = new HashSet<Guid>();
            ImportHelper.CollectAssetDependencies(goEcho, dependencies);

            var prefab = new PrefabAsset();
            prefab.GameObjectData = goEcho;
            prefab.Name = ctx.FileName;

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
    /// Cheap shape check so a file that parses as Echo but isn't a GameObject fails at import,
    /// where the path is reported, rather than as a null at some later Instantiate call.
    /// </summary>
    private static bool DescribesGameObject(EchoObject? echo)
        => echo is { TagType: EchoType.Compound } && echo.TryGet("Transform", out _) && echo.TryGet("Components", out _);
}
