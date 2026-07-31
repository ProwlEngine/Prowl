// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;

namespace Quill.ShaderGen;

/// <summary>
/// Rewrites the GLSL Slang emits (always #version 450, uniforms packed into a std140 block, explicit
/// binding qualifiers) into the dialect the OpenGL 3.3 and OpenGL ES 3.0 backends accept.
///
/// Every rewrite here is a construct Slang is known to emit. Anything unrecognised is left alone and
/// then caught by <see cref="Verify"/>, which fails the build rather than shipping a shader that
/// silently lost a uniform.
/// </summary>
internal static class GlslDowngrade
{
    private static readonly Regex BindingQualifier = new(@"^\s*layout\(binding\s*=\s*\d+\)\s*$", RegexOptions.Compiled);
    private static readonly Regex LineDirective = new(@"^\s*#line\b.*$", RegexOptions.Compiled);
    private static readonly Regex SamplerDecl = new(@"^\s*uniform\s+sampler2D\s+(\w+?)_\d+\s*;\s*$", RegexOptions.Compiled);
    private static readonly Regex LocationQualifier = new(@"^\s*layout\(location\s*=\s*\d+\)\s*$", RegexOptions.Compiled);

    public static string Run(string glsl, GlslDialect dialect, IReadOnlyList<ShaderUniform> uniforms, string stage, IReadOnlyList<string> varyings)
    {
        List<string> lines = [.. glsl.Replace("\r\n", "\n").Split('\n')];

        lines = StripNoise(lines, stage);
        lines = RemoveStruct(lines, "GlobalParams_0");
        lines = UnpackUniformBlock(lines, uniforms, glsl, out bool hadBlock);

        string body = string.Join("\n", lines);

        if (hadBlock)
            body = RewriteUniformReferences(body, uniforms);

        body = RewriteSamplerNames(body);
        body = RewriteVaryingNames(body, varyings);
        body = RewriteVertexBuiltins(body);
        body = body.Replace("mat4x4", "mat4");
        body = CollapseBlankLines(body);

        string header = BuildHeader(dialect);
        string result = header + body.TrimStart('\n');

        Verify(result, uniforms, dialect, stage);
        return result;
    }

    private static List<string> StripNoise(List<string> lines, string stage)
    {
        // Which declarations a location qualifier is still legal on. GLSL 330 allows it on vertex
        // attributes and fragment outputs only; on varyings it needs GL 4.1 or
        // GL_ARB_separate_shader_objects, so those get stripped and matched by name instead.
        string keepLocationOn = stage == "vertex" ? "in" : "out";

        List<string> outLines = new(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            if (LineDirective.IsMatch(line)) continue;
            if (BindingQualifier.IsMatch(line)) continue;                 // GL 4.2+, bound by name instead
            if (line.TrimStart().StartsWith("#version")) continue;        // replaced by BuildHeader
            if (line.Trim() == "layout(column_major) buffer;") continue;  // no storage buffers are used
            // Block layout qualifiers are dead once the uniform block is unpacked into loose
            // uniforms, whose layout is decided by the transpose flag at upload instead.
            if (line.Trim() is "layout(column_major) uniform;" or "layout(row_major) uniform;") continue;
            // Needed only for the gl_BaseVertex that RewriteVertexBuiltins folds away.
            if (line.Trim() == "#extension GL_ARB_shader_draw_parameters : require") continue;

            if (LocationQualifier.IsMatch(line))
            {
                string declaration = NextCode(lines, i);
                if (!declaration.StartsWith(keepLocationOn + " "))
                    continue;
            }

            outLines.Add(line);
        }
        return outLines;
    }

