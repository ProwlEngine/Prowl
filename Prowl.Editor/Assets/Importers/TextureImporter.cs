using Hexa.NET.ImGui;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering.Primitives;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(Texture2D), ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr")]
    public class TextureImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr" };

        public bool generateMipmaps = true;
        public TextureWrap textureWrap = TextureWrap.Repeat;
        public TextureMin textureMinFilter = TextureMin.LinearMipmapLinear;
        public TextureMag textureMagFilter = TextureMag.Linear;

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Load the Texture into a TextureData Object and serialize to Asset Folder
            Texture2D texture = Texture2DLoader.FromFile(assetPath.FullName);

            texture.SetTextureFilters(textureMinFilter, textureMagFilter);
            texture.SetWrapModes(textureWrap, textureWrap);

            if (generateMipmaps)
                texture.GenerateMipmaps();

            ctx.SetMainObject(texture);
        }
    }

    public class ScriptedEditor
    {
        public Gui g => Gui.ActiveGUI;

        public object target { get; internal set; }
        public virtual void OnEnable() { }
        public virtual void OnInspectorGUI() { }
        public virtual void OnDisable() { }
    }

    [CustomEditor(typeof(TextureImporter))]
    public class TextureEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (TextureImporter)(target as MetaFile).importer;

            g.CurrentNode.Layout(LayoutType.Column);

            EditorGUI.DrawProperty(0, "Generate Mipmaps", ref importer.generateMipmaps);
            EditorGUI.DrawProperty(1, "Min Filter", ref importer.textureMinFilter);
            EditorGUI.DrawProperty(2, "Mag Filter", ref importer.textureMagFilter);
            EditorGUI.DrawProperty(3, "Wrap Mode", ref importer.textureWrap);

            if (EditorGUI.QuickButton("Save"))
            {
                (target as MetaFile).Save();
                AssetDatabase.Reimport((target as MetaFile).AssetPath);
            }
        }
    }
}
