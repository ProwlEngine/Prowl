// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace Quill.ShaderGen;

/// <summary>
/// Reshapes the HLSL Slang emits into what Unity's material system can bind. Slang targets raw SM5:
/// explicit register assignments, a named cbuffer, and split texture/sampler pairs called
/// <c>texture0_texture_0</c> / <c>texture0_sampler_0</c>. Unity binds by name and expects a texture
/// called <c>texture0</c> alongside a <c>sampler_texture0</c>, with loose material properties rather
/// than a hand-declared constant buffer.
/// </summary>
internal static class HlslFixup
{
    public static string ForUnity(string hlsl, IReadOnlyList<ShaderUniform> uniforms)
    {
        string body = hlsl.Replace("\r\n", "\n");

        body = DropNvapiBlock(body);
        body = RenameTexturesAndSamplers(body);
        body = UnpackConstantBuffer(body, uniforms);
        body = StripRegisters(body);
        body = Regex.Replace(body, @"\n{3,}", "\n\n");

        Verify(body, uniforms);
        return body;
    }

    private static string DropNvapiBlock(string body)
    {
        body = body.Replace("#ifdef SLANG_HLSL_ENABLE_NVAPI\n#include \"nvHLSLExtns.h\"\n#endif\n", "");
        return body;
    }

    private static string RenameTexturesAndSamplers(string body)
    {
        // Collect the base names Slang split into texture/sampler pairs.
        HashSet<string> names = [];
        foreach (Match m in Regex.Matches(body, @"\b(\w+?)_texture_\d+\b"))
            names.Add(m.Groups[1].Value);

        foreach (string n in names.OrderByDescending(n => n.Length))
        {
            body = Regex.Replace(body, $@"\b{Regex.Escape(n)}_texture_\d+\b", n);
            body = Regex.Replace(body, $@"\b{Regex.Escape(n)}_sampler_\d+\b", "sampler_" + n);
        }

        return body;
    }

    /// <summary>
    /// Replaces Slang's cbuffer with loose declarations. Unity packs material properties into its own
    /// constant buffer, so a hand-declared one just gets in the way of binding by name.
    /// </summary>
    private static string UnpackConstantBuffer(string body, IReadOnlyList<ShaderUniform> uniforms)
    {
        Match block = Regex.Match(body, @"cbuffer\s+globalParams_\d+[^\{]*\{(?<members>[^\}]*)\}[^;]*;", RegexOptions.Singleline);
        if (!block.Success)
            return body;

        List<string> declarations = [];
        foreach (ShaderUniform u in uniforms)
        {
            if (Regex.IsMatch(body, $@"globalParams_\d+\.{Regex.Escape(u.Name)}_\d+\b"))
                declarations.Add($"{u.HlslType} {u.Name};");
        }

        body = body.Remove(block.Index, block.Length).Insert(block.Index, string.Join("\n", declarations));

        // The struct the cbuffer mirrored is dead once the members are loose.
        body = Regex.Replace(body, @"struct\s+GlobalParams_\d+\s*\{[^\}]*\}\s*;\s*", "", RegexOptions.Singleline);

        foreach (ShaderUniform u in uniforms.OrderByDescending(u => u.Name.Length))
            body = Regex.Replace(body, $@"globalParams_\d+\.{Regex.Escape(u.Name)}_\d+\b", u.Name);

        return body;
    }

    private static string StripRegisters(string body)
        => Regex.Replace(body, @"\s*:\s*register\([^\)]*\)", "");

    private static void Verify(string body, IReadOnlyList<ShaderUniform> uniforms)
    {
        List<string> problems = [];

        if (body.Contains("globalParams"))
            problems.Add("a globalParams reference survived the constant-buffer unpack");

        if (Regex.IsMatch(body, @"\b\w+_texture_\d+\b") || Regex.IsMatch(body, @"\b\w+_sampler_\d+\b"))
            problems.Add("a split texture/sampler pair kept its Slang name, so Unity cannot bind it");

        if (body.Contains("register("))
            problems.Add("an explicit register assignment survived; Unity assigns its own");

        foreach (ShaderUniform u in uniforms)
        {
            if (Regex.IsMatch(body, $@"\b{Regex.Escape(u.Name)}_\d+\b"))
                problems.Add($"uniform '{u.Name}' is still mangled");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException("Unity HLSL failed verification:\n  - " + string.Join("\n  - ", problems));
    }
}
