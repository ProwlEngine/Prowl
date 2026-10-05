using System;
using System.Collections.Generic;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class DefaultPropertiesTests : IDisposable
{
    private const string Source = """
        Shader "Test/Defaults"
        {
            Properties
            {
                Tint("Tint", Color) = (1, 0.5, 0.25, 1)
                Scale("Scale", Float) = 2
                Count("Count", Integer) = 3
                Unused("Unused", Float) = 9
                NoDefault("No Default", Float)
                Albedo("Albedo", Texture2D) = "white" {}
            }

            Pass
            {
                SLANGPROGRAM
                float4 vsMain() : SV_Position { return 0; }
                ENDSLANG
            }
        }
        """;

    private readonly GraphicsDevice _device;
    private readonly ShaderDefinition _definition;


    public DefaultPropertiesTests()
    {
        _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));
        _definition = Parse.Shader(Source);

        ShaderDescription description = new()
        {
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Block", ResourceKind.UniformBuffer, ShaderStages.Fragment, 0)
                        {
                            UniformFields =
                            [
                                new UniformBlockField("Tint", 0, 16, UniformScalarType.Float4),
                                new UniformBlockField("Scale", 16, 4, UniformScalarType.Float1),
                                new UniformBlockField("Count", 20, 4, UniformScalarType.Int1),
                                new UniformBlockField("NoDefault", 24, 4, UniformScalarType.Float1),
                            ]
                        },
                        new ResourceLayoutElementDescription("Albedo", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 1),
                    ]
                }
            ]
        };

        Variant variant = new([], [(GraphicsBackend.Vulkan, description)]);
        _definition.Create(_device, new ShaderSnapshot { Passes = [new PassSnapshot { Axes = [], Variants = [variant] }] });
    }


    public void Dispose()
    {
        _device.Dispose();
    }


    private ShaderPass Pass => _definition.Passes![0];


    private static float Float(PropertySet set, string name) => set.Entries[name].Uniform.As<float>();


    [Fact]
    public void ParsedProperties_RecordWhetherTheyHaveADefault()
    {
        Assert.True(_definition.Properties![0].HasDefault);
        Assert.False(_definition.Properties[4].HasDefault);
    }


    [Fact]
    public void SetsDefaultsForPropertiesWithTargets()
    {
        PropertySet set = _definition.CreateDefaultProperties(Pass);

        Assert.Equal(3, set.EntryCount);
        Assert.Equal(new Float4(1, 0.5f, 0.25f, 1), set.Entries["Tint"].Uniform.As<Float4>());
        Assert.Equal(2f, Float(set, "Scale"));
        Assert.Equal(3, set.Entries["Count"].Uniform.As<int>());
    }


    [Fact]
    public void PropertyWithoutTarget_IsSkipped()
    {
        PropertySet set = _definition.CreateDefaultProperties(Pass);

        Assert.DoesNotContain((PropertyID)"Unused", set.Entries.Keys);
    }


    [Fact]
    public void PropertyWithoutDefault_IsSkippedEvenWithTarget()
    {
        PropertySet set = _definition.CreateDefaultProperties(Pass);

        Assert.DoesNotContain((PropertyID)"NoDefault", set.Entries.Keys);
    }


    [Fact]
    public void TextureResolver_ReceivesNameAndType()
    {
        List<(string Name, ShaderPropertyType Type)> calls = new();

        _definition.CreateDefaultProperties(Pass, 0, (name, type) =>
        {
            calls.Add((name, type));
            return null;
        });

        Assert.Equal([("white", ShaderPropertyType.Texture2D)], calls);
    }


    [Fact]
    public void TextureResolver_NullSkipsAndViewIsSet()
    {
        Assert.DoesNotContain((PropertyID)"Albedo", _definition.CreateDefaultProperties(Pass, 0, (_, _) => null).Entries.Keys);

        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        TextureView view = _device.ResourceFactory.CreateTextureView(texture);

        PropertySet set = _definition.CreateDefaultProperties(Pass, 0, (_, _) => view);

        Assert.Same(view, set.Entries["Albedo"].TextureView);
    }
}
