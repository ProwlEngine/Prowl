// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Slang;

namespace Quill.ShaderGen;

/// <summary>
/// Compiles Quill/Shaders/Canvas.slang once and writes a self-contained shader source into every
/// backend sample. Run automatically by Quill's build; the generated files are checked in so the
/// samples build without the Slang toolchain present.
/// </summary>
public static class Program
{
    // Names the backends bind by, paired with the GLSL type the downgrade should declare them as.
    private static readonly ShaderUniform[] Uniforms =
    [
        new("projection", "mat4", "float4x4"),
        new("scissorTransform", "vec4", "float4"),
        new("scissorTranslation", "vec2", "float2"),
        new("scissorExt", "vec2", "float2"),
        new("brushTransform", "vec4", "float4"),
        new("brushTranslation", "vec2", "float2"),
        new("brushType", "int", "int"),
        new("brushColor1", "vec4", "float4"),
        new("brushColor2", "vec4", "float4"),
        new("brushParams", "vec4", "float4"),
        new("brushParams2", "vec2", "float2"),
        new("textureTransform", "vec4", "float4"),
        new("textureTranslation", "vec2", "float2"),
        new("atlasTexelSize", "vec2", "float2"),
        new("sdfPxRange", "float", "float"),
        new("viewportSize", "vec2", "float2"),
        new("backdropBlurAmount", "float", "float"),
        new("backdropFlipY", "int", "int"),
    ];

    // Varyings the canvas stages exchange, in the order the Raylib vertex glue expects them.
    private static readonly string[] CanvasVaryings = ["fragTexCoord", "fragColor", "fragPos"];
    private static readonly string[] BlurVaryings = ["vUV"];

    private static readonly string[] CanvasEntryPoints = ["Vertex", "Fragment"];
    private static readonly string[] BlurEntryPoints = ["BlurVertex", "BlurDownsample", "BlurUpsample"];

    // The blur pass declares its own small set.
    private static readonly ShaderUniform[] BlurUniforms =
    [
        new("halfpixel", "vec2", "float2"),
        new("offset", "float", "float"),
    ];