    /// <summary>The next line that carries actual code, skipping blanks and dropped directives.</summary>
    private static string NextCode(List<string> lines, int from)
    {
        for (int i = from + 1; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || LineDirective.IsMatch(lines[i])) continue;
            return t;
        }
        return string.Empty;
    }

    /// <summary>
    /// Gives both stages the same name for each varying. Slang names them per entry point
    /// (entryPointParam_Vertex_fragPos_0 out of the vertex stage, input_fragPos_0 into the fragment
    /// stage), which only linked because the location qualifiers matched them up. With those gone the
    /// names have to agree.
    /// </summary>
    /// <summary>
    /// Slang targets Vulkan's vertex builtins. gl_VertexIndex is gl_VertexID under another name, and
    /// gl_BaseVertex needs GL 4.6. Quill only ever issues plain non-indexed, non-base-vertex draws for
    /// the passes generated here, so the base vertex is always zero and the subtraction folds away.
    /// </summary>
    private static string RewriteVertexBuiltins(string body)
    {
        body = Regex.Replace(body, @"\bgl_VertexIndex\s*-\s*gl_BaseVertex\b", "gl_VertexID");
        body = Regex.Replace(body, @"\bgl_VertexIndex\b", "gl_VertexID");
        return body;
    }

    private static string RewriteVaryingNames(string body, IReadOnlyList<string> varyings)
    {
        foreach (string v in varyings.OrderByDescending(v => v.Length))
        {
            body = Regex.Replace(body, $@"\bentryPointParam_\w+?_{Regex.Escape(v)}_\d+\b", "vary_" + v);
            body = Regex.Replace(body, $@"\binput_{Regex.Escape(v)}_\d+\b", "vary_" + v);
        }

        // The fragment stage's return value, which Slang names after the entry point.
        body = Regex.Replace(body, @"\bentryPointParam_\w+_\d+\b", "finalColor");
        return body;
    }

    /// <summary>Drops a struct declaration that becomes dead once its uniform block is unpacked.</summary>
    private static List<string> RemoveStruct(List<string> lines, string name)
    {
        int start = lines.FindIndex(l => l.Trim() == $"struct {name}");
        if (start < 0) return lines;

        int end = lines.FindIndex(start, l => l.Trim() == "};");
        if (end < 0) return lines;

        lines.RemoveRange(start, end - start + 1);
        return lines;
    }

    /// <summary>
    /// Replaces the std140 block Slang wraps global uniforms in with loose uniform declarations, so
    /// backends keep addressing them by name through glGetUniformLocation exactly as they do today.
    /// </summary>
    private static List<string> UnpackUniformBlock(List<string> lines, IReadOnlyList<ShaderUniform> uniforms, string original, out bool hadBlock)
    {
        hadBlock = false;

        int start = lines.FindIndex(l => l.TrimStart().StartsWith("layout(std140) uniform block_"));
        if (start < 0) return lines;

        int end = lines.FindIndex(start, l => l.TrimStart().StartsWith("}") && l.Contains("globalParams"));
        if (end < 0) return lines;

        // Declare only what this stage actually reads. The block lists every uniform regardless, so
        // the test has to be against real references in the body, not against the block itself.
        List<string> declarations = [];
        foreach (ShaderUniform u in uniforms)
        {
            if (Regex.IsMatch(original, $@"globalParams_\d+\.{Regex.Escape(u.Name)}_\d+\b"))
                declarations.Add($"uniform {u.GlslType} {u.Name};");
        }

        lines.RemoveRange(start, end - start + 1);
        lines.InsertRange(start, declarations);

        hadBlock = true;
        return lines;
    }

    private static string RewriteUniformReferences(string body, IReadOnlyList<ShaderUniform> uniforms)
    {
        // Longest first so brushParams2_0 is never partially matched by brushParams_0.
        foreach (ShaderUniform u in uniforms.OrderByDescending(u => u.Name.Length))
            body = Regex.Replace(body, $@"globalParams_\d+\.{Regex.Escape(u.Name)}_\d+\b", u.Name);

        return body;
    }

    private static string RewriteSamplerNames(string body)
    {
        List<string> samplers = [];
        foreach (string line in body.Split('\n'))
        {
            Match m = SamplerDecl.Match(line);
            if (m.Success) samplers.Add(m.Groups[1].Value);
        }

        foreach (string s in samplers.OrderByDescending(s => s.Length))
            body = Regex.Replace(body, $@"\b{Regex.Escape(s)}_\d+\b", s);

        return body;
    }

    private static string BuildHeader(GlslDialect dialect)
    {
        StringBuilder sb = new();
        sb.Append(dialect.VersionDirective).Append('\n');

        // ES has no default float precision in fragment shaders, so one must be declared.
        if (dialect.NeedsPrecision)
        {
            sb.Append("precision highp float;\n");
            sb.Append("precision highp int;\n");
        }

        return sb.ToString();
    }

    private static string CollapseBlankLines(string body)
        => Regex.Replace(body, @"\n{3,}", "\n\n");

    /// <summary>
    /// Fails the build if the downgrade left anything the target dialect cannot compile, or dropped a
    /// uniform the backends bind by name. Cheaper to catch here than as a black screen in a sample.
    /// </summary>
    private static void Verify(string glsl, IReadOnlyList<ShaderUniform> uniforms, GlslDialect dialect, string stage)
    {
        List<string> problems = [];

        if (glsl.Contains("globalParams"))
            problems.Add("a globalParams reference survived the uniform-block unpack");

        if (Regex.IsMatch(glsl, @"layout\(binding"))
            problems.Add($"a binding qualifier survived, which {dialect.Name} does not support");

        if (glsl.Contains("layout(column_major) buffer"))
            problems.Add("a storage-buffer layout qualifier survived");

        if (!glsl.StartsWith(dialect.VersionDirective))
            problems.Add($"expected the source to start with '{dialect.VersionDirective}'");

        // A location qualifier on a varying needs GL 4.1; only attributes and fragment outputs keep one.
        string illegalOn = stage == "vertex" ? "out" : "in";
        if (Regex.IsMatch(glsl, $@"layout\(location\s*=\s*\d+\)\s*\n\s*{illegalOn}\s"))
            problems.Add($"a location qualifier survived on a {stage} varying, which needs GL 4.1");

        if (Regex.IsMatch(glsl, @"\bentryPointParam_\w+\b"))
            problems.Add("a varying kept its per-entry-point name, so the stages will not link");

        if (Regex.IsMatch(glsl, @"\bgl_VertexIndex\b|\bgl_BaseVertex\b|\bgl_InstanceIndex\b"))
            problems.Add("a Vulkan vertex builtin survived, which desktop GLSL does not define");

        if (glsl.Contains("#extension"))
            problems.Add("an extension directive survived; check it is available on the target");

        // Any uniform that is declared must not still carry Slang's mangling suffix.
        foreach (ShaderUniform u in uniforms)
        {
            if (Regex.IsMatch(glsl, $@"\b{Regex.Escape(u.Name)}_\d+\b"))
                problems.Add($"uniform '{u.Name}' is still mangled");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException($"{dialect.Name} {stage} shader failed verification:\n  - " + string.Join("\n  - ", problems));
    }
}

internal sealed record GlslDialect(string Name, string VersionDirective, bool NeedsPrecision)
{
    public static readonly GlslDialect Gl330 = new("GLSL 330", "#version 330", false);
    public static readonly GlslDialect Es300 = new("GLSL ES 300", "#version 300 es", true);
}

internal sealed record ShaderUniform(string Name, string GlslType, string HlslType);
