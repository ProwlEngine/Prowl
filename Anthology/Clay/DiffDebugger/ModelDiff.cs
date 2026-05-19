using System.Text;
using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;

namespace Prowl.Clay.DiffDebugger;

/// <summary>
/// Loads two models through <see cref="ModelImporter"/> and produces a structural text diff so
/// the same model authored in different formats can be cross-checked. Names are normalized
/// (strip exporter prefixes like <c>mixamorig:</c> and trailing <c>_NN</c> instance numbers) so
/// minor exporter-naming differences don't drown out the actual structural divergences.
/// </summary>
internal static class ModelDiff
{
    public static int Run(string pathA, string pathB)
    {
        string tagA = TagFor(pathA);
        string tagB = TagFor(pathB);

        var settings = ModelImporterSettings.GameQuality with { OnLog = e => Console.WriteLine($"[Clay] {e}") };
        var modelA = ModelImporter.Load(pathA, settings);
        var modelB = ModelImporter.Load(pathB, settings);

        // Dump each model to a normalized text representation. Saving alongside lets external diff
        // tools surface the actual byte-level divergence; the summary on stdout is the quick view.
        string outDir = Path.Combine(Path.GetTempPath(), "prowl_clay_diff");
        Directory.CreateDirectory(outDir);
        string outA = Path.Combine(outDir, $"{SafeFileTag(tagA)}.txt");
        string outB = Path.Combine(outDir, $"{SafeFileTag(tagB)}.txt");
        File.WriteAllText(outA, Serialize(modelA, tagA));
        File.WriteAllText(outB, Serialize(modelB, tagB));
        Console.WriteLine($"Wrote {outA}");
        Console.WriteLine($"Wrote {outB}");

        Console.WriteLine();
        Console.WriteLine("== Header diff ==");
        Console.WriteLine($"  nodes        : {tagA}={modelA.Nodes.Count}  {tagB}={modelB.Nodes.Count}");
        Console.WriteLine($"  meshes       : {tagA}={modelA.Meshes.Count}  {tagB}={modelB.Meshes.Count}");
        Console.WriteLine($"  skins        : {tagA}={modelA.Skins.Count}  {tagB}={modelB.Skins.Count}");
        Console.WriteLine($"  materials    : {tagA}={modelA.Materials.Count}  {tagB}={modelB.Materials.Count}");
        Console.WriteLine($"  animations   : {tagA}={modelA.AnimationClips.Count}  {tagB}={modelB.AnimationClips.Count}");

        Console.WriteLine();
        Console.WriteLine("== Anim channels per bone (normalized name) ==");
        DiffAnimChannels(modelA, modelB, tagA, tagB);

        Console.WriteLine();
        Console.WriteLine("== Skin / inverse-bind translations per bone (normalized name) ==");
        DiffSkins(modelA, modelB, tagA, tagB);

        return 0;
    }

