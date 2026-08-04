using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .rendertexture files Echo-serialized RenderTexture descriptions (native Prowl format).
/// </summary>
[ImporterFor(".rendertexture")]
public class RenderTextureImporter : AssetImporter
{
    public override int Version => 1;
    public override bool Import(ImportContext ctx) => ImportHelper.ImportEcho<RenderTexture>(ctx, "render texture");
}
