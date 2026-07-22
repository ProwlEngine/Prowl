// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Wavefront;

/// <summary>
/// Parses a Wavefront <c>.mtl</c> file into <see cref="IntermediateMaterial"/> entries plus
/// <see cref="IntermediateTexture"/> references. Handles the classic Phong-style keys (Ka/Kd/Ks/Ke/
/// Ns/Ni/d/Tr/illum/map_Kd/map_Ks/...) and the de-facto PBR extensions (Pr/Pm/Ps/Pc/Pcr, map_Pr,
/// map_Pm, map_Ke, norm).
/// </summary>
internal sealed class MtlReader
{
    public Dictionary<string, int> MaterialIndexByName { get; } = new(StringComparer.Ordinal);

    private readonly IntermediateScene _scene;
    private readonly ImportContext _ctx;
    private readonly string? _mtlPath;

    /// <summary>Texture path -&gt; index in <see cref="IntermediateScene.Textures"/> de-dup cache.</summary>
    private readonly Dictionary<string, int> _textureCache = new(StringComparer.OrdinalIgnoreCase);

    public MtlReader(IntermediateScene scene, ImportContext ctx, string? mtlPath)
    {
        _scene = scene;
        _ctx = ctx;
        _mtlPath = mtlPath;
    }

    public void Read(string mtlText)
    {
        IntermediateMaterial? current = null;

        foreach (var rawLine in mtlText.Split('\n'))
        {
            var line = rawLine.AsSpan().TrimEnd();
            if (line.IsEmpty) continue;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash].TrimEnd();
            if (line.IsEmpty) continue;

            var tok = new ObjTokenizer(line);
            var keyword = tok.NextToken();
            if (keyword.IsEmpty) continue;

            if (keyword.SequenceEqual("newmtl"))
            {
                string name = tok.Rest().ToString();
                current = new IntermediateMaterial { Name = name };
                MaterialIndexByName[name] = _scene.Materials.Count;
                _scene.Materials.Add(current);
                continue;
            }
            if (current is null) continue;

            try { ApplyDirective(keyword, ref tok, current); }
            catch (FormatException ex)
            {
                _ctx.Log.Warning($"MTL parse error on '{rawLine.Trim()}': {ex.Message}", "MtlReader");
            }
        }
    }

    private void ApplyDirective(ReadOnlySpan<char> keyword, ref ObjTokenizer tok, IntermediateMaterial mat)
    {
        // Phong-style colors. We treat diffuse as base color, specular as a hint, ignore ambient.
        if (keyword.SequenceEqual("Kd"))
        {
            float r = tok.NextFloat(); float g = tok.NextFloat(); float b = tok.NextFloat();
            mat.BaseColor = new Color(r, g, b, mat.BaseColor.A);
            return;
        }
        if (keyword.SequenceEqual("Ke"))
        {
            mat.EmissiveFactor = new Color(tok.NextFloat(), tok.NextFloat(), tok.NextFloat(), 1f);
            return;
        }
        if (keyword.SequenceEqual("Ka") || keyword.SequenceEqual("Ks") || keyword.SequenceEqual("Tf"))
        {
            // Ambient, specular, transmission filter - consumed but not surfaced.
            return;
        }

        if (keyword.SequenceEqual("Ns"))
        {
            // Specular exponent (Phong). Map heuristically to PBR roughness: high exponent = smooth.
            float ns = tok.NextFloat();
            mat.Roughness = MathF.Sqrt(MathF.Max(2f, 1000f - ns) / 1000f);
            return;
        }
        if (keyword.SequenceEqual("Ni"))
        {
            mat.Ior = new IorExtension { Ior = MathF.Max(1f, tok.NextFloat()) };
            return;
        }
        if (keyword.SequenceEqual("d"))
        {
            float d = tok.NextFloat();
            mat.BaseColor = new Color(mat.BaseColor.R, mat.BaseColor.G, mat.BaseColor.B, d);
            mat.AlphaMode = d < 1f ? MaterialAlphaMode.Blend : MaterialAlphaMode.Opaque;
            return;
        }
        if (keyword.SequenceEqual("Tr"))
        {
            // OBJ Tr is inverted opacity. Per spec, but many tools use it the same way as d.
            float tr = tok.NextFloat();
            float a = 1f - tr;
            mat.BaseColor = new Color(mat.BaseColor.R, mat.BaseColor.G, mat.BaseColor.B, a);
            mat.AlphaMode = a < 1f ? MaterialAlphaMode.Blend : MaterialAlphaMode.Opaque;
            return;
        }
        if (keyword.SequenceEqual("illum"))
        {
            // illum 0 / 1 in classic OBJ means unlit; everything else is some shaded model.
            int illum = tok.NextInt();
            if (illum == 0 || illum == 1) mat.Unlit = true;
            return;
        }

        // PBR extensions.
        if (keyword.SequenceEqual("Pr")) { mat.Roughness = tok.NextFloat(); return; }
        if (keyword.SequenceEqual("Pm")) { mat.Metallic = tok.NextFloat(); return; }
        if (keyword.SequenceEqual("Ps"))
        {
            mat.Sheen = new SheenExtension
            {
                ColorFactor = new Color(tok.NextFloat(), tok.NextFloatOr(0f), tok.NextFloatOr(0f), 1f),
            };
            return;
        }
        if (keyword.SequenceEqual("Pc")) { mat.Clearcoat = (mat.Clearcoat ?? new ClearcoatExtension()) with { Factor = tok.NextFloat() }; return; }
        if (keyword.SequenceEqual("Pcr")) { mat.Clearcoat = (mat.Clearcoat ?? new ClearcoatExtension()) with { Roughness = tok.NextFloat() }; return; }

        // Textures. We accept the trailing "filename" form; option flags (-s, -o, -mm, -bm) are
        // tolerated but only their first numeric arguments are honored for offset/scale where applicable.
        if (keyword.SequenceEqual("map_Kd")) { mat.BaseColorTexture = ReadTextureSlot(ref tok); return; }
        if (keyword.SequenceEqual("map_Ke")) { mat.EmissiveTexture = ReadTextureSlot(ref tok); return; }
        if (keyword.SequenceEqual("map_d")) { ReadTextureSlot(ref tok); return; }                          // opacity in alpha - unused
        if (keyword.SequenceEqual("map_Bump") || keyword.SequenceEqual("bump") || keyword.SequenceEqual("norm"))
        {
            mat.NormalTexture = ReadTextureSlot(ref tok);
            return;
        }
        if (keyword.SequenceEqual("map_Pr")) { mat.MetallicRoughnessTexture = ReadTextureSlot(ref tok); return; }
        if (keyword.SequenceEqual("map_Pm"))
        {
            // Some authoring tools split metallic/roughness; we fall back to using the same slot for both.
            mat.MetallicRoughnessTexture ??= ReadTextureSlot(ref tok);
            return;
        }
        if (keyword.SequenceEqual("map_Ka") || keyword.SequenceEqual("map_Ns") ||
            keyword.SequenceEqual("disp") || keyword.SequenceEqual("decal"))
        {
            ReadTextureSlot(ref tok); // recognized but not surfaced
            return;
        }

        // Unknown keyword: ignore.
    }

    private IntermediateTextureSlot? ReadTextureSlot(ref ObjTokenizer tok)
    {
        // Read tokens, skipping option pairs (-flag value [value...]). The last non-flag token is
        // the filename (which may contain spaces - rare but legal).
        string? path = null;
        while (!tok.AtEnd)
        {
            var t = tok.NextToken();
            if (t.IsEmpty) break;
            if (t[0] == '-')
            {
                // Skip the next 1-3 numeric tokens belonging to this option.
                SkipOptionArgs(t, ref tok);
                continue;
            }
            // Everything from here to end of line is the filename.
            path = (path is null ? t.ToString() : path + " " + t.ToString())
                + (tok.AtEnd ? string.Empty : " " + tok.Rest().ToString());
            break;
        }
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim();

        int textureIndex = ResolveOrCreateTexture(path);
        if (textureIndex < 0) return null;
        return new IntermediateTextureSlot { TextureIndex = textureIndex };
    }

    private static void SkipOptionArgs(ReadOnlySpan<char> option, ref ObjTokenizer tok)
    {
        int args = option.SequenceEqual("-s") || option.SequenceEqual("-o") || option.SequenceEqual("-t") ? 3 :
                   option.SequenceEqual("-mm") || option.SequenceEqual("-bm") ? 1 :
                   option.SequenceEqual("-clamp") || option.SequenceEqual("-blendu") || option.SequenceEqual("-blendv") ? 1 :
                   0;
        for (int i = 0; i < args; i++)
        {
            if (tok.AtEnd) return;
            tok.NextToken();
        }
    }

    private int ResolveOrCreateTexture(string referencedPath)
    {
        if (_textureCache.TryGetValue(referencedPath, out int existing))
            return existing;

        string? resolved = null;
        if (_mtlPath is not null)
            resolved = _ctx.Resolver.Resolve(_mtlPath, referencedPath);
        else if (_ctx.SourcePath is not null)
            resolved = _ctx.Resolver.Resolve(_ctx.SourcePath, referencedPath);

        var tex = new IntermediateTexture
        {
            Name = Path.GetFileNameWithoutExtension(referencedPath),
            SourcePath = resolved,
            MimeType = GuessMime(referencedPath),
        };
        if (resolved is null)
            _ctx.Log.Warning($"Could not resolve MTL texture '{referencedPath}'.", "MtlReader");

        int idx = _scene.Textures.Count;
        _scene.Textures.Add(tex);
        _textureCache[referencedPath] = idx;
        return idx;
    }

    private static string? GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tga" => "image/x-tga",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => null,
        };
}
