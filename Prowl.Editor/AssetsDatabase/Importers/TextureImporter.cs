using System.IO;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".hdr", ".dds", ".exr")]
public class TextureImporter : AssetImporter
{
    public override int Version => 4; // Bumped: min/mag/mip filter booleans

    public override bool Import(ImportContext ctx)
    {
        // Settings are guaranteed to have defaults merged by EditorAssetDatabase.RunImport
        bool generateMipmaps = ctx.Settings?.TryGet("generateMipmaps", out var mipTag) == true && mipTag.BoolValue;

        // Allocate mip storage up front so GenerateMipmaps (inside FromFile) has somewhere to write
        var texture = Texture2D.FromFile(ctx.AbsolutePath, generateMipmaps);
        texture.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);

        // Read filter/wrap settings (defaults merged by RunImport)
        bool minLinear = ctx.Settings?.TryGet("minLinear", out EchoObject? minTag) != true || minTag.BoolValue;
        bool magLinear = ctx.Settings?.TryGet("magLinear", out EchoObject? magTag) != true || magTag.BoolValue;
        bool mipLinear = ctx.Settings?.TryGet("mipLinear", out EchoObject? mipFilterTag) != true || mipFilterTag.BoolValue;
        SamplerAddressMode wrapMode = ctx.Settings?.TryGet("wrapMode", out EchoObject? wrapTag) == true
            ? (SamplerAddressMode)wrapTag.IntValue : SamplerAddressMode.Wrap;

        texture.SetTextureFilters(CombineFilters(minLinear, magLinear, mipLinear && generateMipmaps));
        texture.SetWrapModes(wrapMode, wrapMode);

        ctx.SetMainAsset(texture);
        return true;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(true);
        s["sRGB"] = new EchoObject(true);
        s["minLinear"] = new EchoObject(true);
        s["magLinear"] = new EchoObject(true);
        s["mipLinear"] = new EchoObject(true);
        s["wrapMode"] = new EchoObject((int)SamplerAddressMode.Wrap);
        return s;
    }

    private static SamplerFilter CombineFilters(bool minLinear, bool magLinear, bool mipLinear)
    {
        // SamplerFilter packs three flags: min (bit 2), mag (bit 1), mip (bit 0).
        int value = (minLinear ? 0b100 : 0) | (magLinear ? 0b010 : 0) | (mipLinear ? 0b001 : 0);
        return (SamplerFilter)value;
    }
}
