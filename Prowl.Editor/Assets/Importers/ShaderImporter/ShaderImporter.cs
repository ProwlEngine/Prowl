using Prowl.Editor.ShaderParser;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Text.RegularExpressions;
using Veldrid;
using Veldrid.SPIRV;
using System.Reflection;


using static System.Text.Encoding;
using System.Text;

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

            ctx.SetMainObject(CreateShader(shaderScript));
        }

        public static Runtime.Shader CreateShader(string shaderScript)
        {
            shaderScript = ClearAllComments(shaderScript);

            ParsedShader parsed = new ShaderParser.ShaderParser(shaderScript).Parse();

            List<ShaderPass> passes = new List<ShaderPass>();

            foreach (var parsedPass in parsed.Passes)
            {
                ShaderPassDescription passDesc = new();

                passDesc.Tags = parsedPass.Tags;
                passDesc.BlendState = new BlendStateDescription(RgbaFloat.White, parsedPass.Blend ?? BlendAttachmentDescription.OverrideBlend);
                passDesc.CullingMode = parsedPass.Cull;
                passDesc.DepthClipEnabled = true;
                passDesc.Keywords = parsedPass.Keywords;
                passDesc.DepthStencilState = parsedPass.Stencil ?? DepthStencilStateDescription.DepthOnlyLessEqual;

                var compiler = new ImporterVariantCompiler()
                {   
                    Inputs = parsedPass.Inputs?.Inputs ?? [],
                    Resources = parsedPass.Inputs?.Resources.ToArray() ?? [ [ ] ],
                    Global = parsed.Global
                };

                ShaderPass pass = new ShaderPass(parsedPass.Name, parsedPass.Programs.ToArray(), passDesc, compiler);

                passes.Add(pass);
            }

            return new Runtime.Shader(parsed.Name, parsed.Properties.ToArray(), passes.ToArray()); 
        }

        private class ImporterVariantCompiler : IVariantCompiler
        {
            public MeshResource[] Inputs;
            public ShaderResource[][] Resources;
            public ParsedGlobalState Global;

            public ShaderVariant CompileVariant(ShaderSource[] sources, KeywordState keywords)
            {
                if (sources.Length != 2 || sources[0].Stage != ShaderStages.Vertex || sources[1].Stage != ShaderStages.Fragment)
                    throw new Exception("Shader compiler does not currently support shader stages other than Vertex or Fragment");

                Debug.Log("Compiling for keywords: " + keywords.ToString());

                StringBuilder vertexSource = new(sources[0].SourceCode);
                StringBuilder fragmentSource = new(sources[1].SourceCode);

                if (Global != null)
                {
                    vertexSource.Insert(0, Global.GlobalInclude);
                    fragmentSource.Insert(0, Global.GlobalInclude);
                }

                foreach (var keyword in keywords.KeyValuePairs)
                {
                    string define = $"#define {keyword.Key} {keyword.Value}\n";
                    vertexSource.Insert(0, define);
                    fragmentSource.Insert(0, define);
                }

                vertexSource.Insert(0, "#version 450\n\n");
                fragmentSource.Insert(0, "#version 450\n\n");

                string vertString = vertexSource.ToString();
                string fragString = fragmentSource.ToString();

                Debug.Log(vertString);
                Debug.Log(fragString);

                (GraphicsBackend, ShaderDescription[])[] shaders = 
                [
                    (GraphicsBackend.Vulkan, CreateVertexFragment(vertString, fragString, GraphicsBackend.Vulkan)),
                    (GraphicsBackend.OpenGL, CreateVertexFragment(vertString, fragString, GraphicsBackend.OpenGL)),
                    (GraphicsBackend.OpenGLES, CreateVertexFragment(vertString, fragString, GraphicsBackend.OpenGLES)),
                    (GraphicsBackend.Metal, CreateVertexFragment(vertString, fragString, GraphicsBackend.Metal)),
                    (GraphicsBackend.Direct3D11, CreateVertexFragment(vertString, fragString, GraphicsBackend.Direct3D11)),
                ];
                
                ShaderVariant variant = new ShaderVariant(
                    keywords, 
                    shaders,
                    Inputs.Select(Mesh.VertexLayoutForResource).ToArray(),
                    Resources
                );

                return variant;
            }

            public static ShaderDescription[] CreateVertexFragment(string vert, string frag, GraphicsBackend backend)
            {
                CrossCompileOptions options = new()
                {
                    FixClipSpaceZ = (backend == GraphicsBackend.OpenGL || backend == GraphicsBackend.OpenGLES) && !Graphics.Device.IsDepthRangeZeroToOne,
                    InvertVertexOutputY = false,
                    Specializations = Graphics.GetSpecializations()
                };

                ShaderDescription vertexShaderDesc = new ShaderDescription(
                    ShaderStages.Vertex, 
                    UTF8.GetBytes(vert),
                    "main"
                );
                
                ShaderDescription fragmentShaderDesc = new ShaderDescription(
                    ShaderStages.Fragment, 
                    UTF8.GetBytes(frag), 
                    "main"
                );

                return SPIRVCompiler.CreateFromSpirv(vertexShaderDesc, vert, fragmentShaderDesc, frag, options, backend);
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
