using System;
using System.Collections.Generic;
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
    private const int BaseVersion = 2;

    public override int Version => BaseVersion + MeshFeatureRegistry.AggregateVersion;

    public override EchoSource Source => EchoSource.Text;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            SerializationContext serCtx = ImportHelper.CreateTrackingContext(out HashSet<Guid>? dependencies);

            Mesh? mesh = Serializer.Deserialize<Mesh>(echo, serCtx);
            if (mesh.IsNotValid())
            {
                Debug.LogError($"Failed to deserialize mesh: {ctx.AbsolutePath}");
                return false;
            }

            mesh.Name = ctx.FileName;
            ctx.SetMainAsset(mesh);

            foreach (Guid dep in dependencies)
                ctx.AddDependency(dep);

            MeshFeatureImporter.GenerateAll([mesh], ctx.Settings, ctx, ["main"]);
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
