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
    public override int Version => 3; // Bumped: re-import to regenerate scenes cached by the pre-fix
                                      // GameObject deserializer (which could drop every object to null).

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
                ctx.SetMainAsset(scene);

                // Also walk the raw echo for PrefabAssetId references
                // (these are plain Guid strings, not AssetRef fields, so not auto-tracked)
                ImportHelper.CollectAssetDependencies(echo, dependencies);

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
}
