using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using HexaEngine.ImGuiNET;
using Prowl.Runtime.Assets;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(Texture2D), ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr")]
    public class TextureImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".png", ".bmp", ".jpg", ".jpeg", ".qoi", ".psd", ".tga", ".dds", ".hdr", ".ktx", ".pkm", ".pvr" };

        public bool generateMipmaps = true;
        public Raylib_cs.TextureWrap textureWrap = Raylib_cs.TextureWrap.TEXTURE_WRAP_REPEAT;
        public Raylib_cs.TextureFilter textureFilter = Raylib_cs.TextureFilter.TEXTURE_FILTER_BILINEAR;

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            if (!Supported.Contains(assetPath.Extension, StringComparer.OrdinalIgnoreCase))
            {
                ImGuiNotify.InsertNotification("Failed to Import Texture.", new(0.8f, 0.1f, 0.1f, 1f), "Format Not Supported: " + assetPath.Extension);
                return;
            }

            try
            {
                // Load the Texture into a TextureData Object and serialize to Asset Folder
                Texture2D texture = new Texture2D(assetPath.FullName);
                if (generateMipmaps)
                    texture.GenerateMipMaps();

                texture.SetFilter(textureFilter);
                texture.SetWrap(textureWrap);

                ctx.SetMainObject(texture);

                ImGuiNotify.InsertNotification("Texture Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
            }
            catch (Exception e)
            {
                ImGuiNotify.InsertNotification("Failed to Import Texture.", new(0.8f, 0.1f, 0.1f, 1), "Reason: " + e.Message);
            }
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
        private string[] filterNames = Enum.GetNames<Raylib_cs.TextureFilter>();
        private Raylib_cs.TextureFilter[] filters = Enum.GetValues<Raylib_cs.TextureFilter>();

        private string[] wrapNames = Enum.GetNames<Raylib_cs.TextureWrap>();
        private Raylib_cs.TextureWrap[] wraps = Enum.GetValues<Raylib_cs.TextureWrap>();

        public override void OnInspectorGUI()
        {
            var importer = (TextureImporter)(target as MetaFile).importer;

            ImGui.Checkbox("Generate Mipmaps", ref importer.generateMipmaps);
            // textureFilter
            int filterIndex = Array.IndexOf(filters, importer.textureFilter);
            if (ImGui.Combo("##FilterMode", ref filterIndex, filterNames, filterNames.Length))
                importer.textureFilter = filters[filterIndex];
            // textureWrap
            int wrapIndex = Array.IndexOf(wraps, importer.textureWrap);
            if (ImGui.Combo("##WrapMode", ref wrapIndex, wrapNames, wrapNames.Length))
                importer.textureWrap = wraps[wrapIndex];


            if(ImGui.Button("Save"))
                (target as MetaFile).Save();
        }
    }
}