    public static int Main(string[] args)
    {
        try
        {
            string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();
            string shaderDir = Path.Combine(repoRoot, "Quill", "Shaders");

            Console.WriteLine($"[quill-shadergen] source: {shaderDir}");

            SlangCompile canvas = new(shaderDir, "Canvas");
            SlangCompile blur = new(shaderDir, "Blur");

            string vert330 = Glsl(canvas, CanvasEntryPoints, "Vertex", GlslDialect.Gl330, Uniforms, "vertex", CanvasVaryings);
            string frag330 = Glsl(canvas, CanvasEntryPoints, "Fragment", GlslDialect.Gl330, Uniforms, "fragment", CanvasVaryings);
            string vertEs = Glsl(canvas, CanvasEntryPoints, "Vertex", GlslDialect.Es300, Uniforms, "vertex", CanvasVaryings);
            string fragEs = Glsl(canvas, CanvasEntryPoints, "Fragment", GlslDialect.Es300, Uniforms, "fragment", CanvasVaryings);

            string blurVert330 = Glsl(blur, BlurEntryPoints, "BlurVertex", GlslDialect.Gl330, BlurUniforms, "vertex", BlurVaryings);
            string blurDown330 = Glsl(blur, BlurEntryPoints, "BlurDownsample", GlslDialect.Gl330, BlurUniforms, "fragment", BlurVaryings);
            string blurUp330 = Glsl(blur, BlurEntryPoints, "BlurUpsample", GlslDialect.Gl330, BlurUniforms, "fragment", BlurVaryings);

            string raylibVert330 = RaylibVertex.Build(GlslDialect.Gl330, CanvasVaryings);

            // SFML runs on a pre-130 GLSL pipeline, so it takes a legacy fragment stage and its own
            // fixed-function vertex glue. Its blur passes go through sf::Shader on a drawn quad, so
            // they read SFML's gl_TexCoord rather than the generated fullscreen triangle.
            string sfmlFrag = Glsl(canvas, CanvasEntryPoints, "Fragment", GlslDialect.Legacy, Uniforms, "fragment", CanvasVaryings);
            string sfmlVert = SfmlVertex.Build(CanvasVaryings);
            string sfmlBlurDown = Glsl(blur, BlurEntryPoints, "BlurDownsample", GlslDialect.Legacy, BlurUniforms, "fragment", BlurVaryings)
                .Replace("varying vec2 vary_vUV;", "").Replace("vary_vUV", "gl_TexCoord[0].xy").Replace("src", "texture");
            string sfmlBlurUp = Glsl(blur, BlurEntryPoints, "BlurUpsample", GlslDialect.Legacy, BlurUniforms, "fragment", BlurVaryings)
                .Replace("varying vec2 vary_vUV;", "").Replace("vary_vUV", "gl_TexCoord[0].xy").Replace("src", "texture");

            // Raylib runs the blur through DrawTexturePro, so those passes use rlgl's own vertex
            // shader and its fragTexCoord varying rather than the generated fullscreen triangle.
            string raylibBlurDown = blurDown330.Replace("vary_vUV", "fragTexCoord");
            string raylibBlurUp = blurUp330.Replace("vary_vUV", "fragTexCoord");

            // Quill is the shader's home, but Paper and Origami consume the same canvas and used to
            // keep their own copies. Everything downstream is generated from here too. Paths are
            // relative to the directory holding all three, and a target whose project is missing is
            // skipped so a standalone Quill checkout still generates cleanly.
            string root = Path.GetFullPath(Path.Combine(repoRoot, ".."));

            int written = 0;

            foreach ((string dir, string ns) in new[]
            {
                (Path.Combine("Quill", "Samples", "OpenTKExample"), "OpenTKExample"),
                (Path.Combine("Quill", "Samples", "SilkNETExample"), "SilkExample"),
                (Path.Combine("Paper", "Samples", "OpenTK"), "OpenTKSample"),
                (Path.Combine("Origami", "Samples", "OpenTK"), "OpenTKSample"),
            })
            {
                written += WriteCSharp(Target(root, dir, "Shaders", "CanvasShaders.generated.cs"), ns,
                    vert330, frag330, blurVert330, blurDown330, blurUp330);
            }

            foreach ((string dir, string ns) in new[]
            {
                (Path.Combine("Quill", "Samples", "RaylibExample"), "RaylibExample"),
                (Path.Combine("Paper", "Samples", "RaylibSample"), "RaylibSample"),
            })
            {
                written += WriteCSharp(Target(root, dir, "Shaders", "CanvasShaders.generated.cs"), ns,
                    raylibVert330, frag330, blurVert330, raylibBlurDown, raylibBlurUp);
            }

            foreach (string dir in new[]
            {
                Path.Combine("Quill", "Samples", "WasmExample"),
                Path.Combine("Paper", "Samples", "WasmExample"),
            })
            {
                written += WriteJavaScript(Target(root, dir, "canvasShaders.generated.js"), vertEs, fragEs);
            }

            written += WriteCSharp(Target(root, Path.Combine("Quill", "Samples", "SFMLExample"), "Shaders", "CanvasShaders.generated.cs"), "SFMLExample",
                sfmlVert, sfmlFrag, "", sfmlBlurDown, sfmlBlurUp, SfmlBanner);

            foreach (string dir in new[]
            {
                Path.Combine("Quill", "Samples", "GraphiteExample"),
                Path.Combine("Paper", "Samples", "GraphiteSample"),
            })
            {
                written += WriteGraphite(Target(root, dir, "Shaders", "Shader.shader"), Path.Combine(shaderDir, "Canvas.slang"));
                written += WriteGraphite(Target(root, dir, "Shaders", "Blur.shader"), Path.Combine(shaderDir, "Blur.slang"), blurVariant: true);
            }

            written += WriteUnity(Target(root, Path.Combine("Quill", "Samples", "UnityExample"), "Assets", "Quill", "QuillShader.shader"),
                canvas.Emit(CompileTarget.Hlsl, "sm_5_0", CanvasEntryPoints, "Vertex"),
                canvas.Emit(CompileTarget.Hlsl, "sm_5_0", CanvasEntryPoints, "Fragment"));

            // Stamp the run so the build can skip it when the Slang source has not moved. The
            // generated files themselves are unsuitable as a build output because an unchanged run
            // deliberately leaves them untouched.
            File.WriteAllText(Path.Combine(shaderDir, ".shadergen.stamp"), DateTime.UtcNow.ToString("O"));

            Console.WriteLine($"[quill-shadergen] {written} file(s) updated");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[quill-shadergen] FAILED: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Builds an output path, or null when the consuming project is not present in this checkout.
    /// </summary>
    private static string? Target(string root, string projectDir, params string[] rest)
    {
        string full = Path.Combine(root, projectDir);
        if (!Directory.Exists(full))
            return null;

        return Path.Combine(new[] { full }.Concat(rest).ToArray());
    }

    private static string Glsl(SlangCompile compile, string[] entryPoints, string entry, GlslDialect dialect,
                               ShaderUniform[] uniforms, string stage, string[] varyings)
        => GlslDowngrade.Run(compile.Emit(CompileTarget.Glsl, "glsl_450", entryPoints, entry), dialect, uniforms, stage, varyings);

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Prowl.Quill.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("could not locate the Quill root (Prowl.Quill.slnx)");
    }

    private const string Banner = """
        // <auto-generated> Generated from Quill/Shaders/Canvas.slang by Quill.ShaderGen. Do not edit.
        //
        // Matrix convention: Slang emits mul(M, v) as v * M, so every mat4 uniform here must be
        // uploaded transposed (glUniformMatrix4fv with transpose = true) relative to the matrix the
        // canvas hands the backend. Same bytes as before, flipped flag.
        """;

    private const string SfmlBanner = """
        // <auto-generated> Generated from Quill/Shaders/Canvas.slang by Quill.ShaderGen. Do not edit.
        //
        // Legacy GLSL: SFML binds through its own fixed-function vertex pipeline, so the fragment
        // stage uses varying and gl_FragColor and there is no version directive. Unlike the GL
        // backends nothing is transposed here, because the vertex glue writes projection * gl_Vertex.
        """;

    private static int WriteCSharp(string? path, string ns, string vertex, string fragment,
                                   string blurVertex, string blurDown, string blurUp, string? banner = null)
    {
        StringBuilder sb = new();
        sb.AppendLine(banner ?? Banner);
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("internal static class CanvasShaders");
        sb.AppendLine("{");
        Literal(sb, "Vertex", vertex);
        Literal(sb, "Fragment", fragment);
        Literal(sb, "BlurVertex", blurVertex);
        Literal(sb, "BlurDownsample", blurDown);
        Literal(sb, "BlurUpsample", blurUp);
        sb.AppendLine("}");

        return WriteIfChanged(path, sb.ToString());
    }

    private static void Literal(StringBuilder sb, string name, string source)
    {
        sb.AppendLine($"    public const string {name} = @\"" + source.Replace("\"", "\"\"") + "\";");
        sb.AppendLine();
    }

    /// <summary>
    /// Wraps Slang's HLSL in the ShaderLab scaffolding Unity needs. The shading itself is the same
    /// Canvas.slang everything else is built from, which is what closes the drift this backend had:
    /// it was sampling the glyph atlas as plain coverage while Scribe emits a distance field.
    /// </summary>
    private static int WriteUnity(string? path, string vertexHlsl, string fragmentHlsl)
    {
        StringBuilder sb = new();
        sb.AppendLine("""
            // <auto-generated> Generated from Quill/Shaders/Canvas.slang by Quill.ShaderGen. Do not edit.
            //
            // Matrix convention: unlike the GLSL backends, nothing here needs transposing. Slang emits
            // mul(M, v) as v * M for HLSL too, but it also emits #pragma pack_matrix(row_major), which
            // the GLSL path loses when its uniform block is unpacked. The two cancel out.
            """);
        sb.AppendLine("""
            Shader "Quill/CanvasShader"
            {
                Properties
                {
                    texture0 ("Texture", 2D) = "white" {}
                    fontTexture ("Font Atlas", 2D) = "white" {}
                    backdropTexture ("Backdrop", 2D) = "black" {}
                }
                SubShader
                {
                    Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
                    LOD 100

                    Lighting Off
                    Cull Off
                    ZWrite On
                    ZTest Always
                    Blend One OneMinusSrcAlpha  // Quill emits premultiplied colours

                    Pass
                    {
                        Name "QUILL BUILTIN"

                        HLSLPROGRAM
                        #pragma target 4.5
                        #pragma vertex Vertex
                        #pragma fragment Fragment
            """);

        string vs = HlslFixup.ForUnity(vertexHlsl, Uniforms);
        string fs = HlslFixup.ForUnity(fragmentHlsl, Uniforms);

        sb.AppendLine(Indent(vs, "            "));
        sb.AppendLine(Indent(StripDuplicateDeclarations(fs, vs), "            "));

        sb.AppendLine("""
                        ENDHLSL
                    }
                }
            }
            """);

        return WriteIfChanged(path, sb.ToString());
    }

    /// <summary>
    /// Both HLSL entry points are emitted as standalone translation units, so the shared uniform and
    /// struct declarations appear in each. Unity compiles them as one unit, so the repeats are dropped.
    /// </summary>
    private static string StripDuplicateDeclarations(string fragment, string vertex)
    {
        HashSet<string> seen = new(vertex.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()));
        List<string> kept = [];
        bool inBlock = false;

        foreach (string line in fragment.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();

            // Struct and cbuffer bodies are kept or dropped whole, on the strength of their header.
            if (!inBlock && (t.StartsWith("struct ") || t.StartsWith("cbuffer ")))
            {
                if (seen.Contains(t)) { inBlock = true; continue; }
            }
            if (inBlock)
            {
                if (t.StartsWith("}")) inBlock = false;
                continue;
            }

            if (t.Length > 0 && seen.Contains(t) && (t.StartsWith("Texture2D") || t.StartsWith("SamplerState") || t.StartsWith("uniform ")))
                continue;

            kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    private static string Indent(string text, string pad)
        => string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.Length == 0 ? l : pad + l));

