// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Dictionary<ShaderPass, Dictionary<string, Keyword[]>> wanted = new();

        foreach (Guid asset in assets)
        {
            if (AssetDatabase.GetAssetType(asset) != typeof(Material) || AssetDatabase.Load<Material>(asset) is not { } material)
                continue;
            Shader? source = material.Shader;
            if (source.IsNotValid() || source.GpuShader is not GShader shader)
                continue;

            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (VariantSpace axis in shader.MaterialAxes)
                values[axis.Name] = material.GetKeyword(axis.Name) is { Length: > 0 } value ? value : axis.Values[0];
            Keyword[][] globals = VariantCombos.Generate(shader.GlobalAxes);

            foreach (ShaderPass pass in shader.Definition.Passes ?? [])
            {
                if (!wanted.TryGetValue(pass, out Dictionary<string, Keyword[]>? combos))
                    wanted[pass] = combos = new Dictionary<string, Keyword[]>(StringComparer.Ordinal);
                IReadOnlyList<VariantSpace> axes = pass.AxesWith(compiler);
                foreach (Keyword[] global in globals)
                {
                    foreach (Keyword keyword in global) values[keyword.Name] = keyword.Value;
                    if (ComboOf(axes, values) is { } combo)
                        combos.TryAdd(string.Join(";", combo.Select(k => $"{k.Name}={k.Value}")), combo);
                }
            }
        }

        BakedVariants baked = new(compiler);
        Parallel.ForEach(wanted, new ParallelOptions { CancellationToken = cancel }, pass =>
        {
            if (pass.Value.Count > 0)
                baked.Capture(pass.Key, backend, pass.Value.Values);
        });

        using FileStream file = File.Create(path);
        baked.Write(file);
        return baked.Count;
    }

    // The value of each of the pass's axes, or null when one has a value the axis does not take
    private static Keyword[]? ComboOf(IReadOnlyList<VariantSpace> axes, Dictionary<string, string> values)
    {
        Keyword[] combo = new Keyword[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            string value = values.TryGetValue(axes[i].Name, out string? set) ? set : axes[i].Values[0];
            if (!axes[i].Values.Contains(value)) return null;
            combo[i] = new Keyword(axes[i].Name, value);
        }
        return combo;
    }
}
