using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .mat files Echo-serialized Material objects (native Prowl format).
/// </summary>
[ImporterFor(".mat")]
public class MaterialImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var serCtx = ImportHelper.CreateTrackingContext(out var dependencies);

            var material = Serializer.Deserialize<Material>(echo, serCtx);
            if (material != null)
            {
                material.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
                ctx.SetMainAsset(material);
                foreach (var dep in dependencies)
                    ctx.AddDependency(dep);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import material: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
