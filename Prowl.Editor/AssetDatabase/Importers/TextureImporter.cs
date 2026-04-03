using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".hdr", ".gif")]
public class TextureImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        bool generateMipmaps = settings?.TryGet("generateMipmaps", out var mipTag) == true && mipTag.BoolValue;

        var texture = Texture2D.FromFile(absolutePath, generateMipmaps);
        texture.Name = Path.GetFileNameWithoutExtension(absolutePath);

        return new ImportResult { MainAsset = texture };
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(true);
        s["sRGB"] = new EchoObject(true);
        return s;
    }
}
