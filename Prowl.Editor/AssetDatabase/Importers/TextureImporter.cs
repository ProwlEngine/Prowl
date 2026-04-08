using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".hdr", ".dds", ".exr")]
public class TextureImporter : AssetImporter
{
    public override int Version => 2; // Bumped: now applies filter/wrap settings

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        // Settings are guaranteed to have defaults merged by EditorAssetDatabase.RunImport
        bool generateMipmaps = settings?.TryGet("generateMipmaps", out var mipTag) == true && mipTag.BoolValue;

        // Load texture WITHOUT mipmaps first — we'll generate them after applying settings
        var texture = Texture2D.FromFile(absolutePath, false);
        texture.Name = Path.GetFileNameWithoutExtension(absolutePath);

        // Read filter/wrap settings (defaults merged by RunImport)
        var minFilter = settings?.TryGet("minFilter", out var minTag2) == true
            ? (TextureMin)minTag2.IntValue : (generateMipmaps ? TextureMin.LinearMipmapLinear : TextureMin.Linear);
        var magFilter = settings?.TryGet("magFilter", out var magTag) == true
            ? (TextureMag)magTag.IntValue : TextureMag.Linear;
        var wrapMode = settings?.TryGet("wrapMode", out var wrapTag) == true
            ? (TextureWrap)wrapTag.IntValue : TextureWrap.Repeat;

        // Generate mipmaps if requested (must happen before setting mipmap filters)
        if (generateMipmaps)
            texture.GenerateMipmaps();

        // Downgrade mipmap filters if no mipmaps
        if (!generateMipmaps)
        {
            minFilter = minFilter switch
            {
                TextureMin.NearestMipmapNearest or TextureMin.NearestMipmapLinear => TextureMin.Nearest,
                TextureMin.LinearMipmapNearest or TextureMin.LinearMipmapLinear => TextureMin.Linear,
                _ => minFilter
            };
        }

        texture.SetTextureFilters(minFilter, magFilter);
        texture.SetWrapModes(wrapMode, wrapMode);

        return new ImportResult { MainAsset = texture };
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(true);
        s["sRGB"] = new EchoObject(true);
        s["minFilter"] = new EchoObject((int)TextureMin.LinearMipmapLinear);
        s["magFilter"] = new EchoObject((int)TextureMag.Linear);
        s["wrapMode"] = new EchoObject((int)TextureWrap.Repeat);
        return s;
    }
}
