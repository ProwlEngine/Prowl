using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Text.RegularExpressions;
using Veldrid;

namespace Prowl.Editor.Assets
{
    [Importer("ShaderIcon.png", typeof(Prowl.Runtime.Shader), ".shader")]
    public class ShaderImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".shader" };

        private static FileInfo currentAssetPath;

        private static readonly Regex _preprocessorIncludeRegex = new Regex(@"^\s*#include\s*[""<](.+?)["">]\s*$", RegexOptions.Multiline);

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            currentAssetPath = assetPath;

            string shaderScript = File.ReadAllText(assetPath.FullName);

            shaderScript = ClearAllComments(shaderScript);

            ShaderParser.ParsedShader parsed = new ShaderParser.ShaderParser(shaderScript).Parse();

            List<ShaderPass> passes = new List<ShaderPass>();

            

            ctx.SetMainObject(shader);
        }

        private static string ImportReplacer(Match match)
        {
            var relativePath = match.Groups[1].Value + ".glsl";

            // First check the Defaults path
            var file = new FileInfo(Path.Combine(Project.ProjectDefaultsDirectory, relativePath));
            if (!file.Exists)
                file = new FileInfo(Path.Combine(currentAssetPath.Directory!.FullName, relativePath));

            if (!file.Exists)
            {
                Debug.LogError("Failed to Import Shader. Include not found: " + file.FullName);
                return "";
            }

            // Recursively handle Imports
            var includeScript = _preprocessorIncludeRegex.Replace(File.ReadAllText(file.FullName), ImportReplacer);

            return includeScript;
        }

        public static string ClearAllComments(string input)
        {
            // Remove single-line comments
            var noSingleLineComments = Regex.Replace(input, @"//.*", "");

            // Remove multi-line comments
            var noComments = Regex.Replace(noSingleLineComments, @"/\*.*?\*/", "", RegexOptions.Singleline);

            return noComments;
        }

    }
}