    private static string Serialize(Model model, string tag)
    {
        var sb = new StringBuilder();
        sb.Append("MODEL ").Append(tag).Append('\n');
        sb.Append("  format=").Append(model.Metadata.Format).Append('\n');
        sb.Append("  format_version=").Append(model.Metadata.FormatVersion ?? "?").Append('\n');
        sb.Append("  generator=").Append(model.Metadata.Generator ?? "?").Append('\n');
        sb.Append("  nodes=").Append(model.Nodes.Count).Append('\n');
        sb.Append("  meshes=").Append(model.Meshes.Count).Append('\n');
        sb.Append("  materials=").Append(model.Materials.Count).Append('\n');
        sb.Append("  skins=").Append(model.Skins.Count).Append('\n');
        sb.Append("  animations=").Append(model.AnimationClips.Count).Append('\n');

        sb.Append("\nNODES (depth-first, normalized names)\n");
        SerializeNodeRecursive(model.Root, depth: 0, sb);

        sb.Append("\nSKINS\n");
        for (int s = 0; s < model.Skins.Count; s++)
        {
            var sk = model.Skins[s];
            sb.Append("  skin[").Append(s).Append("] name=").Append(sk.Name).Append(" bones=").Append(sk.BoneNodeIndices.Length).Append(" root=");
            sb.Append(sk.RootNodeIndex >= 0 ? Normalize(model.Nodes[sk.RootNodeIndex].Name) : "<none>").Append('\n');
            for (int b = 0; b < sk.BoneNodeIndices.Length; b++)
            {
                int ni = sk.BoneNodeIndices[b];
                string name = Normalize(model.Nodes[ni].Name);
                var ibm = sk.InverseBindPoses[b];
                sb.Append("    [").Append(b.ToString("D2")).Append("] ").Append(name);
                sb.Append(" ibm.t=").Append(Fmt(new Float3(ibm.c3.X, ibm.c3.Y, ibm.c3.Z)));
                sb.Append(" ibm.basis_det=").Append(Det3x3(ibm).ToString("F3"));
                sb.Append('\n');
            }
        }

        sb.Append("\nMESHES\n");
        for (int m = 0; m < model.Meshes.Count; m++)
        {
            var msh = model.Meshes[m];
            sb.Append("  mesh[").Append(m).Append("] name=").Append(msh.Name).Append(" verts=").Append(msh.VertexCount);
            sb.Append(" indices=").Append(msh.Indices.Length).Append(" submeshes=").Append(msh.SubMeshes.Length);
            sb.Append(" has_uv=").Append(msh.UVs[0] is not null);
            sb.Append(" has_norm=").Append(msh.Normals is not null);
            sb.Append(" has_weights=").Append(msh.BoneWeights is not null);
            sb.Append(" bbox=[");
            sb.Append(Fmt(msh.Bounds.Min)).Append("..").Append(Fmt(msh.Bounds.Max));
            sb.Append("]\n");
        }

        sb.Append("\nANIMATIONS\n");
        for (int a = 0; a < model.AnimationClips.Count; a++)
        {
            var clip = model.AnimationClips[a];
            sb.Append("  anim[").Append(a).Append("] name=").Append(clip.Name).Append(" duration=").Append(clip.Duration.ToString("F2"));
            sb.Append(" bindings=").Append(clip.Bindings.Length).Append('\n');
            // Group bindings by normalized node name + property, count keys.
            var byBone = new Dictionary<string, (int P, int R, int S, int Pkeys, int Rkeys, int Skeys)>();
            foreach (var b in clip.Bindings)
            {
                if (b.NodeIndex < 0 || b.NodeIndex >= model.Nodes.Count) continue;
                string name = Normalize(model.Nodes[b.NodeIndex].Name);
                byBone.TryGetValue(name, out var entry);
                int keys = b.Curve.Times.Length;
                switch (b.Property)
                {
                    case AnimatedProperty.Position: entry = (1, entry.R, entry.S, keys, entry.Rkeys, entry.Skeys); break;
                    case AnimatedProperty.Rotation: entry = (entry.P, 1, entry.S, entry.Pkeys, keys, entry.Skeys); break;
                    case AnimatedProperty.Scale:    entry = (entry.P, entry.R, 1, entry.Pkeys, entry.Rkeys, keys); break;
                }
                byBone[name] = entry;
            }
            foreach (var kv in byBone.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append("    ").Append(kv.Key);
                sb.Append(" P=").Append(kv.Value.P).Append("(").Append(kv.Value.Pkeys).Append(")");
                sb.Append(" R=").Append(kv.Value.R).Append("(").Append(kv.Value.Rkeys).Append(")");
                sb.Append(" S=").Append(kv.Value.S).Append("(").Append(kv.Value.Skeys).Append(")");
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static void SerializeNodeRecursive(ModelNode node, int depth, StringBuilder sb)
    {
        string pad = new(' ', depth * 2);
        sb.Append(pad).Append(Normalize(node.Name));
        sb.Append(" P=").Append(Fmt(node.LocalPosition));
        sb.Append(" R=").Append(Fmt(node.LocalRotation));
        sb.Append(" S=").Append(Fmt(node.LocalScale));
        if (node.MeshIndex >= 0) sb.Append(" mesh=").Append(node.MeshIndex);
        if (node.SkinIndex >= 0) sb.Append(" skin=").Append(node.SkinIndex);
        sb.Append('\n');
        foreach (var c in node.Children)
            SerializeNodeRecursive(c, depth + 1, sb);
    }

    private static void DiffAnimChannels(Model a, Model b, string tagA, string tagB)
    {
        var chansA = BuildChannelMap(a);
        var chansB = BuildChannelMap(b);

        var allBones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in chansA.Keys) allBones.Add(k);
        foreach (var k in chansB.Keys) allBones.Add(k);

        int differ = 0;
        foreach (var name in allBones.OrderBy(s => s, StringComparer.Ordinal))
        {
            chansA.TryGetValue(name, out var fa);
            chansB.TryGetValue(name, out var fb);
            if (fa.P == fb.P && fa.R == fb.R && fa.S == fb.S) continue;
            Console.WriteLine($"  {name,-30} | {tagA}:P={fa.P} R={fa.R} S={fa.S} | {tagB}:P={fb.P} R={fb.R} S={fb.S}");
            differ++;
        }
        if (differ == 0) Console.WriteLine("  (no per-bone channel differences after normalization)");
    }

    private static Dictionary<string, (bool P, bool R, bool S)> BuildChannelMap(Model model)
    {
        var result = new Dictionary<string, (bool, bool, bool)>(StringComparer.Ordinal);
        foreach (var clip in model.AnimationClips)
        {
            foreach (var b in clip.Bindings)
            {
                if (b.NodeIndex < 0 || b.NodeIndex >= model.Nodes.Count) continue;
                string name = Normalize(model.Nodes[b.NodeIndex].Name);
                result.TryGetValue(name, out var e);
                e = b.Property switch
                {
                    AnimatedProperty.Position => (true, e.Item2, e.Item3),
                    AnimatedProperty.Rotation => (e.Item1, true, e.Item3),
                    AnimatedProperty.Scale    => (e.Item1, e.Item2, true),
                    _ => e,
                };
                result[name] = e;
            }
        }
        return result;
    }

    private static void DiffSkins(Model a, Model b, string tagA, string tagB)
    {
        var ibmA = BuildIbmMap(a);
        var ibmB = BuildIbmMap(b);

        var allBones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in ibmA.Keys) allBones.Add(k);
        foreach (var k in ibmB.Keys) allBones.Add(k);

        int shown = 0;
        foreach (var name in allBones.OrderBy(s => s, StringComparer.Ordinal))
        {
            bool hasA = ibmA.TryGetValue(name, out var ma);
            bool hasB = ibmB.TryGetValue(name, out var mb);
            string sA = hasA ? $"t={Fmt(new Float3(ma.c3.X, ma.c3.Y, ma.c3.Z))} det={Det3x3(ma):F3}" : "<absent>";
            string sB = hasB ? $"t={Fmt(new Float3(mb.c3.X, mb.c3.Y, mb.c3.Z))} det={Det3x3(mb):F3}" : "<absent>";
            if (hasA && hasB)
            {
                float dt = MathF.Sqrt(
                    (ma.c3.X - mb.c3.X) * (ma.c3.X - mb.c3.X) +
                    (ma.c3.Y - mb.c3.Y) * (ma.c3.Y - mb.c3.Y) +
                    (ma.c3.Z - mb.c3.Z) * (ma.c3.Z - mb.c3.Z));
                if (dt < 0.005f && MathF.Abs(Det3x3(ma) - Det3x3(mb)) < 0.01f) continue;
            }
            Console.WriteLine($"  {name,-30} | {tagA}:{sA} | {tagB}:{sB}");
            if (++shown >= 25) { Console.WriteLine("  (...truncated)"); break; }
        }
        if (shown == 0) Console.WriteLine("  (no inverse-bind translation differences after normalization)");
    }

    private static Dictionary<string, Float4x4> BuildIbmMap(Model model)
    {
        var result = new Dictionary<string, Float4x4>(StringComparer.Ordinal);
        foreach (var skin in model.Skins)
            for (int b = 0; b < skin.BoneNodeIndices.Length; b++)
            {
                string name = Normalize(model.Nodes[skin.BoneNodeIndices[b]].Name);
                if (!result.ContainsKey(name))
                    result[name] = skin.InverseBindPoses[b];
            }
        return result;
    }

    /// <summary>
    /// Strips common name decorations so the same logical bone matches between exports:
    /// <list type="bullet">
    /// <item>Drop a <c>foo:</c> prefix (Maya namespace, e.g. <c>mixamorig:Hips</c> -&gt; <c>Hips</c>).</item>
    /// <item>Drop a trailing <c>_NN</c> instance number (e.g. <c>Hips_01</c> -&gt; <c>Hips</c>).</item>
    /// </list>
    /// </summary>
    private static string Normalize(string name)
    {
        int colon = name.IndexOf(':');
        if (colon >= 0) name = name[(colon + 1)..];
        int underscore = name.LastIndexOf('_');
        if (underscore > 0 && underscore < name.Length - 1)
        {
            bool allDigits = true;
            for (int i = underscore + 1; i < name.Length; i++)
                if (!char.IsDigit(name[i])) { allDigits = false; break; }
            if (allDigits) name = name[..underscore];
        }
        return name;
    }

    private static string TagFor(string path)
    {
        // Use the extension (lowercased, no dot) as the tag - clean, short, and unambiguous when
        // the two inputs are the same model in different formats.
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? "model" : ext;
    }

    private static string SafeFileTag(string tag)
    {
        var chars = tag.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
        return new string(chars);
    }

    private static string Fmt(Float3 v) => $"({v.X:F3},{v.Y:F3},{v.Z:F3})";
    private static string Fmt(Quaternion q) => $"({q.X:F3},{q.Y:F3},{q.Z:F3},{q.W:F3})";

    private static float Det3x3(Float4x4 m)
    {
        float a = m.c0.X, b = m.c1.X, c = m.c2.X;
        float d = m.c0.Y, e = m.c1.Y, f = m.c2.Y;
        float g = m.c0.Z, h = m.c1.Z, i = m.c2.Z;
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }
}
