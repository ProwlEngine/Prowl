// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Xunit;

using Prowl.Runtime.Graphite;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class PipelineTests
{
    private readonly GraphiteTestFixture _fixture;

    private const string VertexShaderSource = @"
#version 430 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}
";

    private const string FragmentShaderSource = @"
#version 430 core
in vec2 vTexCoord;
in vec4 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vColor;
}
";

    private const string ComputeShaderSource = @"
#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) buffer Data {
    float values[];
};

void main()
{
    uint idx = gl_GlobalInvocationID.x;
    values[idx] *= 2.0;
}
";

    public PipelineTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    private ShaderModule CreateVertexShader() =>
        _fixture.Device.CreateShaderModule(ShaderModuleDescriptor.VertexGLSL(VertexShaderSource));

    private ShaderModule CreateFragmentShader() =>
        _fixture.Device.CreateShaderModule(ShaderModuleDescriptor.FragmentGLSL(FragmentShaderSource));

    private ShaderModule CreateComputeShader() =>
        _fixture.Device.CreateShaderModule(ShaderModuleDescriptor.ComputeGLSL(ComputeShaderSource));

    [Fact]
    public void CreateGraphicsPipeline_MinimalConfig_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            DebugName = "MinimalPipeline"
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
        Assert.Equal(PrimitiveTopology.TriangleList, pipeline.Topology);
    }

    [Fact]
    public void CreateGraphicsPipeline_WithVertexLayout_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(36, // 3 floats + 2 floats + 4 floats = 9 * 4 = 36
                    new VertexAttribute(0, VertexFormat.Float3, 0),
                    new VertexAttribute(1, VertexFormat.Float2, 12),
                    new VertexAttribute(2, VertexFormat.Float4, 20))
            )
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateGraphicsPipeline_WithInstancedVertexLayout_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(20, VertexStepMode.Vertex,
                    new VertexAttribute(0, VertexFormat.Float3, 0),
                    new VertexAttribute(1, VertexFormat.Float2, 12)),
                new VertexBufferLayout(16, VertexStepMode.Instance,
                    new VertexAttribute(2, VertexFormat.Float4, 0))
            )
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateGraphicsPipeline_WithDepthStencil_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            DepthStencilState = new DepthStencilStateDescriptor
            {
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompare = CompareFunction.Less,
                StencilTestEnable = true,
                StencilReadMask = 0xFF,
                StencilWriteMask = 0xFF,
                StencilFront = new StencilFaceState
                {
                    Compare = CompareFunction.Always,
                    FailOp = StencilOp.Keep,
                    DepthFailOp = StencilOp.Keep,
                    PassOp = StencilOp.Replace
                },
                StencilBack = new StencilFaceState
                {
                    Compare = CompareFunction.Always,
                    FailOp = StencilOp.Keep,
                    DepthFailOp = StencilOp.Keep,
                    PassOp = StencilOp.Replace
                }
            }
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateGraphicsPipeline_WithBlending_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            BlendState = new BlendStateDescriptor(BlendAttachment.AlphaBlend)
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateGraphicsPipeline_WithRasterizer_Succeeds()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            RasterizerState = new RasterizerStateDescriptor
            {
                CullMode = CullMode.Back,
                FrontFace = FrontFace.CounterClockwise,
                PolygonMode = PolygonMode.Fill,
                DepthBiasEnable = true,
                DepthBiasConstant = 1.0f,
                DepthBiasSlope = 1.0f,
                DepthClampEnable = false
            }
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Theory]
    [InlineData(PrimitiveTopology.PointList)]
    [InlineData(PrimitiveTopology.LineList)]
    [InlineData(PrimitiveTopology.LineStrip)]
    [InlineData(PrimitiveTopology.TriangleList)]
    [InlineData(PrimitiveTopology.TriangleStrip)]
    public void CreateGraphicsPipeline_VariousTopologies_Succeeds(PrimitiveTopology topology)
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = topology
        };

        using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);

        Assert.NotNull(pipeline);
        Assert.Equal(topology, pipeline.Topology);
    }

    [Fact]
    public void CreateGraphicsPipeline_NoVertexShader_ThrowsException()
    {
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = null,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        };

        Assert.Throws<ArgumentException>(() =>
        {
            using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);
        });
    }

    [Fact]
    public void CreateGraphicsPipeline_NoFragmentShader_ThrowsException()
    {
        using var vertexShader = CreateVertexShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = null,
            Topology = PrimitiveTopology.TriangleList
        };

        Assert.Throws<ArgumentException>(() =>
        {
            using var pipeline = _fixture.Device.CreatePipelineState(in descriptor);
        });
    }

    [Fact]
    public void CreateComputePipeline_Succeeds()
    {
        using var computeShader = CreateComputeShader();

        var descriptor = new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader,
            DebugName = "ComputePipeline"
        };

        using var pipeline = _fixture.Device.CreateComputePipelineState(in descriptor);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateComputePipeline_NoShader_ThrowsException()
    {
        var descriptor = new ComputePipelineStateDescriptor
        {
            ComputeShader = null
        };

        Assert.Throws<ArgumentException>(() =>
        {
            using var pipeline = _fixture.Device.CreateComputePipelineState(in descriptor);
        });
    }

    [Fact]
    public void Pipeline_Dispose_CleansUpResources()
    {
        using var vertexShader = CreateVertexShader();
        using var fragmentShader = CreateFragmentShader();

        var descriptor = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        };

        var pipeline = _fixture.Device.CreatePipelineState(in descriptor);
        Assert.NotNull(pipeline);

        // Should dispose without error
        pipeline.Dispose();
    }
}
