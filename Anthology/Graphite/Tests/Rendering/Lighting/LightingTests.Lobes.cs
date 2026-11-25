using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's coat, fuzz and anisotropy on the GPU: the white furnace over white bases, turned frames,
// the LITE tier, and each lobe against PbrReference under a point and a directional light.
public sealed partial class LightingTests
{
    private static void White(Material m) => m.SetColor("base_color", new Float4(1, 1, 1, 1));

    // A dielectric with diffuse, and a metal
    private static readonly Action<Material>[] s_whiteBases =
    [
        m => { White(m); m.SetFloat("specular_roughness", 0.4f); m.SetFloat("base_diffuse_roughness", 0.5f); },
        m => { White(m); m.SetFloat("base_metalness", 1); m.SetFloat("specular_roughness", 0.3f); },
    ];

    private static readonly PbrInputs[] s_lobeInputs =
    [
        new(new Vector3(0.6f, 0.3f, 0.2f), Roughness: 0.5f, DiffuseRoughness: 0.3f) { CoatWeight = 0.8f, CoatColor = new Vector3(0.9f, 0.7f, 0.5f), CoatRoughness = 0.2f, CoatIor = 1.7f },
        new(new Vector3(0.9f, 0.6f, 0.4f), Metalness: 1, Roughness: 0.4f, SpecularColor: new Vector3(0.9f, 0.8f, 0.6f)) { Anisotropy = 0.7f, CoatWeight = 1, CoatRoughness = 0.1f, CoatAnisotropy = 0.5f, CoatDarkening = 0.5f },
        new(new Vector3(0.3f, 0.4f, 0.6f), Roughness: 0.6f) { FuzzWeight = 0.9f, FuzzColor = new Vector3(1, 0.8f, 0.6f), FuzzRoughness = 0.4f },
        new(new Vector3(0.7f, 0.5f, 0.3f), Roughness: 0.35f, Ior: 1.4f) { Anisotropy = 0.5f, CoatWeight = 0.5f, CoatRoughness = 0.3f, CoatIor = 1.8f, FuzzWeight = 0.5f, FuzzRoughness = 0.7f, SubsurfaceWeight = 0.5f, SubsurfaceColor = new Vector3(0.9f, 0.4f, 0.3f) },
    ];

    public static IEnumerable<object[]> LobeCases()
    {
        for (int i = 0; i < s_lobeInputs.Length; i++)
            yield return [i];
    }

}
