namespace Prowl.Editor.Importers;

/// <summary>
/// Fallback importer for unrecognized file types.
/// Tracks the file in the database but does not produce an importable asset.
/// </summary>
public class DefaultImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        // Default importer just tracks the file no runtime asset produced
        return true;
    }
}
