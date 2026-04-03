using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Fallback importer for unrecognized file types.
/// Tracks the file in the database but does not produce an importable asset.
/// </summary>
public class DefaultImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        // Default importer just tracks the file — no runtime asset produced
        return new ImportResult();
    }
}
