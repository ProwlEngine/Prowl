using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .mesh files Echo-serialized Mesh objects (native Prowl format).
/// Feature sub-assets (SDF, BVH, Prism) are generated here based on importer settings.
/// </summary>
[ImporterFor(".mesh")]
public class MeshImporter : AssetImporter
{
    private const int BaseVersion = 1;

    public override int Version => BaseVersion + MeshFeatureRegistry.AggregateVersion;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var serCtx = ImportHelper.CreateTrackingContext(out var dependencies);

            var mesh = Serializer.Deserialize<Mesh>(echo, serCtx);
            if (mesh == null)
            {
                Debug.LogError($"Failed to deserialize mesh: {ctx.AbsolutePath}");
                return false;
            }

            mesh.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
            ctx.SetMainAsset(mesh);

            foreach (var dep in dependencies)
                ctx.AddDependency(dep);

            MeshFeatureImporter.GenerateAll(mesh, ctx.Settings, ctx);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import mesh: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        MeshFeatureRegistry.PopulateDefaultSettings(s);
        return s;
    }
}
