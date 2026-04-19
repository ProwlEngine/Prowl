using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports <c>.shadergraph</c> files — Echo-serialized <see cref="ShaderGraph"/> assets.
/// Mirrors the Material importer pattern: pure native format, no external conversion.
/// </summary>
[ImporterFor(".shadergraph")]
public class ShaderGraphImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var serCtx = ImportHelper.CreateTrackingContext(out var dependencies);
            var graph = Serializer.Deserialize<ShaderGraph>(echo, serCtx);
            if (graph == null)
            {
                Debug.LogError($"Failed to deserialize shader graph: {ctx.AbsolutePath}");
                return false;
            }

            graph.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
            ctx.SetMainAsset(graph);

            foreach (var dep in dependencies)
                ctx.AddDependency(dep);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import shader graph: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
