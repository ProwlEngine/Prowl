using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Text.RegularExpressions;
using Veldrid;
using Veldrid.SPIRV;

using static System.Text.Encoding;

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

            foreach (var parsedPass in parsed.Passes)
            {
                ShaderPassDescription passDesc = new();

                passDesc.Tags = parsedPass.Tags;
                passDesc.ShaderSources = parsedPass.Programs.ToArray();
                passDesc.BlendState = new BlendStateDescription(RgbaFloat.White, parsedPass.Blend.Value);
                passDesc.CullingMode = parsedPass.Cull;
                passDesc.DepthClipEnabled = true;
                passDesc.Keywords = parsedPass.Keywords;
                passDesc.DepthStencilState = parsedPass.Stencil.Value;

                ShaderPass pass = new ShaderPass(parsedPass.Name, passDesc);

                pass.CompilePrograms(new ImporterVariantCompiler()
                {   
                    Inputs = parsedPass.Inputs.Inputs.ToArray(),
                    Resources = parsedPass.Inputs.Resources.ToArray()
                });

                passes.Add(pass);
            }

            Runtime.Shader shader = new Runtime.Shader("New Shader", parsed.Properties.ToArray(), passes.ToArray()); 

            ctx.SetMainObject(shader);
        }

        private class ImporterVariantCompiler : IVariantCompiler
        {
            public MeshResource[] Inputs;
            public ShaderResource[][] Resources;

            public ShaderVariant CompileVariant(ShaderSource[] sources, KeyGroup<string, string> keywords)
            {
                ShaderVariant variant = new ShaderVariant(keywords, CreateVertexFragment(sources[0].SourceCode, sources[1].SourceCode));

                variant.VertexInputs.AddRange(Inputs);
                variant.ResourceSets.AddRange(Resources);

                return variant;
            }

            public static Veldrid.Shader[] CreateVertexFragment(string vert, string frag)
            {
                CrossCompileOptions options = new()
                {
                    FixClipSpaceZ = (Graphics.Device.BackendType == GraphicsBackend.OpenGL || Graphics.Device.BackendType == GraphicsBackend.OpenGLES) && !Graphics.Device.IsDepthRangeZeroToOne,
                    InvertVertexOutputY = false,
                    Specializations = Graphics.GetSpecializations()
                };

                ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, UTF8.GetBytes(vert), "main");
                ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, UTF8.GetBytes(frag), "main");

                return Graphics.Factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc, options);
            }
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
