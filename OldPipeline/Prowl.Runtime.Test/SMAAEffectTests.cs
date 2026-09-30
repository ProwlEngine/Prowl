// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Reflection;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Headless smoke tests for the SMAA post-process effect: its shader parses into the three
/// expected passes, the SMAA reference-library `#include "SMAA"` resolves and is inlined, and
/// the precomputed AreaTex/SearchTex lookup binaries are embedded at the expected dimensions.
/// GPU program compilation is not exercised here (the test process is headless).
/// </summary>
public class SMAAEffectTests
{
    [Fact]
    public void SMAA_EmbeddedAssets_ArePresent()
    {
        string[] names = typeof(Shader).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("Assets.Defaults.SMAA.shader"));
        Assert.Contains(names, n => n.EndsWith("Assets.Defaults.SMAA.glsl"));
        Assert.Contains(names, n => n.EndsWith("Assets.Defaults.SMAAAreaTex.bin"));
        Assert.Contains(names, n => n.EndsWith("Assets.Defaults.SMAASearchTex.bin"));
    }

    [Fact]
    public void SMAA_Shader_ParsesIntoThreePasses_WithIncludeInlined()
    {
        Shader shader = Shader.LoadDefault(DefaultShader.SMAA);
        Assert.NotNull(shader);

        string[] passNames = shader.Passes.Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "EdgeDetection", "BlendWeights", "NeighborhoodBlend" }, passNames);

        // The `#include "SMAA"` must have been inlined: the blend-weight fragment source should
        // contain a reference-library function that only exists in SMAA.glsl. If the include had
        // failed to resolve, the parser would substitute an empty string instead of throwing.
        ShaderPass blend = shader.GetPass("BlendWeights");
        string frag = (string)typeof(ShaderPass)
            .GetField("_fragmentSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(blend)!;
        Assert.Contains("SMAABlendingWeightCalculationPS", frag);
    }

    [Fact]
    public void SMAA_LookupTextures_LoadAtExpectedSizes()
    {
        Texture2D area = SMAALookupTextures.AreaTex;
        Assert.Equal(160u, area.Width);
        Assert.Equal(560u, area.Height);

        Texture2D search = SMAALookupTextures.SearchTex;
        Assert.Equal(64u, search.Width);
        Assert.Equal(16u, search.Height);
    }
}
