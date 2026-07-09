// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports TrueType (.ttf) and OpenType (.otf) font files as FontAsset resources.
/// The raw font bytes are embedded in the asset so Scribe can reconstruct the
/// FontFile and generate glyph atlases at runtime.
/// </summary>
[ImporterFor(".ttf", ".otf")]
public class FontImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            byte[] fontData = File.ReadAllBytes(ctx.AbsolutePath);
            string name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);

            var fontAsset = new FontAsset(name, fontData);
            ctx.SetMainAsset(fontAsset);

            Debug.Log($"Imported font: {fontAsset.FamilyName} ({fontAsset.Style})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import font: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
