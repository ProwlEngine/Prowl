// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ShaderPass = Prowl.Graphite.ShaderDef.ShaderPass;

namespace Prowl.Runtime.Test;

public class MeshDrawPathTests : RuntimeTestBase
{
    private static Shader MakeTaggedShader()
    {
        var definition = new ShaderDefinition
        {
            Name = "Test/Tagged",
            Passes =
            [
                new ShaderPass { Name = "Shadow", State = new PassState(), InlineSlang = "", Tags = new() { [PassTags.LightMode] = PassTags.ShadowCaster } },
                new ShaderPass { Name = "Forward", State = new PassState(), InlineSlang = "", Tags = new() { [PassTags.RenderOrder] = PassTags.Opaque } },
            ],
        };
        return new Shader("Tagged", [], definition, new ShaderSnapshot());
    }

    [Fact]
    public void GetPassWithTag_ResolvesAndCachesPerTagValue()
    {
        Shader shader = MakeTaggedShader();

        Assert.Equal(1, shader.GetPassWithTag(PassTags.RenderOrder, PassTags.Opaque));
        Assert.Equal(1, shader.GetPassWithTag(PassTags.RenderOrder, PassTags.Opaque));
        Assert.Equal(1, shader.TagCacheEntryCount);

        Assert.Equal(0, shader.GetPassWithTag(PassTags.LightMode, PassTags.ShadowCaster));
        Assert.Equal(0, shader.GetPassWithTag(PassTags.LightMode));
        Assert.Null(shader.GetPassWithTag(PassTags.RenderOrder, PassTags.Transparent));
        Assert.Equal(4, shader.TagCacheEntryCount);

        Assert.Null(shader.GetPassWithTagOrNull(PassTags.RenderOrder, PassTags.UI));
    }

    [Fact]
    public void KeywordArray_IsCachedAndInvalidatedOnChange()
    {
        var material = new Material();
        material.ClearKeywords();

        Keyword[] empty = material.KeywordArray;
        Assert.Empty(empty);
        Assert.Same(empty, material.KeywordArray);

        material.SetKeyword("HAS_NORMALS", true);
        Keyword[] one = material.KeywordArray;
        Assert.NotSame(empty, one);
        Assert.Single(one);
        Assert.Equal("HAS_NORMALS", one[0].Name);
        Assert.Equal("true", one[0].Value);
        Assert.Same(one, material.KeywordArray);

        material.SetKeyword("HAS_NORMALS", false);
        Keyword[] flipped = material.KeywordArray;
        Assert.NotSame(one, flipped);
        Assert.Equal("false", flipped[0].Value);

        Assert.True(material.ClearKeyword("HAS_NORMALS"));
        Assert.Empty(material.KeywordArray);
        Assert.False(material.ClearKeyword("HAS_NORMALS"));
    }

    [Fact]
    public void RenderStateOverride_DefaultsToNoOverride()
    {
        var material = new Material();
        Assert.NotNull(material.RenderStateOverride);
        Assert.False(material.HasRenderStateOverride);

        material.RenderStateOverride.CullMode = FaceCullMode.None;
        Assert.True(material.HasRenderStateOverride);

        var clone = new Material(material);
        Assert.Equal(FaceCullMode.None, clone.RenderStateOverride.CullMode);
        Assert.NotSame(material.RenderStateOverride, clone.RenderStateOverride);
    }

    [Fact]
    public void RenderStateOverride_RoundTripsThroughEcho()
    {
        var material = new Material();
        material.RenderStateOverride.CullMode = FaceCullMode.Front;
        material.RenderStateOverride.DepthWriteMask = false;
        material.RenderStateOverride.EnableBlend = true;
        material.RenderStateOverride.WriteMask = ColorWriteMask.Red | ColorWriteMask.Alpha;

        Prowl.Echo.EchoObject echo = Prowl.Echo.Serializer.Serialize(material);
        var loaded = Prowl.Echo.Serializer.Deserialize<Material>(echo);

        Assert.NotNull(loaded);
        Assert.Equal(FaceCullMode.Front, loaded!.RenderStateOverride.CullMode);
        Assert.False(loaded.RenderStateOverride.DepthWriteMask);
        Assert.True(loaded.RenderStateOverride.EnableBlend);
        Assert.Equal(ColorWriteMask.Red | ColorWriteMask.Alpha, loaded.RenderStateOverride.WriteMask);
        Assert.Null(loaded.RenderStateOverride.DepthFunc);
        Assert.True(loaded.HasRenderStateOverride);
    }

    [Fact]
    public void DrawParams_DefaultsToWholeMeshSingleDraw()
    {
        var p = new DrawParams();
        Assert.Equal(Float4x4.Identity, p.Model);
        Assert.Equal(Float4x4.Identity, p.WorldToObject);
        Assert.Equal(Float4x4.Identity, p.PrevModel);
        Assert.Equal(-1, p.SubMeshIndex);
        Assert.Null(p.Properties);
        Assert.Null(p.InstanceBuffer);
        Assert.Equal(0u, p.InstanceCount);
    }

    [Fact]
    public void MeshRenderer_TracksPreviousWorldMatrixAcrossFrames()
    {
        var time = new TimeData { DeltaTime = 1f / 60f };
        Time.TimeStack.Push(time);
        try
        {
            Scene scene = CreateScene(enable: true);
            GameObject go = CreateGameObject("Cube");
            scene.Add(go);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.Mesh = Mesh.CreateCube(Float3.One);
            renderer.Material = new Material();

            var culler = new SceneCuller();

            go.Transform.Position = new Float3(1, 0, 0);
            Float4x4 first = go.Transform.LocalToWorldMatrix;
            renderer.OnRenderCollect(culler);
            var r0 = Assert.IsType<MeshRenderable>(Assert.Single(culler.Renderables));
            Assert.Equal(first, r0.Matrix);
            Assert.Equal(first, r0.PreviousMatrix);

            culler.Clear();
            renderer.OnRenderCollect(culler);
            var r0b = Assert.IsType<MeshRenderable>(Assert.Single(culler.Renderables));
            Assert.Equal(first, r0b.PreviousMatrix);

            time.FrameCount++;
            go.Transform.Position = new Float3(2, 0, 0);
            Float4x4 second = go.Transform.LocalToWorldMatrix;
            culler.Clear();
            renderer.OnRenderCollect(culler);
            var r1 = Assert.IsType<MeshRenderable>(Assert.Single(culler.Renderables));
            Assert.Equal(second, r1.Matrix);
            Assert.Equal(first, r1.PreviousMatrix);
            Assert.Equal(first, renderer.PreviousWorldMatrix);

            time.FrameCount++;
            culler.Clear();
            renderer.OnRenderCollect(culler);
            var r2 = Assert.IsType<MeshRenderable>(Assert.Single(culler.Renderables));
            Assert.Equal(second, r2.PreviousMatrix);
        }
        finally
        {
            Time.TimeStack.Pop();
        }
    }
}
