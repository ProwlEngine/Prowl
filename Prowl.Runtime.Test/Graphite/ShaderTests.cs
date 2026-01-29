// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Xunit;

using Prowl.Runtime.Graphite;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class ShaderTests
{
    private readonly GraphiteTestFixture _fixture;

    // Simple GLSL shaders for testing
    private const string SimpleVertexShader = @"
#version 430 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    vTexCoord = aTexCoord;
}
";

    private const string SimpleFragmentShader = @"
#version 430 core
in vec2 vTexCoord;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vTexCoord, 0.0, 1.0);
}
";

    private const string UniformFragmentShader = @"
#version 430 core
in vec2 vTexCoord;
out vec4 FragColor;

layout(std140, binding = 0) uniform Material {
    vec4 color;
    float roughness;
};

void main()
{
    FragColor = color * roughness;
}
";

    private const string TexturedFragmentShader = @"
#version 430 core
in vec2 vTexCoord;
out vec4 FragColor;

layout(binding = 0) uniform sampler2D uTexture;

void main()
{
    FragColor = texture(uTexture, vTexCoord);
}
";

    private const string ComputeShader = @"
#version 430 core
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(std430, binding = 0) buffer OutputBuffer {
    float data[];
};

void main()
{
    uint index = gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * gl_NumWorkGroups.x * 16;
    data[index] = float(index);
}
";

    private const string InvalidShader = @"
#version 430 core
void main() {
    this_is_not_valid_glsl;
}
";

    public ShaderTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateVertexShader_ValidGLSL_Succeeds()
    {
        using var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(SimpleVertexShader));

        Assert.NotNull(shader);
        Assert.Equal(ShaderStage.Vertex, shader.Stage);
    }

    [Fact]
    public void CreateFragmentShader_ValidGLSL_Succeeds()
    {
        using var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(SimpleFragmentShader));

        Assert.NotNull(shader);
        Assert.Equal(ShaderStage.Fragment, shader.Stage);
    }

    [Fact]
    public void CreateComputeShader_ValidGLSL_Succeeds()
    {
        using var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(ComputeShader));

        Assert.NotNull(shader);
        Assert.Equal(ShaderStage.Compute, shader.Stage);
    }

    [Fact]
    public void CreateShader_InvalidGLSL_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var shader = _fixture.Device.CreateShaderModule(
                ShaderModuleDescriptor.FragmentGLSL(InvalidShader));
        });
    }

    [Fact]
    public void CreateShader_EmptySource_Throws_OrCreatesInvalidShader()
    {
        // Empty source may either throw during creation or create a shader that fails at link time
        // This is implementation-dependent - some drivers accept empty shaders, some don't
        try
        {
            using var shader = _fixture.Device.CreateShaderModule(
                ShaderModuleDescriptor.VertexGLSL(""));
            // If we get here, the shader was created (may fail later at link time)
            Assert.NotNull(shader);
        }
        catch (InvalidOperationException)
        {
            // This is also acceptable - shader compilation failed
        }
    }

    [Fact]
    public void Shader_Dispose_CleansUpResources()
    {
        var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(SimpleVertexShader));

        // Should dispose without error
        shader.Dispose();
    }

    [Fact]
    public void CreateShader_WithUniforms_Succeeds()
    {
        using var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(UniformFragmentShader));

        Assert.NotNull(shader);
    }

    [Fact]
    public void CreateShader_WithTextureSampler_Succeeds()
    {
        using var shader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(TexturedFragmentShader));

        Assert.NotNull(shader);
    }

    [Fact]
    public void CreateShader_WithDebugName_Succeeds()
    {
        var desc = ShaderModuleDescriptor.VertexGLSL(SimpleVertexShader);
        desc.DebugName = "TestVertexShader";

        using var shader = _fixture.Device.CreateShaderModule(desc);

        Assert.NotNull(shader);
        Assert.Equal("TestVertexShader", shader.DebugName);
    }
}
