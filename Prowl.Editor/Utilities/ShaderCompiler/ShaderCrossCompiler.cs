using Prowl.Runtime;
using Veldrid;

using System.Text;

using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using SPIRVCross.NET.GLSL;
using SPIRVCross.NET.MSL;
using SPIRVCross.NET.HLSL;

namespace Prowl.Editor.Utilities
{
    public sealed partial class ShaderCrossCompiler : IVariantCompiler
    {
        [SerializeField]
        public (uint, uint) model;

        [SerializeField]
        public EntryPoint[] entrypoints;

        [SerializeField]
        public string sourceCode;

        public ShaderCrossCompiler(string source, EntryPoint[] entrypoints, (uint, uint) model)
        {
            this.sourceCode = source;
            this.entrypoints = entrypoints;
            this.model = model;
        }

        private static byte[] GetBytes(GraphicsBackend backend, string code)
        {
            return backend switch
            {
                GraphicsBackend.Direct3D11 or 
                GraphicsBackend.OpenGL or 
                GraphicsBackend.OpenGLES 
                    => Encoding.ASCII.GetBytes(code),
                
                GraphicsBackend.Metal 
                    => Encoding.UTF8.GetBytes(code),

                _ => throw new Exception($"Invalid GraphicsBackend: {backend}"),
            };
        }

        private static ShaderType StageToType(ShaderStages stage)
        {
            return stage switch
            {
                ShaderStages.Vertex => ShaderType.Vertex,
                ShaderStages.Geometry => ShaderType.Geometry,
                ShaderStages.TessellationControl => ShaderType.Hull,
                ShaderStages.TessellationEvaluation => ShaderType.Domain,
                ShaderStages.Fragment => ShaderType.Fragment,
                ShaderStages.Compute => ShaderType.Compute,
            };
        }

        public ShaderVariant CompileVariant(KeywordState keywords)
        {
            using Context ctx = new Context();

            ShaderDescription[] compiledSPIRV = new ShaderDescription[entrypoints.Length]; 

            for (int i = 0; i < compiledSPIRV.Length; i++)
            {
                EntryPoint entry = entrypoints[i];

                ShaderType type = StageToType(entry.Stage);

                DirectXShaderCompiler.NET.CompilerOptions options = new(type.ToProfile(6, 0))
                {
                    generateAsSpirV = true,
                    useOpenGLMemoryLayout = true,
                    entryPoint = entry.Name,
                };

                foreach (var keyword in keywords.KeyValuePairs)
                {
                    if (!string.IsNullOrWhiteSpace(keyword.Key) && !string.IsNullOrWhiteSpace(keyword.Value))
                        options.SetMacro(keyword.Key, keyword.Value);
                }

                CompilationResult result = ShaderCompiler.Compile(sourceCode, options, DontInclude);

                if (result.compilationErrors != null)
                {
                    Debug.LogError("Failed to compile shader: ", new Exception(result.compilationErrors));
                    return ShaderVariant.Empty;
                }

                compiledSPIRV[i] = new ShaderDescription(entry.Stage, result.objectBytes, entry.Name);
            }

            using Context context = new Context();

            return new ShaderVariant(keywords, [ (GraphicsBackend.Vulkan, compiledSPIRV) ], [], []);    
        }


        

        // Fills the dictionary with every possible permutation for the given definitions, initializing values with the generator function
        private void GenerateVariants()
        {   
            this.variants = new();

            List<KeyValuePair<string, HashSet<string>>> combinations = keywords.ToList();
            List<KeyValuePair<string, string>> combination = new(combinations.Count);

            void GenerateRecursive(int depth)
            {
                if (depth == combinations.Count) // Reached the end for this permutation, add a result.
                {
                    KeywordState key = new(combination);
                    variants.Add(key, compiler.CompileVariant(key));
 
                    return;
                }

                var pair = combinations[depth];
                foreach (var value in pair.Value) // Go down a level for every value
                {
                    combination.Add(new(pair.Key, value));
                    GenerateRecursive(depth + 1);
                    combination.RemoveAt(combination.Count - 1); // Go up once we're done
                }
            }

            GenerateRecursive(0);
        }


        static string DontInclude(string includeName)
        {
            return $"// Including {includeName}";
        }
    }
}
