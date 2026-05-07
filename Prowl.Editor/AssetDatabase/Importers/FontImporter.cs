// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".ttf")]
public class FontImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            var font = new FontAsset();
            font.fontName = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
            font.fontData = File.ReadAllBytes(ctx.AbsolutePath);
            ctx.SetMainAsset(font);
        }
        catch (System.Exception ex)
        {
            Runtime.Debug.LogError($"Failed to import font: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
