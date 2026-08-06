using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .navmesh files - Echo-serialized NavMeshData objects (baked navigation meshes).
/// </summary>
[ImporterFor(".navmesh")]
public class NavMeshDataImporter : AssetImporter
{
    public override int Version => 1;
    public override bool Import(ImportContext ctx) => ImportHelper.ImportEcho<NavMeshData>(ctx, "nav mesh data");
}
