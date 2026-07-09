using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .terraindata files - Echo-serialized TerrainData objects.
/// </summary>
[ImporterFor(".terraindata")]
public class TerrainDataImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var serCtx = ImportHelper.CreateTrackingContext(out var dependencies);

            var terrainData = Serializer.Deserialize<TerrainData>(echo, serCtx);
            if (terrainData != null)
            {
                terrainData.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
                ctx.SetMainAsset(terrainData);
                foreach (var dep in dependencies)
                    ctx.AddDependency(dep);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import terrain data: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