    /// <summary>
    /// Graphite consumes Slang directly, so its shader is the canonical source wrapped in the
    /// ShaderDefinition scaffolding rather than a translation of it.
    /// </summary>
    private static int WriteGraphite(string? path, string slangSourcePath, bool blurVariant = false)
    {
        string body = File.ReadAllText(slangSourcePath).Replace("\r\n", "\n");

        // The module declaration belongs to standalone compilation, not to an inlined pass.
        body = string.Join("\n", body.Split('\n').Where(l => !l.TrimStart().StartsWith("module ")));

        // Graphite drives the blur by keyword off a single Fragment entry point, whereas the GLSL
        // backends need the two directions as separate entry points. Same maths either way; this
        // adds the variant axis and a dispatcher on top rather than forking the source.
        if (blurVariant)
        {
            body = body.Replace("BlurVertex(", "Vertex(");
            body = "import VariantAttributes;\n\n[VariantAxis]\nextern static const bool Upsample;\n" + body + """


                [shader("fragment")]
                float4 Fragment(BlurVaryings input) : SV_Target
                {
                    static if (Upsample)
                        return BlurUpsample(input);

                    return BlurDownsample(input);
                }
                """;

            // The direction-specific entry points become plain functions the dispatcher calls.
            body = body.Replace("[shader(\"fragment\")]\nfloat4 BlurDownsample", "float4 BlurDownsample");
            body = body.Replace("[shader(\"fragment\")]\nfloat4 BlurUpsample", "float4 BlurUpsample");
        }

        string name = Path.GetFileNameWithoutExtension(slangSourcePath);

        StringBuilder sb = new();
        sb.AppendLine(Banner);
        sb.AppendLine($$"""
            Shader "Quill/{{name}}"
            {
                Pass
                {
                    Name "{{name}}"

                    Cull Off
                    ZTest Disabled
                    Blend One InverseSourceAlpha

                    SLANGPROGRAM
            """);
        sb.AppendLine(Indent(body, "        "));
        sb.AppendLine("""
                    ENDSLANG
                }
            }
            """);

        return WriteIfChanged(path, sb.ToString());
    }

