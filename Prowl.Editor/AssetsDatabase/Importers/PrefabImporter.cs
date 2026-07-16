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
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var goEcho = EchoObject.ReadFromString(text);

            var dependencies = new HashSet<Guid>();
            ImportHelper.CollectAssetDependencies(goEcho, dependencies);

            var prefab = new PrefabAsset();
            prefab.GameObjectData = goEcho;

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
}
