using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .scene files, Echo-serialized <see cref="SceneContent"/>, as the <see cref="Scene"/> that stores them.
/// Nothing is built: the stored tree is kept as it is, and what it references is read straight from it. A file saved
/// as a live scene by an older engine is converted on the way in.
/// </summary>
[ImporterFor(".scene")]
public class SceneImporter : AssetImporter
{
    public override int Version => 5;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            var echo = EchoObject.ReadFromString(File.ReadAllText(ctx.AbsolutePath));
            Migration.Releases.Release_1_0.SceneContentLayout(echo);
            var scene = new Scene { Name = ctx.FileName, Data = echo };
            ctx.SetMainAsset(scene);
            ImportHelper.CollectAssetDependencies(echo, ctx);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import scene: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
