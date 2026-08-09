using System;
using System.IO;
using System.Linq;

using Prowl.Echo;

using Prowl.Runtime;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Runtime.Resources;

using ShaderProperty = Prowl.Runtime.Rendering.Shaders.ShaderProperty;


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

            ShaderProperty[] properties = [.. (definition.Properties ?? []).Select(Prowl.Runtime.Rendering.Shaders.ShaderPropertyConverter.Convert)];

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
        catch (ArgumentException argEx)
        {
            // Thrown by ShaderPropertyConverter for an unresolvable property default.
            Debug.LogError($"Shader '{Path.GetFileName(path)}': {argEx.Message}");
            return null;
        }
        catch (Exception)
        {
            // Compile failures are already logged by CompilationWorker with source-mapped diagnostics.
            return null;
        }
    }
}