    private static int WriteJavaScript(string? path, string vertex, string fragment)
    {
        StringBuilder sb = new();
        sb.AppendLine(Banner);
        sb.AppendLine();
        sb.AppendLine("export const CANVAS_VERTEX_SHADER = `" + vertex.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${") + "`;");
        sb.AppendLine();
        sb.AppendLine("export const CANVAS_FRAGMENT_SHADER = `" + fragment.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${") + "`;");

        return WriteIfChanged(path, sb.ToString());
    }

    private static int WriteIfChanged(string? path, string content)
    {
        if (path == null)
            return 0;

        content = content.Replace("\r\n", "\n");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == content)
            return 0;

        File.WriteAllText(path, content);
        Console.WriteLine($"[quill-shadergen]   wrote {path}");
        return 1;
    }
}

/// <summary>Loads a Slang module once and emits an entry point for a given target.</summary>
internal sealed class SlangCompile(string searchPath, string moduleName)
{
    private sealed class Files(string root) : IFileProvider
    {
        public Memory<byte>? LoadFile(string path)
        {
            string full = Path.Combine(root, path);
            if (File.Exists(full)) return File.ReadAllBytes(full);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }

    /// <summary>
    /// Emits one entry point. All entry points are linked together every time so their shared
    /// varying and uniform layout stays consistent; <paramref name="entryPoints"/> fixes the order.
    /// </summary>
    public string Emit(CompileTarget target, string profile, string[] entryPoints, string wanted)
    {
        ProfileID id = GlobalSession.FindProfile(profile);
        if (id == ProfileID.Unknown)
            throw new InvalidOperationException($"Slang does not know the profile '{profile}'");

        TargetDescription targetDesc = new() { Format = target, Profile = id };
        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
            SearchPaths = [searchPath],
            FileProvider = new Files(searchPath),
        };

        Session session = GlobalSession.CreateSession(sessionDesc);
        Module module = session.LoadModule(moduleName, out DiagnosticInfo diag);
        Check(diag);

        List<ComponentType> parts = [module];
        foreach (string name in entryPoints)
            parts.Add(module.FindEntryPointByName(name));

        ComponentType composite = session.CreateCompositeComponentType([.. parts], out diag);
        Check(diag);

        ComponentType linked = composite.Link(out diag);
        Check(diag);

        int index = Array.IndexOf(entryPoints, wanted);
        if (index < 0)
            throw new InvalidOperationException($"'{wanted}' is not one of the declared entry points");

        Memory<byte> code = linked.GetEntryPointCode(index, 0, out diag);
        Check(diag);

        return Encoding.UTF8.GetString(code.Span);
    }

    private static void Check(DiagnosticInfo diag)
    {
        if (string.IsNullOrWhiteSpace(diag.Message))
            return;

        Console.WriteLine("[slang] " + diag.Message.Trim());

        foreach (Diagnostic d in diag.GetDiagnostics())
        {
            if (d.Severity == Severity.Error)
                throw new InvalidOperationException("Slang compilation failed: " + d.Message);
        }
    }
}
