using Prowl.Runtime;
using Prowl.Runtime.Utils;
using HexaEngine.ImGuiNET;
using Prowl.Runtime.Assets;
using Silk.NET.OpenGL;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(Texture2D), ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr")]
    public class TextureImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr" };

        public bool generateMipmaps = true;
        public TextureWrapMode textureWrap = TextureWrapMode.Repeat;
        public TextureMinFilter textureMinFilter = TextureMinFilter.Nearest;
        public TextureMagFilter textureMagFilter = TextureMagFilter.Linear;

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            if (!Supported.Contains(assetPath.Extension, StringComparer.OrdinalIgnoreCase))
            {
                ImGuiNotify.InsertNotification("Failed to Import Texture.", new(0.8f, 0.1f, 0.1f, 1f), "Format Not Supported: " + assetPath.Extension);
                return;
            }

            // Load the Texture into a TextureData Object and serialize to Asset Folder
            Texture2D texture = Texture2D.FromFile(assetPath.FullName);
            if (generateMipmaps)
                texture.GenerateMipmaps();

            texture.SetTextureFilters(textureMinFilter, textureMagFilter);
            texture.SetWrapModes(textureWrap, textureWrap);

            ctx.SetMainObject(texture);

            ImGuiNotify.InsertNotification("Texture Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

    public class ScriptedEditor
    {
        public object target { get; internal set; }
        public virtual void OnEnable() { }
        public virtual void OnInspectorGUI() { }
        public virtual void OnDisable() { }
    }

    [CustomEditor(typeof(TextureImporter))]
    public class TextureEditor : ScriptedEditor
    {
        private string[] filterNames = Enum.GetNames<TextureMinFilter>();
        private TextureMinFilter[] filters = Enum.GetValues<TextureMinFilter>();
        private string[] filterMagNames = Enum.GetNames<TextureMagFilter>();
        private TextureMagFilter[] filtersMag = Enum.GetValues<TextureMagFilter>();

        private string[] wrapNames = Enum.GetNames<TextureWrapMode>();
        private TextureWrapMode[] wraps = Enum.GetValues<TextureWrapMode>();

        public override void OnInspectorGUI()
        {
            var importer = (TextureImporter)(target as MetaFile).importer;

            ImGui.Checkbox("Generate Mipmaps", ref importer.generateMipmaps);
            // textureFilter
            int filterMinIndex = Array.IndexOf(filters, importer.textureMinFilter);
            if (ImGui.Combo("Min Filter##FilterMinMode", ref filterMinIndex, filterNames, filterNames.Length))
                importer.textureMinFilter = filters[filterMinIndex];
            int filterMagIndex = Array.IndexOf(filtersMag, importer.textureMagFilter);
            if (ImGui.Combo("Mag Filter##filtersMag", ref filterMagIndex, filterMagNames, filterMagNames.Length))
                importer.textureMagFilter = filtersMag[filterMagIndex];
            // textureWrap
            int wrapIndex = Array.IndexOf(wraps, importer.textureWrap);
            if (ImGui.Combo("##WrapMode", ref wrapIndex, wrapNames, wrapNames.Length))
                importer.textureWrap = wraps[wrapIndex];


            if(ImGui.Button("Save"))
                (target as MetaFile).Save();
        }
    }
}
