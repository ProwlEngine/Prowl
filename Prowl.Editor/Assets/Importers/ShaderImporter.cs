using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Editor.Utilities;

using System.Text.RegularExpressions;

namespace Prowl.Editor.Assets
{
    [Importer("ShaderIcon.png", typeof(Runtime.Shader), ".shader")]
    public class ShaderImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".shader" };

        private static FileInfo currentAssetPath;

        private static readonly Regex _preprocessorIncludeRegex = new Regex(@"^\s*#include\s*[""<](.+?)["">]\s*$", RegexOptions.Multiline);

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            currentAssetPath = assetPath;

            string shaderScript = File.ReadAllText(assetPath.FullName);

            Runtime.Shader? shader;

            try
            {
                shader = ShaderParser.ParseShader(shaderScript);
            }
            catch (Exception ex)
            {
                if (assetPath.Name == "InternalErrorShader.shader")
                    Debug.LogError("InternalErrorShader failed to compile. Non-compiling shaders loaded through script will cause cascading exceptions.", ex);

                Debug.LogError("Failed to compile shader", ex);
                shader = Application.AssetProvider.LoadAsset<Runtime.Shader>("Defaults/InternalErrorShader.shader").Res;
            }
            
            ctx.SetMainObject(shader);
        }
    }
}
