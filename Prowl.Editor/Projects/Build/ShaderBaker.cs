// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using GShader = Prowl.Graphite.Rendering.Shader;

namespace Prowl.Editor.Build;

/// <summary>
/// Compiles the variants a build's materials draw with, each material's keywords under every combination of its shader's
/// global axes, and writes them where the player loads them before its renderer starts. Everything else the player
/// compiles on demand.
/// </summary>
internal static class ShaderBaker
{
    public static int Bake(IEnumerable<Guid> assets, string path, CancellationToken cancel)
    {
        GraphicsBackend backend = Graphics.Device?.BackendType ?? GraphicsBackend.Vulkan;
        IShaderCompiler compiler = Graphics.Compiler;
        Dictionary<PassVariants, HashSet<int>> wanted = new();

        foreach (Guid asset in assets)
        {
            if (AssetDatabase.GetAssetType(asset) != typeof(Material) || AssetDatabase.Load<Material>(asset) is not { } material)
                continue;
            Shader? source = material.Shader;
            if (source.IsNotValid() || source.GpuShader is not GShader shader)
                continue;

            List<Keyword> keywords = new();
            foreach (VariantSpace axis in shader.MaterialAxes)
                keywords.Add(new Keyword(axis.Name, material.GetKeyword(axis.Name) is { Length: > 0 } value ? value : axis.Values[0]));
            Keyword[][] globals = VariantCombos.Generate(shader.GlobalAxes);

            foreach (ShaderPass pass in shader.Definition.Passes ?? [])
            {
                PassVariants variants = pass.Variants(compiler, backend);
                if (!wanted.TryGetValue(variants, out HashSet<int>? keys))
                    wanted[variants] = keys = new HashSet<int>();
                foreach (Keyword[] global in globals)
                {
                    if (variants.TryGetKey([.. keywords, .. global], out int key))
                        keys.Add(key);
                }
            }
        }

        Parallel.ForEach(wanted, new ParallelOptions { CancellationToken = cancel }, pass =>
        {
            foreach (int key in pass.Value)
                pass.Key.Request(key, CompilePolicy.Sync, out _, out _);
        });

        BakedVariants.Clear();
        try
        {
            foreach ((PassVariants variants, HashSet<int> keys) in wanted)
                BakedVariants.Capture(variants, keys);
            using FileStream file = File.Create(path);
            BakedVariants.Write(file);
            return BakedVariants.Count;
        }
        finally
        {
            BakedVariants.Clear();
        }
    }
}
