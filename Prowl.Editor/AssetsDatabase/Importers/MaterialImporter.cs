using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .mat files Echo-serialized Material objects (native Prowl format).
/// </summary>
[ImporterFor(".mat")]
public class MaterialImporter : AssetImporter
{
    public override int Version => 1;
    public override bool Import(ImportContext ctx) => ImportHelper.ImportEcho<Material>(ctx, "material");
}
