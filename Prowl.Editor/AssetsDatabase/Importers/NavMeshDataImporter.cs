using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .navmesh files - Echo-serialized NavMeshData objects (baked navigation meshes).
/// Binary, because a navmesh is almost entirely compressed voxelization blobs: as text they are
/// base64, which is larger, slower to parse, and undiffable anyway.
/// </summary>
[ImporterFor(".navmesh")]
public class NavMeshDataImporter : AssetImporter
{
    /// <summary>Bumping this reimports every .navmesh. A text-written one does not parse as
    /// binary, so it fails the import rather than loading wrong — rebake it.</summary>
    public override int Version => 2;

    public override bool Import(ImportContext ctx) => ImportHelper.ImportEchoBinary<NavMeshData>(ctx, "nav mesh data");
}
