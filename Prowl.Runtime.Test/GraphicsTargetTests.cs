// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Xunit;

namespace Prowl.Runtime.Test;

public class GraphicsTargetTests
{
    private static GraphicsCapabilities Caps(GraphicsTarget target, int major, int minor, params string[] extensions)
        => new(target, major, minor, new HashSet<string>(extensions));

    [Fact]
    public void OpenGL41_HasNoComputeOrStorageBuffers()
    {
        GraphicsCapabilities caps = Caps(GraphicsTarget.OpenGL, 4, 1);

        Assert.False(caps.Has(GraphicsFeature.ComputeShaders));
        Assert.False(caps.Has(GraphicsFeature.StorageBuffers));
        Assert.False(caps.Has(GraphicsFeature.ShaderBindingLayout));
        Assert.True(caps.Has(GraphicsFeature.MultiDraw));
        Assert.True(caps.Has(GraphicsFeature.TextureReadback));
    }

    [Fact]
    public void OpenGL46_HasEverything()
    {
        GraphicsCapabilities caps = Caps(GraphicsTarget.OpenGL, 4, 6);

        foreach (GraphicsFeature feature in System.Enum.GetValues<GraphicsFeature>())
            Assert.True(caps.Has(feature), feature.ToString());
    }

    [Fact]
    public void OpenGLES32_LacksDesktopOnlyFeatures_UnlessTheDriverHasTheExtension()
    {
        GraphicsCapabilities bare = Caps(GraphicsTarget.OpenGLES, 3, 2);
        Assert.True(bare.Has(GraphicsFeature.ComputeShaders));
        Assert.True(bare.Has(GraphicsFeature.StorageBuffers));
        Assert.False(bare.Has(GraphicsFeature.MultiDraw));
        Assert.False(bare.Has(GraphicsFeature.TextureReadback));
        Assert.False(bare.Has(GraphicsFeature.DepthClamp));
        Assert.False(bare.Has(GraphicsFeature.FloatLinearFiltering));

        GraphicsCapabilities extended = Caps(GraphicsTarget.OpenGLES, 3, 2, "GL_EXT_depth_clamp", "GL_OES_texture_float_linear");
        Assert.True(extended.Has(GraphicsFeature.DepthClamp));
        Assert.True(extended.Has(GraphicsFeature.FloatLinearFiltering));
    }

    [Fact]
    public void ShaderPrelude_FollowsTheTarget()
    {
        GraphicsCapabilities previous = Graphics.Capabilities;
        try
        {
            Graphics.Capabilities = Caps(GraphicsTarget.OpenGLES, 3, 2);
            string es = Graphics.ShaderPrelude;
            Assert.StartsWith("#version 320 es\n", es);
            Assert.Contains("#define PROWL_GLES", es);
            Assert.Contains("precision highp float;", es);
            Assert.Contains("precision highp sampler2DShadow;", es);
            Assert.Contains("#define PROWL_STORAGE_BUFFERS", es);

            Graphics.Capabilities = Caps(GraphicsTarget.OpenGL, 4, 1);
            string gl41 = Graphics.ShaderPrelude;
            Assert.StartsWith("#version 410 core\n", gl41);
            Assert.DoesNotContain("PROWL_GLES", gl41);
            Assert.DoesNotContain("PROWL_STORAGE_BUFFERS", gl41);
            Assert.DoesNotContain("precision", gl41);

            Graphics.Capabilities = Caps(GraphicsTarget.OpenGL, 4, 6);
            Assert.StartsWith("#version 460 core\n", Graphics.ShaderPrelude);
            Assert.Contains("#define PROWL_COMPUTE", Graphics.ShaderPrelude);
        }
        finally
        {
            Graphics.Capabilities = previous;
        }
    }
}
