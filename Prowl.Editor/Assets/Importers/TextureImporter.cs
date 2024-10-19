// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Assets;

[Importer("FileIcon.png", typeof(Texture2D), ".png", ".bmp", ".jpg", ".jpeg", ".tga", ".dds", ".pbm", ".webp", ".tif", ".tiff", ".gif")]
public class TextureImporter : ScriptedImporter
{
    public static readonly string[] Supported = [".png", ".bmp", ".jpg", ".jpeg", ".tga", ".dds", ".pbm", ".webp", ".tif", ".tiff", ".gif"];

    public bool generateMipmaps = true;

    public TextureWrapMode textureWrap = TextureWrapMode.Wrap;

    public FilterType textureMinFilter = FilterType.Linear;
    public FilterType textureMagFilter = FilterType.Linear;
    public FilterType textureMipFilter = FilterType.Linear;

    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        // Load the Texture into a TextureData Object and serialize to Asset Folder
        Texture2D texture = Texture2DLoader.FromFile(assetPath.FullName, generateMipmaps);

        texture.Name = Path.GetFileNameWithoutExtension(assetPath.Name);

        texture.Sampler.SetFilter(textureMinFilter, textureMagFilter, textureMipFilter);
        texture.Sampler.SetWrapMode(SamplerAxis.U | SamplerAxis.V | SamplerAxis.W, textureWrap);

        if (generateMipmaps)
            texture.GenerateMipmaps();

        ctx.SetMainObject(texture);
    }
}


[CustomEditor(typeof(TextureImporter))]
public class TextureImporterEditor : ScriptedEditor
{
    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        var importer = (TextureImporter)(target as MetaFile).importer;

        gui.CurrentNode.Layout(LayoutType.Column);

        if (EditorGUI.DrawProperty(0, "Generate Mipmaps", ref importer.generateMipmaps))
            changes.Add(importer, nameof(TextureImporter.generateMipmaps));
        if (EditorGUI.DrawProperty(1, "Min Filter", ref importer.textureMinFilter))
            changes.Add(importer, nameof(TextureImporter.textureMinFilter));
        if (EditorGUI.DrawProperty(2, "Mag Filter", ref importer.textureMagFilter))
            changes.Add(importer, nameof(TextureImporter.textureMagFilter));
        if (EditorGUI.DrawProperty(3, "Wrap Mode", ref importer.textureWrap))
            changes.Add(importer, nameof(TextureImporter.textureWrap));

        if (EditorGUI.StyledButton("Save"))
        {
            (target as MetaFile).Save();
            AssetDatabase.Reimport((target as MetaFile).AssetPath);
        }
    }
}
