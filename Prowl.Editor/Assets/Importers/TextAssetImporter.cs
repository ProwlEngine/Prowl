// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[Importer("FileIcon.png", typeof(TextAsset), ".txt", ".md")]
public class TextAssetImporter : ScriptedImporter
{
    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        TextAsset textAsset = new();
        textAsset.Text = File.ReadAllText(assetPath.FullName);

        ctx.SetMainObject(textAsset);
    }
}
