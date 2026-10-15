using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Editor.Build;

/// <summary>
/// Precompiles the shader variants a build's assets use into <see cref="Graphics.BakedShadersFile"/>.
/// Anything not baked still compiles on demand in the player.
/// </summary>
public static class ShaderBaker
{
    // Above this many global axis combinations only the defaults are baked
    private const int MaxGlobalCombos = 64;

    private const GraphicsBackend Backend = GraphicsBackend.Vulkan;

    // Fullscreen programs of the volumetric features, as (fragment, name, axis, value, reads source, lit)
    private static readonly (string Fragment, string Name, string Axis, string Value, bool Source, bool Lit)[] s_fullscreen =
    [
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsCoverage", "CLOUD", "COVERAGE", false, false),
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsSun", "CLOUD", "SUN", false, false),
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsDepth", "CLOUD", "DEPTH", false, false),
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsMarch", "CLOUD", "MARCH", false, false),
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsTemporal", "CLOUD", "TEMPORAL", false, false),
        (VolumetricCloudsFeature.Clouds, "Prowl/VolumetricCloudsComposite", "CLOUD", "COMPOSITE", true, false),
        (VolumetricFogFeature.Fog, "Prowl/VolumetricFogFroxel", "FOG", "FROXEL", false, true),
        (VolumetricFogFeature.Fog, "Prowl/VolumetricFogMarch", "FOG", "MARCH", false, false),
        (VolumetricFogFeature.Fog, "Prowl/VolumetricFogTemporal", "FOG", "TEMPORAL", false, false),
        (VolumetricFogFeature.Fog, "Prowl/VolumetricFogComposite", "FOG", "COMPOSITE", true, false),
    ];

    /// <summary>Bakes the variants the assets use into <paramref name="outputPath"/> and returns the number of passes baked.</summary>
    public static int Bake(HashSet<Guid> assets, string outputPath, CancellationToken ct)
    {
        IShaderCompiler compiler = Graphics.Compiler;
        BakedVariants.Clear();
        try
        {
            Dictionary<ShaderPass, HashSet<int>> wanted = new(ReferenceEqualityComparer.Instance);
            foreach (Guid guid in assets)
            {
                ct.ThrowIfCancellationRequested();
                Type? type = AssetDatabase.GetAssetType(guid);
                if (type == typeof(Material) && AssetDatabase.Load<Material>(guid) is { } material && material.Shader is { } materialShader)
                    Want(wanted, compiler, materialShader, material);
                else if (type == typeof(Runtime.Resources.Shader) && AssetDatabase.Load<Runtime.Resources.Shader>(guid) is { } shader)
                    Want(wanted, compiler, shader, null);
            }

            foreach ((ShaderPass pass, HashSet<int> keys) in wanted)
            {
                ct.ThrowIfCancellationRequested();
                Capture(pass.Variants(compiler, Backend), keys);
            }

            foreach ((string fragment, string name, string axis, string value, bool source, bool lit) in s_fullscreen)
            {
                ct.ThrowIfCancellationRequested();
                BakeFullscreen(compiler, fragment, name, axis, value, source, lit);
            }

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using (FileStream file = File.Create(outputPath))
                BakedVariants.Write(file);
            return BakedVariants.Count;
        }
        finally
        {
            BakedVariants.Clear();
        }
    }

    private static void Want(Dictionary<ShaderPass, HashSet<int>> wanted, IShaderCompiler compiler, Runtime.Resources.Shader shader, Material? material)
    {
        if (shader.GpuShader is not { } gpu || gpu.Definition.Passes is not { } passes)
            return;

        List<Keyword> fixedKeywords = [];
        foreach (VariantSpace axis in gpu.MaterialAxes)
            fixedKeywords.Add(new Keyword(axis.Name, material != null ? KeywordOf(material, axis) : axis.Values[0]));

        List<Keyword[]> combos = GlobalCombos(gpu.GlobalAxes);
        foreach (ShaderPass pass in passes)
        {
            PassVariants variants = pass.Variants(compiler, Backend);
            if (!wanted.TryGetValue(pass, out HashSet<int>? keys))
                wanted[pass] = keys = [];
            foreach (Keyword[] global in combos)
            {
                if (variants.TryGetKey([.. fixedKeywords, .. global], out int key))
                    keys.Add(key);
            }
        }
    }

    // The material's value, then its parents', then the axis default
    private static string KeywordOf(Material material, VariantSpace axis)
    {
        for (Material? m = material; m.IsValid(); m = m.Parent)
        {
            if (m.Keywords.TryGetValue(axis.Name, out string? value) && axis.Values.Contains(value))
                return value;
        }
        return axis.Values[0];
    }

    private static List<Keyword[]> GlobalCombos(IReadOnlyList<VariantSpace> axes)
    {
        long count = 1;
        foreach (VariantSpace axis in axes)
            count *= axis.Values.Count;

        List<Keyword[]> combos = [];
        if (count > MaxGlobalCombos)
        {
            Keyword[] defaults = new Keyword[axes.Count];
            for (int i = 0; i < defaults.Length; i++)
                defaults[i] = new Keyword(axes[i].Name, axes[i].Values[0]);
            combos.Add(defaults);
            return combos;
        }

        for (int n = 0; n < count; n++)
        {
            Keyword[] combo = new Keyword[axes.Count];
            int rest = n;
            for (int i = axes.Count - 1; i >= 0; i--)
            {
                combo[i] = new Keyword(axes[i].Name, axes[i].Values[rest % axes[i].Values.Count]);
                rest /= axes[i].Values.Count;
            }
            combos.Add(combo);
        }
        return combos;
    }

    // Mono and layered forms
    private static void BakeFullscreen(IShaderCompiler compiler, string fragment, string name, string axis, string value, bool source, bool lit)
    {
        PassVariants variants = FullscreenProgram.Pass(fragment, name, lit).Variants(compiler, Backend);
        HashSet<int> keys = [];
        foreach (bool layered in new[] { false, true })
        {
            Keyword[] keywords =
            [
                new(axis, value),
                new("GRAPHITE_NO_SOURCE", source ? "false" : "true"),
                new("GRAPHITE_LAYERED", layered ? "true" : "false"),
            ];
            if (variants.TryGetKey(keywords, out int key))
                keys.Add(key);
        }
        Capture(variants, keys);
    }

    // Failed variants are skipped
    private static void Capture(PassVariants variants, IEnumerable<int> keys)
    {
        List<int> compiled = [];
        foreach (int key in keys)
        {
            if (variants.Request(key, CompilePolicy.Sync, out _, out _) == VariantState.Ready)
                compiled.Add(key);
        }
        BakedVariants.Capture(variants, compiled);
    }
}
