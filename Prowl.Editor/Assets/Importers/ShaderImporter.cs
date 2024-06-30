using Prowl.Editor.VeldridShaderParser;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Text.RegularExpressions;
using Veldrid;

namespace Prowl.Editor.Assets
{
    [Importer("ShaderIcon.png", typeof(Prowl.Runtime.Shader), ".shader")]
    public class ShaderImporter : ScriptedImporter
    {
        #warning Veldrid change

        public static readonly string[] Supported = { ".shader" };

        private static FileInfo currentAssetPath;

        private static readonly Regex _preprocessorIncludeRegex = new Regex(@"^\s*#include\s*[""<](.+?)["">]\s*$", RegexOptions.Multiline);

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            currentAssetPath = assetPath;

            string shaderScript = File.ReadAllText(assetPath.FullName);

            shaderScript = ClearAllComments(shaderScript);

            ParsedShader parsed = new VeldridShaderParser.VeldridShaderParser(shaderScript).Parse();

            List<ShaderPass> passes = new List<ShaderPass>();
            foreach (var parsedPass in parsed.Passes)
            {
                var tags = new List<(string, string)>();
                foreach (var tag in parsedPass.Tags)
                    tags.Add((tag.Key, tag.Value));
                // Add global tags - these are tags that apply to all passes
                foreach (var tag in parsed.Global.Tags)
                    tags.Add((tag.Key, tag.Value));

                var programs = new List<(ShaderStages, string)>();
                foreach (var program in parsedPass.Programs) 
                {
                    ShaderStages shaderStage = program.Type;

                    // Recursively handle Imports
                    program.Content = _preprocessorIncludeRegex.Replace(program.Content, ImportReplacer);

                    // Strip out comments and Multi-line Comments
                    program.Content = ClearAllComments(program.Content);

                    // Insert global include at the top of the shader
                    program.Content.Insert(0, parsed.Global.GlobalInclude);

                    // Insert #version at the top of the shader
                    program.Content.Insert(0, "#version 450");

                    programs.Add((shaderStage, program.Content));
                }

                ShaderPass pass = new(parsedPass.Name, tags.ToArray(), programs.ToArray(), new());

                BlendAttachmentDescription blend = parsedPass.Blend ?? parsed.Global.Blend ?? BlendAttachmentDescription.Disabled;

                pass.blend = new() {
                    BlendFactor = RgbaFloat.White,
                    AlphaToCoverageEnabled = false,
                    AttachmentStates = [blend]
                };

                pass.depthStencil = parsedPass.Stencil ?? parsed.Global.Stencil ?? DepthStencilStateDescription.Disabled;

                pass.cullMode = parsedPass.Cull;

                passes.Add(pass);
            }

            Prowl.Runtime.Shader shader = new Prowl.Runtime.Shader(parsed.Name, passes.ToArray());


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
