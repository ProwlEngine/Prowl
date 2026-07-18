using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Vector;

using Prowl.Runtime;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Runtime.Resources;

using ParsedProperty = Prowl.Graphite.ShaderDef.ShaderProperty;
using ShaderProperty = Prowl.Runtime.Rendering.Shaders.ShaderProperty;
using ShaderPropertyType = Prowl.Graphite.ShaderDef.ShaderPropertyType;


namespace Prowl.Editor.Importers;


[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 4; // Bumped: on-demand compilation setting


    public override bool Import(ImportContext ctx)
    {
        string source = File.ReadAllText(ctx.AbsolutePath);

        // Settings are guaranteed to have defaults merged by EditorAssetDatabase.RunImport
        bool onDemand = ctx.Settings?.TryGet("onDemandCompilation", out EchoObject? onDemandTag) == true && onDemandTag.BoolValue;

        Shader? shader = LoadShader(source, ctx.AbsolutePath, onDemand);

        if (shader == null && !IsFallbackShader(ctx.AbsolutePath))
        {
            Debug.LogError($"Shader '{Path.GetFileName(ctx.AbsolutePath)}' failed to compile; substituting the fallback shader.");
            shader = LoadFallback(ctx.AbsolutePath);
        }

        if (shader != null)
            ctx.SetMainAsset(shader);

        return shader != null;
    }


    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["onDemandCompilation"] = new EchoObject(false);
        return s;
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


    public static Shader? LoadShader(string source, string path, bool onDemand = false)
    {
        try
        {
            ShaderDefinition definition = ShaderParser.Parse(source);

            ShaderProperty[] properties = [.. (definition.Properties ?? []).Select(ConvertProperty)];

            ShaderSnapshot snapshot = CompilationWorker.CompileAll(definition, definition.Name ?? Path.GetFileNameWithoutExtension(path), path, onDemand);

            // On-demand shaders legitimately bake zero variants at import - whatever's requested first
            // compiles then, through Shader.EditorCompiler. Only eager (CompileMode.All) imports use an
            // empty snapshot as a signal that the shader is genuinely broken.
            if (!onDemand)
            {
                bool anyCompiled = false;
                foreach (PassSnapshot passSnapshot in snapshot.Passes ?? [])
                    anyCompiled |= passSnapshot.Variants is { Length: > 0 };

                if (!anyCompiled)
                {
                    Debug.LogError($"Shader '{definition.Name}' produced no compiled variants.");
                    return null;
                }
            }

            return new Shader(definition.Name ?? Path.GetFileNameWithoutExtension(path), properties, definition, snapshot);
        }
        catch (ParseException parseEx)
        {
            DebugStackFrame frame = new(path, parseEx.Line, parseEx.Column);
            Debug.Log(parseEx.Message, LogSeverity.Error, new(frame));
            return null;
        }
        catch (Exception)
        {
            // Compile failures are already logged by CompilationWorker with source-mapped diagnostics.
            return null;
        }
    }


    private static ShaderProperty ConvertProperty(ParsedProperty parsed)
    {
        ShaderProperty prop = parsed.PropertyType switch
        {
            ShaderPropertyType.Float => (float)parsed.Value.X,
            ShaderPropertyType.Integer => (int)parsed.Value.X,
            ShaderPropertyType.Color => new Color(parsed.Value.X, parsed.Value.Y, parsed.Value.Z, parsed.Value.W),
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
        prop.HasRange = false;
        prop.Range = Float2.One;

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
