using System;
using System.IO;
using System.Linq;

using Prowl.Vector;

using Prowl.Runtime;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Rendering.Shaders;


using ParsedProperty = Prowl.Graphite.ShaderDef.ShaderProperty;
using ShaderProperty = Prowl.Runtime.Rendering.Shaders.ShaderProperty;
using ShaderPropertyType = Prowl.Graphite.ShaderDef.ShaderPropertyType;
using System.Threading.Tasks;
using Prowl.Graphite.Compiler;
using Prowl.Editor.Projects;
using Prowl.Graphite;


namespace Prowl.Editor.Importers;


[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 2;


    public override bool Import(ImportContext ctx)
    {
        string source = File.ReadAllText(ctx.AbsolutePath);

        Shader? shader = LoadShader(source, ctx.AbsolutePath);

        if (shader == null && !IsFallbackShader(ctx.AbsolutePath))
        {
            Debug.LogError($"Shader '{Path.GetFileName(ctx.AbsolutePath)}' failed to compile; substituting the fallback shader.");
            shader = LoadFallback(ctx.AbsolutePath);
        }

        if (shader != null)
            ctx.SetMainAsset(shader);

        return shader != null;
    }


    private static bool IsFallbackShader(string path)
        => string.Equals(Path.GetFileNameWithoutExtension(path), nameof(DefaultShader.Invalid), StringComparison.OrdinalIgnoreCase);


    private static Shader? LoadFallback(string path)
    {
        try
        {
            string source = Runtime.Resources.EmbeddedResources.ReadAllText("Assets/Defaults/Invalid.shader");
            return LoadShader(source, path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load fallback shader: {ex.Message}");
            return null;
        }
    }


    public static Shader? LoadShader(string source, string path)
    {
        string assetsRoot = Path.GetFullPath(Project.Current.AssetsPath);
        string relative = Path.GetRelativePath(assetsRoot, path);
        ParsedShader? parsed = null;
        try
        {
            parsed = ParsedShader.Parse(source);

            ShaderProperty[] properties = [.. parsed!.Properties.Select(ConvertProperty)];

            ShaderPass[] passes = Task
                .WhenAll(parsed.Passes.Select((x) => CompilePass(x, parsed.Name, relative)))
                .GetAwaiter()
                .GetResult();

            ShaderPass? firstFail = passes.FirstOrDefault(x => !x.Variants.Any(), null);

            if (firstFail != null)
            {
                Debug.LogError($"Pass '{firstFail.Name}' failed to compile");
                return null;
            }

            return new Shader(parsed.Name, properties, passes, parsed.Fallback);
        }
        catch (ParseException parseEx)
        {
            DebugStackFrame frame = new(path, parseEx.Line, parseEx.Column);
            Debug.Log(parseEx.Message, LogSeverity.Error, new(frame));
            return null;
        }
    }


    private static async Task<ShaderPass> CompilePass(ParsedPass parsed, string name, string path)
    {
        CompilationRequest request = new()
        {
            ModuleName = name,
            ModulePath = path,
            SourceUtf8 = System.Text.Encoding.UTF8.GetBytes(parsed.InlineSlang),
            Type = ShaderType.Rasterization
        };

        CompilationResult result = await CompilationWorker.CompileAsync(request);

        if (result.CompiledVariants == null || result.CompiledVariants.Length == 0)
            return new ShaderPass(parsed.Name, [], []);

        ShaderVariant[] variants = [.. result.CompiledVariants.Select(x => ConvertVariant(x, parsed))];

        return new ShaderPass(parsed.Name, parsed.Tags, variants);
    }


    private static ShaderVariant ConvertVariant(VariantResult result, ParsedPass pass)
    {
        (ShaderDescription, GraphicsBackend)[] variantBackends = new (ShaderDescription, GraphicsBackend)[result.Backends.Length];
        for (int i = 0; i < result.Backends.Length; i++)
        {
            (ShaderDescription desc, GraphicsBackend back) = result.Backends[i];
            desc.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
            desc.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual);
            desc.RasterizerState = pass.State.ToRasterizerState(RasterizerStateDescription.Default);

            variantBackends[i] = (desc, back);
        }

        ShaderVariant variant = new()
        {
            Backends = variantBackends,
            Keywords = result.Variants
        };

        return variant;
    }


    private static ShaderProperty ConvertProperty(ParsedProperty parsed)
    {
        ShaderProperty prop = parsed.PropertyType switch
        {
            ShaderPropertyType.Float => (float)parsed.Value.R,
            ShaderPropertyType.Integer => (int)parsed.Value.R,
            ShaderPropertyType.Color => new Color(parsed.Value.R, parsed.Value.G, parsed.Value.B, parsed.Value.A),
            ShaderPropertyType.Vector => parsed.Value,
            ShaderPropertyType.Matrix => parsed.MatrixValue,
            ShaderPropertyType.Texture2D => Texture2DParse(parsed.TextureValue),
            ShaderPropertyType.Texture3D => Texture3DParse(parsed.TextureValue),
            ShaderPropertyType.Texture2DArray => throw new ParseException("Texture2DArray does not currently have any loadable defaults", 0, 0),
            ShaderPropertyType.TextureCubemap => throw new ParseException("TextureCubemap does not currently have any loadable defaults", 0, 0),
            ShaderPropertyType.TextureCubemapArray => throw new ParseException("TextureCubemapArray does not currently have any loadable defaults", 0, 0),
            _ => throw new NotSupportedException($"Format: {parsed.PropertyType} not supported")
        };

        prop.Name = parsed.Name;
        prop.DisplayName = parsed.DisplayName;
        prop.HasRange = false;//parsed.HasRange;
        prop.Range = Float2.One;//parsed.Range;

        return prop;
    }


    private static Texture2D Texture2DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture2D.LoadDefault(DefaultTexture.White),
            "gray" or "grey" => Texture2D.LoadDefault(DefaultTexture.Gray18),
            "grid" => Texture2D.LoadDefault(DefaultTexture.Grid),
            "black" or "emission" => Texture2D.LoadDefault(DefaultTexture.Emission),
            "normal" => Texture2D.LoadDefault(DefaultTexture.Normal),
            "surface" => Texture2D.LoadDefault(DefaultTexture.Surface),
            "noise" => Texture2D.LoadDefault(DefaultTexture.Noise),
            _ => throw new ParseException($"Unknown Texture2D default: {texture}", 0, 0)
        };
    }


    private static Texture3D Texture3DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture3D.White,
            _ => throw new ParseException($"Unknown Texture3D default: {texture}", 0, 0)
        };
    }
}