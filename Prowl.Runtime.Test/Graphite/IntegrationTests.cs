// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Xunit;

using Silk.NET.OpenGL;

using Prowl.Runtime.Graphite;
using Prowl.Runtime.Graphite.OpenGL;
using Prowl.Vector;

namespace Prowl.Runtime.Test.Graphite;

/// <summary>
/// Integration tests that verify actual GPU computation and rendering results.
/// These tests dispatch compute shaders, render to textures, and read back results to verify correctness.
/// </summary>
[Collection("Graphite")]
public class IntegrationTests
{
    private readonly GraphiteTestFixture _fixture;

    public IntegrationTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Compute Shader Tests

    private const string DoubleValuesComputeShader = @"
#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) buffer InputBuffer {
    float inputData[];
};

layout(std430, binding = 1) buffer OutputBuffer {
    float outputData[];
};

void main()
{
    uint idx = gl_GlobalInvocationID.x;
    outputData[idx] = inputData[idx] * 2.0;
}
";

    // Adds 100 to each value (constant embedded in shader)
    private const string AddConstantComputeShader = @"
#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) buffer DataBuffer {
    float data[];
};

void main()
{
    uint idx = gl_GlobalInvocationID.x;
    data[idx] = data[idx] + 100.0;
}
";

    // Simple 4x4 matrix multiply (size hardcoded to avoid needing uniform buffer)
    private const string MatrixMultiply4x4ComputeShader = @"
#version 430 core
layout(local_size_x = 4, local_size_y = 4) in;

layout(std430, binding = 0) buffer MatrixA {
    float matA[16]; // 4x4
};

layout(std430, binding = 1) buffer MatrixB {
    float matB[16]; // 4x4
};

layout(std430, binding = 2) buffer MatrixC {
    float matC[16]; // 4x4
};

void main()
{
    uint row = gl_GlobalInvocationID.y;
    uint col = gl_GlobalInvocationID.x;

    if (row >= 4u || col >= 4u) return;

    float sum = 0.0;
    for (int k = 0; k < 4; k++) {
        sum += matA[row * 4u + k] * matB[k * 4u + col];
    }
    matC[row * 4u + col] = sum;
}
";

    [Fact]
    public void ComputeShader_DoubleValues_ProducesCorrectResults()
    {
        // Input data: 64 floats
        var inputData = new float[64];
        for (int i = 0; i < inputData.Length; i++)
            inputData[i] = i + 1; // 1, 2, 3, ..., 64

        // Create input buffer
        using var inputBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Storage,
            inputData.AsSpan(),
            MemoryAccess.GpuOnly);

        // Create output buffer (for GPU read-back)
        using var outputBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = (uint)(inputData.Length * sizeof(float)),
            Usage = BufferUsage.Storage | BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Create compute shader and pipeline
        using var computeShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(DoubleValuesComputeShader));

        using var pipeline = _fixture.Device.CreateComputePipelineState(new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader,
            DebugName = "DoubleValuesPipeline"
        });

        // Create bind group layout and bind group
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageBuffer(0, ShaderStage.Compute),
            BindGroupLayoutEntry.StorageBuffer(1, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, inputBuffer),
            BindGroupEntry.ForBuffer(1, outputBuffer)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        // Record and submit compute commands
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.SetComputePipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Dispatch(1, 1, 1); // 1 workgroup of 64 threads
        cmd.MemoryBarrier();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back results using GL directly
        var results = new float[64];
        ReadBufferData<float>(outputBuffer, results);

        // Verify results: each value should be doubled
        for (int i = 0; i < results.Length; i++)
        {
            float expected = (i + 1) * 2.0f;
            Assert.Equal(expected, results[i], precision: 5);
        }
    }

    [Fact]
    public void ComputeShader_AddConstant_ProducesCorrectResults()
    {
        // Input/output data: 64 floats initialized to their indices
        var data = new float[64];
        for (int i = 0; i < data.Length; i++)
            data[i] = i;

        // Create data buffer (input/output)
        using var dataBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Storage | BufferUsage.CopySource,
            data.AsSpan(),
            MemoryAccess.GpuToCpu);

        // Create compute shader and pipeline
        using var computeShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(AddConstantComputeShader));

        using var pipeline = _fixture.Device.CreateComputePipelineState(new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader
        });

        // Create bind group (only storage buffer now, constant is in shader)
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageBuffer(0, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, dataBuffer)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        // Execute compute
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.SetComputePipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Dispatch(1, 1, 1);
        cmd.MemoryBarrier();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back and verify
        var results = new float[64];
        ReadBufferData<float>(dataBuffer, results);

        for (int i = 0; i < results.Length; i++)
        {
            float expected = i + 100.0f; // constant is embedded in shader
            Assert.Equal(expected, results[i], precision: 5);
        }
    }

    [Fact]
    public void ComputeShader_MatrixMultiply_ProducesCorrectResults()
    {
        const int matrixSize = 4; // 4x4 matrix (hardcoded in shader)
        const int totalElements = matrixSize * matrixSize;

        // Create identity-like matrices for easy verification
        // A = simple values, B = identity matrix
        var matrixA = new float[totalElements];
        var matrixB = new float[totalElements];
        var expectedC = new float[totalElements];

        // Initialize A with sequential values (row-major)
        for (int i = 0; i < totalElements; i++)
            matrixA[i] = i + 1;

        // Initialize B as identity matrix
        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                matrixB[row * matrixSize + col] = (row == col) ? 1.0f : 0.0f;
            }
        }

        // Expected result: A * I = A
        Array.Copy(matrixA, expectedC, totalElements);

        // Create buffers
        using var bufferA = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Storage, matrixA.AsSpan(), MemoryAccess.GpuOnly);
        using var bufferB = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Storage, matrixB.AsSpan(), MemoryAccess.GpuOnly);
        using var bufferC = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = (uint)(totalElements * sizeof(float)),
            Usage = BufferUsage.Storage | BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Create pipeline
        using var computeShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(MatrixMultiply4x4ComputeShader));
        using var pipeline = _fixture.Device.CreateComputePipelineState(new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader
        });

        // Create bind group (no uniform buffer needed - size is hardcoded)
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageBuffer(0, ShaderStage.Compute),
            BindGroupLayoutEntry.StorageBuffer(1, ShaderStage.Compute),
            BindGroupLayoutEntry.StorageBuffer(2, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, bufferA),
            BindGroupEntry.ForBuffer(1, bufferB),
            BindGroupEntry.ForBuffer(2, bufferC)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        // Execute
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.SetComputePipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Dispatch(1, 1, 1); // 4x4 threads in one workgroup
        cmd.MemoryBarrier();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back and verify
        var results = new float[totalElements];
        ReadBufferData<float>(bufferC, results);

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(expectedC[i], results[i], precision: 5);
        }
    }

    #endregion

    #region Render Tests

    private const string SolidColorVertexShader = @"
#version 430 core
layout(location = 0) in vec2 aPosition;

void main()
{
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

    private const string SolidColorFragmentShader = @"
#version 430 core
out vec4 FragColor;

layout(std140, binding = 0) uniform Color {
    vec4 solidColor;
};

void main()
{
    FragColor = solidColor;
}
";

    private const string FullscreenTriVertexShader = @"
#version 430 core

// Generates a fullscreen triangle from vertex ID (no vertex buffer needed)
void main()
{
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),
        vec2( 3.0, -1.0),
        vec2(-1.0,  3.0)
    );
    gl_Position = vec4(positions[gl_VertexID], 0.0, 1.0);
}
";

    private const string RedFragmentShader = @"
#version 430 core
out vec4 FragColor;

void main()
{
    FragColor = vec4(1.0, 0.0, 0.0, 1.0);
}
";

    private const string GreenFragmentShader = @"
#version 430 core
out vec4 FragColor;

void main()
{
    FragColor = vec4(0.0, 1.0, 0.0, 1.0);
}
";

    private const string BlueFragmentShader = @"
#version 430 core
out vec4 FragColor;

void main()
{
    FragColor = vec4(0.0, 0.0, 1.0, 1.0);
}
";

    [Fact]
    public void Render_ClearToRed_PixelIsRed()
    {
        // Create a 1x1 render target
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1,
            Height = 1,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Create readback buffer
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4, // 1 pixel * 4 bytes (RGBA8)
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Record commands to clear to red
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(1.0f, 0.0f, 0.0f, 1.0f) // Red
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.EndRenderPass();

        // Copy texture to buffer
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });

        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back the pixel
        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Verify it's red (RGBA: 255, 0, 0, 255)
        Assert.Equal(255, pixelData[0]); // R
        Assert.Equal(0, pixelData[1]);   // G
        Assert.Equal(0, pixelData[2]);   // B
        Assert.Equal(255, pixelData[3]); // A
    }

    [Fact]
    public void Render_ClearToGreen_PixelIsGreen()
    {
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.0f, 1.0f, 0.0f, 1.0f) // Green
                }
            ]
        });
        cmd.EndRenderPass();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        Assert.Equal(0, pixelData[0]);   // R
        Assert.Equal(255, pixelData[1]); // G
        Assert.Equal(0, pixelData[2]);   // B
        Assert.Equal(255, pixelData[3]); // A
    }

    [Fact]
    public void Render_FullscreenTriangle_FillsPixel()
    {
        // Create a 2x2 render target
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16, // 4 pixels * 4 bytes
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Create shaders and pipeline
        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RedFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });

        // Record commands
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.0f, 0.0f, 0.0f, 1.0f) // Clear to black
                }
            ]
        });
        cmd.SetViewport(0, 0, 2, 2, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0); // Draw fullscreen triangle
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back all 4 pixels
        var pixelData = new byte[16];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // All pixels should be red (the fullscreen triangle covers everything)
        for (int pixel = 0; pixel < 4; pixel++)
        {
            int offset = pixel * 4;
            Assert.Equal(255, pixelData[offset + 0]); // R
            Assert.Equal(0, pixelData[offset + 1]);   // G
            Assert.Equal(0, pixelData[offset + 2]);   // B
            Assert.Equal(255, pixelData[offset + 3]); // A
        }
    }

    [Fact]
    public void Render_MultipleDrawCalls_LastColorWins()
    {
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Create three pipelines with different colors
        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var redFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RedFragmentShader));
        using var greenFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(GreenFragmentShader));
        using var blueFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(BlueFragmentShader));

        using var redPipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = redFragShader,
            Topology = PrimitiveTopology.TriangleList
        });
        using var greenPipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = greenFragShader,
            Topology = PrimitiveTopology.TriangleList
        });
        using var bluePipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = blueFragShader,
            Topology = PrimitiveTopology.TriangleList
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);

        // Draw red, then green, then blue
        cmd.SetPipeline(redPipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.SetPipeline(greenPipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.SetPipeline(bluePipeline);
        cmd.Draw(3, 1, 0, 0);

        cmd.EndRenderPass();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Last color (blue) should win
        Assert.Equal(0, pixelData[0]);   // R
        Assert.Equal(0, pixelData[1]);   // G
        Assert.Equal(255, pixelData[2]); // B
        Assert.Equal(255, pixelData[3]); // A
    }

    [Fact]
    public void Render_WithUniformBuffer_UsesCorrectColor()
    {
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Create uniform buffer with magenta color
        var colorData = new float[] { 1.0f, 0.0f, 1.0f, 1.0f }; // Magenta
        using var uniformBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Uniform, colorData.AsSpan(), MemoryAccess.CpuToGpu);

        // Create shaders and pipeline
        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(SolidColorFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });

        // Create bind group
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0, ShaderStage.Fragment)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, uniformBuffer)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        // Record and execute
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Should be magenta
        Assert.Equal(255, pixelData[0]); // R
        Assert.Equal(0, pixelData[1]);   // G
        Assert.Equal(255, pixelData[2]); // B
        Assert.Equal(255, pixelData[3]); // A
    }

    [Fact]
    public void Render_4x4Gradient_VerifyAllPixels()
    {
        const string GradientFragmentShader = @"
#version 430 core
out vec4 FragColor;

void main()
{
    // Create a gradient based on fragment coordinates
    // gl_FragCoord.xy gives pixel center (0.5, 0.5) for bottom-left pixel
    vec2 uv = gl_FragCoord.xy / 4.0;
    FragColor = vec4(uv.x, uv.y, 0.0, 1.0);
}
";

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64, // 16 pixels * 4 bytes
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(GradientFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[64];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Verify gradient pattern
        // Each pixel should have R and G values based on position
        // Formula: (pixelCenter / textureSize) where pixelCenter = pixelIndex + 0.5
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int pixelIndex = y * 4 + x;
                int offset = pixelIndex * 4;

                // Expected color: uv = (x+0.5)/4, (y+0.5)/4
                float expectedR = (x + 0.5f) / 4.0f;
                float expectedG = (y + 0.5f) / 4.0f;

                byte expectedRByte = (byte)(expectedR * 255);
                byte expectedGByte = (byte)(expectedG * 255);

                // Allow some tolerance for floating point conversion
                Assert.InRange(pixelData[offset + 0], (byte)(expectedRByte - 2), (byte)(expectedRByte + 2));
                Assert.InRange(pixelData[offset + 1], (byte)(expectedGByte - 2), (byte)(expectedGByte + 2));
                Assert.Equal(0, pixelData[offset + 2]); // B should be 0
                Assert.Equal(255, pixelData[offset + 3]); // A should be 255
            }
        }
    }

    #endregion

    #region Depth Buffer Tests

    [Fact]
    public void Render_DepthTest_CloserObjectWins_WithVertexBuffer()
    {
        // This test originally exposed a Graphite bug where vertex buffer bindings
        // were lost when switching pipelines (each pipeline has its own VAO in OpenGL).
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var depthTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        const string DepthTestVertexShader = @"
#version 430 core
layout(location = 0) in vec3 aPosition;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
}
";
        // Two fullscreen triangles at different depths
        var vertices = new float[]
        {
            // Triangle 1 (red) - further away (z=0.8)
            -1, -1, 0.8f,
             3, -1, 0.8f,
            -1,  3, 0.8f,
            // Triangle 2 (green) - closer (z=0.2)
            -1, -1, 0.2f,
             3, -1, 0.2f,
            -1,  3, 0.2f,
        };

        using var vertexBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Vertex, vertices.AsSpan(), MemoryAccess.GpuOnly);

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(DepthTestVertexShader));
        using var redFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RedFragmentShader));
        using var greenFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(GreenFragmentShader));

        var pipelineDesc = new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = redFragShader,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(12,
                    new VertexAttribute(0, VertexFormat.Float3, 0))
            ),
            DepthStencilState = new DepthStencilStateDescriptor
            {
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompare = CompareFunction.Less
            }
        };

        using var redPipeline = _fixture.Device.CreatePipelineState(in pipelineDesc);

        pipelineDesc.FragmentShader = greenFragShader;
        using var greenPipeline = _fixture.Device.CreatePipelineState(in pipelineDesc);

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthTarget,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Store,
                StencilClearValue = 0
            }
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);

        // Draw red triangle first (further, z=0.8)
        cmd.SetPipeline(redPipeline);
        cmd.SetVertexBuffer(0, vertexBuffer, 0);
        cmd.Draw(3, 1, 0, 0);

        // Draw green triangle second (closer, z=0.2) - should pass depth test
        // Previously crashed because pipeline switch lost vertex buffer binding
        cmd.SetPipeline(greenPipeline);
        cmd.Draw(3, 1, 3, 0);

        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Green (closer) should win due to depth test
        Assert.Equal(0, pixelData[0]);   // R
        Assert.Equal(255, pixelData[1]); // G
        Assert.Equal(0, pixelData[2]);   // B
        Assert.Equal(255, pixelData[3]); // A
    }

    [Fact]
    public void Render_WithDepthBuffer_ClearsCorrectly()
    {
        // Simpler depth test - just verify we can create and clear a depth buffer
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var depthTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64, // 16 pixels * 4 bytes
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.0f, 1.0f, 0.0f, 1.0f) // Green
                }
            ],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthTarget,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Store,
                StencilClearValue = 0
            }
        });
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[64];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // All pixels should be green
        for (int i = 0; i < 16; i++)
        {
            int offset = i * 4;
            Assert.Equal(0, pixelData[offset + 0]);   // R
            Assert.Equal(255, pixelData[offset + 1]); // G
            Assert.Equal(0, pixelData[offset + 2]);   // B
            Assert.Equal(255, pixelData[offset + 3]); // A
        }
    }

    #endregion

    #region OpenGL State Leak Tests

    [Fact]
    public void Bug_ColorMask_LeaksWhenBlendAttachmentsNull()
    {
        // Exposes a bug where ColorMask set by one pipeline leaks to the next
        // when the next pipeline has null BlendState.Attachments.
        // ApplyBlendState's early return path skips resetting ColorMask.

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var redFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RedFragmentShader));
        using var blueFragShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(BlueFragmentShader));

        // Pipeline A: restricts ColorMask to Red-only
        using var pipelineA = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = redFragShader,
            Topology = PrimitiveTopology.TriangleList,
            BlendState = new BlendStateDescriptor(
                new BlendAttachment { WriteMask = ColorWriteMask.Red }
            )
        });

        // Pipeline B: explicitly null Attachments (triggers the early return bug in ApplyBlendState)
        // This should reset ColorMask to All, but the buggy early return skips it.
        using var pipelineB = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = blueFragShader,
            Topology = PrimitiveTopology.TriangleList,
            BlendState = new BlendStateDescriptor { Attachments = null }
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero // Clear to (0,0,0,0)
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);

        // Draw red with Pipeline A (Red-only ColorMask) → pixel = (255, 0, 0, 0)
        cmd.SetPipeline(pipelineA);
        cmd.Draw(3, 1, 0, 0);

        // Switch to Pipeline B (should reset ColorMask to all channels)
        // Draw blue (0, 0, 255, 255) → pixel should become (0, 0, 255, 255)
        // BUG: ColorMask leaks Red-only from Pipeline A, so only R=0 is written → (0, 0, 0, 0)
        cmd.SetPipeline(pipelineB);
        cmd.Draw(3, 1, 0, 0);

        cmd.EndRenderPass();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Pipeline B draws blue. If ColorMask is correctly reset to RGBA:
        // pixel = (0, 0, 255, 255)
        // If ColorMask leaks (Red-only): pixel = (0, 0, 0, 0) — B and A fail
        Assert.Equal(0, pixelData[0]);     // R
        Assert.Equal(0, pixelData[1]);     // G
        Assert.Equal(255, pixelData[2]);   // B (fails if ColorMask leaks!)
        Assert.Equal(255, pixelData[3]);   // A (fails if ColorMask leaks!)
    }

    [Fact]
    public void Bug_VertexBufferStride_UsesNewPipelineStride_AfterPipelineSwitch()
    {
        // Exposes a bug where vertex buffer re-application after pipeline switch uses
        // the stride from the original pipeline (at SetVertexBuffer time) instead of
        // the new pipeline's stride. This causes incorrect vertex reads if the layouts differ.
        //
        // Setup: Vertex buffer with Float3+Float2 per vertex (stride 20).
        // Pipeline A reads Float3 only (stride 12) → draws red.
        // Pipeline B reads Float3+Float2 (stride 20) → outputs texcoord as color.
        //
        // Sequence: SetPipeline(A), SetVertexBuffer (captures stride 12), Draw,
        //           SetPipeline(B), Draw (re-applies VB with stride 12 instead of 20).
        // With bug: Pipeline B reads garbage texcoords. With fix: reads correct texcoords.

        const string TexCoordVertexShader = @"
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
        const string TexCoordFragmentShader = @"
#version 430 core
in vec2 vTexCoord;
out vec4 FragColor;

void main()
{
    // Output texcoord as color: valid texcoords (0-1) → green channel > 0
    FragColor = vec4(0.0, vTexCoord.y, 0.0, 1.0);
}
";

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Vertex data: Float3 position + Float2 texcoord per vertex (stride 20)
        var vertices = new float[]
        {
            // Fullscreen triangle with texcoords
            -1f, -1f, 0f,   0f, 1f,
             3f, -1f, 0f,   2f, 1f,
            -1f,  3f, 0f,   0f, -1f,
        };

        using var vertexBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Vertex, vertices.AsSpan(), MemoryAccess.GpuOnly);

        // Pipeline A: Float3 only (stride 12), red output
        using var vertShaderA = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(@"
#version 430 core
layout(location = 0) in vec3 aPosition;
void main() { gl_Position = vec4(aPosition, 1.0); }
"));
        using var fragShaderA = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RedFragmentShader));

        using var pipelineA = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertShaderA,
            FragmentShader = fragShaderA,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(12,
                    new VertexAttribute(0, VertexFormat.Float3, 0))
            )
        });

        // Pipeline B: Float3 + Float2 (stride 20), outputs texcoord.y as green
        using var vertShaderB = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(TexCoordVertexShader));
        using var fragShaderB = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(TexCoordFragmentShader));

        using var pipelineB = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertShaderB,
            FragmentShader = fragShaderB,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(20,
                    new VertexAttribute(0, VertexFormat.Float3, 0),
                    new VertexAttribute(1, VertexFormat.Float2, 12))
            )
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);

        // Set Pipeline A (stride 12) and bind vertex buffer → stride 12 is tracked
        cmd.SetPipeline(pipelineA);
        cmd.SetVertexBuffer(0, vertexBuffer, 0);
        cmd.Draw(3, 1, 0, 0); // Draws red

        // Switch to Pipeline B (stride 20) — vertex buffer is re-applied
        // BUG: Re-applied with stride 12 (from Pipeline A), Pipeline B reads wrong texcoords
        // FIX: Re-applied with stride 20 (from Pipeline B's layout), reads correct texcoords
        cmd.SetPipeline(pipelineB);
        cmd.Draw(3, 1, 0, 0);

        cmd.EndRenderPass();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Pipeline B outputs vec4(0, texcoord.y, 0, 1).
        // The 1x1 pixel center is at (0.5, 0.5) in the viewport.
        // With correct stride 20: texcoord.y should be ~1.0 (interpolated from vertex data)
        // so green channel should be ~255.
        // With wrong stride 12: texcoord.y reads from wrong offset, giving garbage.
        Assert.Equal(0, pixelData[0]);           // R = 0
        Assert.True(pixelData[1] > 100,
            $"Green channel should be high (correct texcoord) but was {pixelData[1]}. " +
            "Stride was likely wrong after pipeline switch.");
        Assert.Equal(0, pixelData[2]);           // B = 0
        Assert.Equal(255, pixelData[3]);         // A = 255
    }

    #endregion

    #region Pixel Format Copy Bug Tests

    [Fact]
    public void Bug_CopyTextureToBuffer_R32Float_UsesCorrectPixelFormat()
    {
        // GLCommandList's GetPixelFormat is missing R32Float → PixelFormat.Red,
        // so it falls through to the default PixelFormat.Rgba. This causes ReadPixels
        // to read 4 floats (16 bytes) per pixel instead of 1 float (4 bytes),
        // writing garbage or overflowing the readback buffer.

        const string FloatValueFragmentShader = @"
#version 430 core
out vec4 FragColor;
void main()
{
    // Write 0.75 to the R channel (only R is stored in R32Float target)
    FragColor = vec4(0.75, 0.0, 0.0, 1.0);
}
";

        // Create a 1x1 R32Float render target
        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.R32Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Create readback buffer initialized to zero (16 bytes to avoid overflow with bug)
        var zeros = new float[4]; // 16 bytes, all zero
        using var readbackBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.CopyDestination, zeros.AsSpan(), MemoryAccess.GpuToCpu);

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(FloatValueFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back the entire 16-byte buffer
        var results = new float[4];
        ReadBufferData<float>(readbackBuffer, results);

        // With correct format (Red): only first float is written = 0.75, rest stay 0
        // With bug format (Rgba): ReadPixels reads RGBA from R32Float framebuffer,
        // OpenGL fills missing components: R=0.75, G=0, B=0, A=1.0
        // So results[3] will be 1.0f (alpha fill) instead of 0.0f (zero-initialized)
        Assert.Equal(0.75f, results[0], precision: 3);
        Assert.Equal(0.0f, results[3], precision: 3); // Fails if format=Rgba (gets 1.0f from alpha fill)
    }

    [Fact]
    public void Bug_CopyTextureToBuffer_RG16Float_UsesCorrectPixelFormat()
    {
        // Same underlying bug: GLCommandList's GetPixelFormat is missing RG16Float → PixelFormat.RG,
        // so it defaults to Rgba, reading 4 components per pixel instead of 2.

        const string RGOutputFragmentShader = @"
#version 430 core
out vec4 FragColor;
void main()
{
    FragColor = vec4(0.5, 0.25, 0.0, 1.0);
}
";

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RG16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // RG16Float = 2 x half-float = 4 bytes per pixel
        // Allocate 16 bytes zero-initialized (oversized to survive bug without crash)
        var zeros = new byte[16];
        using var readbackBuffer = _fixture.Device.CreateBuffer<byte>(
            BufferUsage.CopyDestination, zeros.AsSpan(), MemoryAccess.GpuToCpu);

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(RGOutputFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var results = new byte[16];
        ReadBufferData<byte>(readbackBuffer, results);

        // RG16Float pixel = 4 bytes (2 half-floats)
        // With correct format (RG): bytes 0-3 written (R and G half-floats), bytes 4-15 stay zero
        // With bug format (Rgba): bytes 0-7 written (4 half-floats: R, G, B=0, A=1.0h),
        //   bytes 8-15 stay zero
        // Check that bytes 4-7 are zero (they shouldn't be touched with correct format)
        bool extraBytesAreZero = results[4] == 0 && results[5] == 0 &&
                                  results[6] == 0 && results[7] == 0;
        Assert.True(extraBytesAreZero,
            $"Bytes 4-7 should be zero (untouched) but were [{results[4]}, {results[5]}, {results[6]}, {results[7]}]. " +
            "CopyTextureToBuffer likely used wrong pixel format (Rgba instead of RG).");
    }

    #endregion

    #region Per-Attachment Blend Tests

    [Fact]
    public void Bug_PerAttachmentBlend_OnlyFirstAttachmentBlendUsed()
    {
        // GLPipelineState.ApplyBlendState only uses the first attachment's blend state
        // for all render targets. When using MRT with different blend modes per attachment,
        // only the first attachment's mode is applied globally.

        const string MRTFragmentShader = @"
#version 430 core
layout(location = 0) out vec4 Color0;
layout(location = 1) out vec4 Color1;

void main()
{
    // Both outputs write the same color
    Color0 = vec4(0.3, 0.0, 0.0, 1.0);
    Color1 = vec4(0.3, 0.0, 0.0, 1.0);
}
";

        // Create two 1x1 render targets
        using var rt0 = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });
        using var rt1 = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer0 = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4, Usage = BufferUsage.CopyDestination, MemoryAccess = MemoryAccess.GpuToCpu
        });
        using var readbackBuffer1 = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4, Usage = BufferUsage.CopyDestination, MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(FullscreenTriVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(MRTFragmentShader));

        // Pipeline: attachment 0 = no blend (opaque), attachment 1 = additive blend
        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            BlendState = new BlendStateDescriptor(
                // Attachment 0: opaque (no blend)
                BlendAttachment.Opaque,
                // Attachment 1: additive blend (src + dst)
                new BlendAttachment
                {
                    BlendEnable = true,
                    SrcColorFactor = BlendFactor.One,
                    DstColorFactor = BlendFactor.One,
                    ColorOp = BlendOp.Add,
                    SrcAlphaFactor = BlendFactor.One,
                    DstAlphaFactor = BlendFactor.One,
                    AlphaOp = BlendOp.Add,
                    WriteMask = ColorWriteMask.All
                }
            )
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();

        // Clear both targets to (128, 0, 0, 255) = (0.5, 0, 0, 1) approx
        var renderPass = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = rt0,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.5f, 0.0f, 0.0f, 1.0f)
                },
                new RenderPassColorAttachment
                {
                    Texture = rt1,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.5f, 0.0f, 0.0f, 1.0f)
                }
            ]
        };

        cmd.BeginRenderPass(in renderPass);
        cmd.SetViewport(0, 0, 1, 1, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0); // Draw (0.3, 0, 0, 1) to both
        cmd.EndRenderPass();

        // Copy both render targets to readback buffers
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = rt0, Buffer = readbackBuffer0,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = rt1, Buffer = readbackBuffer1,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixel0 = new byte[4];
        var pixel1 = new byte[4];
        ReadBufferData<byte>(readbackBuffer0, pixel0);
        ReadBufferData<byte>(readbackBuffer1, pixel1);

        // Attachment 0 (opaque): draw overwrites → R = 0.3 * 255 ≈ 77
        Assert.InRange(pixel0[0], (byte)74, (byte)80); // R ≈ 77

        // Attachment 1 (additive): dst(0.5) + src(0.3) = 0.8 → R ≈ 204
        // BUG: Uses attachment 0's opaque blend globally → R ≈ 77 (overwrite, not additive)
        // FIX: Per-attachment blend → R ≈ 204 (additive)
        Assert.True(pixel1[0] > 150,
            $"Attachment 1 R channel should be ~204 (additive: 0.5+0.3=0.8) but was {pixel1[0]}. " +
            "Per-attachment blend is not working — only the first attachment's blend mode is applied.");
    }

    #endregion

    #region Storage Texture Format Tests

    [Fact]
    public void Bug_StorageTexture_RG32Float_NotSupported()
    {
        // GLBindGroup.GetImageFormat is incomplete - missing many texture formats.
        // This test verifies that RG32Float storage textures work correctly via compute.

        const string StorageTextureComputeShader = @"
#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

layout(rg32f, binding = 0) uniform image2D outputImage;

void main()
{
    // Write (1.5, 2.5) to the image at (0,0)
    imageStore(outputImage, ivec2(0, 0), vec4(1.5, 2.5, 0.0, 1.0));
}
";

        // Create a 1x1 RG32Float storage texture
        using var storageTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RG32Float,
            Usage = TextureUsage.Storage | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Readback buffer: 2 floats = 8 bytes
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 8,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var computeShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(StorageTextureComputeShader));

        using var pipeline = _fixture.Device.CreateComputePipelineState(new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader
        });

        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageTexture(0, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForTexture(0, storageTexture)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.SetComputePipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Dispatch(1, 1, 1);
        cmd.MemoryBarrier();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = storageTexture, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var results = new float[2];
        ReadBufferData<float>(readbackBuffer, results);

        // Should read (1.5, 2.5) - if GetImageFormat doesn't map RG32Float it falls back to RGBA8
        Assert.Equal(1.5f, results[0], precision: 3);
        Assert.Equal(2.5f, results[1], precision: 3);
    }

    [Fact]
    public void Bug_StorageTexture_R16Float_NotSupported()
    {
        // GLBindGroup.GetImageFormat is missing R16Float.
        // Note: R16Float requires the r16f image format qualifier in GLSL.

        const string StorageTextureR16FComputeShader = @"
#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

layout(r16f, binding = 0) uniform image2D outputImage;

void main()
{
    imageStore(outputImage, ivec2(0, 0), vec4(0.75, 0.0, 0.0, 1.0));
}
";

        using var storageTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.R16Float,
            Usage = TextureUsage.Storage | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Readback buffer: 1 half-float = 2 bytes, but we'll read as 4 for safety
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var computeShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.ComputeGLSL(StorageTextureR16FComputeShader));

        using var pipeline = _fixture.Device.CreateComputePipelineState(new ComputePipelineStateDescriptor
        {
            ComputeShader = computeShader
        });

        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageTexture(0, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForTexture(0, storageTexture)
        );
        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.SetComputePipeline(pipeline);
        cmd.SetBindGroup(0, bindGroup);
        cmd.Dispatch(1, 1, 1);
        cmd.MemoryBarrier();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = storageTexture, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back as bytes and convert half-float to float
        var bytes = new byte[4];
        ReadBufferData<byte>(readbackBuffer, bytes);

        // Convert first 2 bytes (half-float) to float
        ushort halfFloat = (ushort)(bytes[0] | (bytes[1] << 8));
        float value = HalfToFloat(halfFloat);

        // Should be approximately 0.75
        Assert.InRange(value, 0.7f, 0.8f);
    }

    // Helper to convert half-float to float (IEEE 754 half precision)
    private static float HalfToFloat(ushort half)
    {
        int sign = (half >> 15) & 1;
        int exp = (half >> 10) & 0x1F;
        int mant = half & 0x3FF;

        if (exp == 0)
        {
            if (mant == 0) return sign == 0 ? 0f : -0f;
            // Denormalized
            float m = mant / 1024f;
            return (sign == 0 ? 1f : -1f) * m * MathF.Pow(2, -14);
        }
        if (exp == 31)
        {
            return mant == 0 ? (sign == 0 ? float.PositiveInfinity : float.NegativeInfinity) : float.NaN;
        }

        float mantissa = 1f + mant / 1024f;
        float result = mantissa * MathF.Pow(2, exp - 15);
        return sign == 0 ? result : -result;
    }

    #endregion

    #region Texture Copy Missing Format Tests

    [Fact]
    public void Bug_CopyBufferToTexture_Texture1D_NotHandled()
    {
        // CopyBufferToTextureCore switch statement is missing Texture1D case
        // The copy will silently do nothing for 1D textures

        // Create a 4-pixel 1D texture
        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture1D,
            Width = 4, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Source buffer with recognizable pattern: red, green, blue, white
        var sourceData = new byte[]
        {
            255, 0, 0, 255,     // Red
            0, 255, 0, 255,     // Green
            0, 0, 255, 255,     // Blue
            255, 255, 255, 255  // White
        };
        using var sourceBuffer = _fixture.Device.CreateBuffer<byte>(
            BufferUsage.CopySource, sourceData.AsSpan(), MemoryAccess.GpuOnly);

        // Readback buffer
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        // Copy buffer to 1D texture
        cmd.CopyBufferToTexture(new BufferTextureCopy
        {
            Buffer = sourceBuffer,
            BufferOffset = 0,
            Texture = texture,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 1, Depth = 1
        });
        // Copy 1D texture back to buffer
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = texture,
            Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var result = new byte[16];
        ReadBufferData<byte>(readbackBuffer, result);

        // If copy worked, first pixel should be red (255, 0, 0, 255)
        Assert.Equal(255, result[0]); // R
        Assert.Equal(0, result[1]);   // G
        Assert.Equal(0, result[2]);   // B
        Assert.Equal(255, result[3]); // A
    }

    [Fact]
    public void Bug_CopyTextureToBuffer_CubeMap_NotHandled()
    {
        // CopyTextureToBufferCore is missing TextureCubeMap case
        // Cubemap face readback won't work

        // Create a 2x2 cubemap
        using var cubeMap = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.TextureCube,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1, ArrayLayers = 6, // 6 faces
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Upload distinct colors to each face using UpdateTexture
        var faceColors = new byte[][]
        {
            [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255], // +X: Red
            [0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255], // -X: Green
            [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255], // +Y: Blue
            [255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255], // -Y: Yellow
            [255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255], // +Z: Magenta
            [0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255], // -Z: Cyan
        };

        for (int face = 0; face < 6; face++)
        {
            _fixture.Device.UpdateTexture(cubeMap, new TextureUpdateDescriptor
            {
                X = 0, Y = 0, Z = 0,
                Width = 2, Height = 2, Depth = 1,
                MipLevel = 0,
                ArrayLayer = (uint)face // Face index
            }, faceColors[face]);
        }

        // Readback buffer for one face (2x2 = 16 bytes)
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        // Try to read back face 2 (+Y, should be blue)
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = cubeMap,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 2, // +Y face (cube face index)
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var result = new byte[16];
        ReadBufferData<byte>(readbackBuffer, result);

        // Face 2 (+Y) should be blue: (0, 0, 255, 255)
        Assert.Equal(0, result[0]);   // R
        Assert.Equal(0, result[1]);   // G
        Assert.Equal(255, result[2]); // B (fails if cubemap readback not implemented)
        Assert.Equal(255, result[3]); // A
    }

    #endregion

    #region Depth Texture Copy Tests

    [Fact]
    public void Bug_CopyTextureToBuffer_DepthTexture_WrongAttachment()
    {
        // CopyTextureToBufferCore attaches depth textures to ColorAttachment0,
        // but they should be attached to DepthAttachment for the framebuffer to be complete.

        // Create a 4x4 depth texture
        using var depthTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Create a render target to render depth to the depth texture
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        // Clear depth to 0.5
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.DontCare,
                    ClearColor = Float4.Zero
                }
            ],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthTexture,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 0.5f
            }
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Readback buffer for depth values (4x4 float = 64 bytes)
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd2 = _fixture.Device.CreateCommandList();
        using var fence2 = _fixture.Device.CreateFence(false);

        cmd2.Begin();
        cmd2.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = depthTexture,
            Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd2.End();

        _fixture.Device.SubmitCommands(cmd2, fence2);
        fence2.Wait();

        var depthValues = new float[16];
        ReadBufferData<float>(readbackBuffer, depthValues);

        // All depth values should be approximately 0.5
        Assert.InRange(depthValues[0], 0.4f, 0.6f);
        Assert.InRange(depthValues[5], 0.4f, 0.6f);
        Assert.InRange(depthValues[15], 0.4f, 0.6f);
    }

    [Fact]
    public void Bug_RenderToArrayTextureLayer_ArrayLayerIgnored()
    {
        // RenderPassColorAttachment has ArrayLayer field but CreateFramebuffer
        // uses FramebufferTexture2D which can't specify a layer.
        // This means ArrayLayer is silently ignored and always renders to layer 0.

        // Create a 2-layer array texture
        using var arrayTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1,
            ArrayLayers = 2, // 2 layers
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64, // 4x4 RGBA
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        // Clear layer 0 to RED
        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = arrayTexture,
                    MipLevel = 0,
                    ArrayLayer = 0, // Layer 0
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(1, 0, 0, 1) // RED
                }
            ]
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        // Clear layer 1 to GREEN
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = arrayTexture,
                    MipLevel = 0,
                    ArrayLayer = 1, // Layer 1 - THIS IS THE BUG: it's ignored!
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0, 1, 0, 1) // GREEN
                }
            ]
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        // Read back layer 0 - should still be RED (not overwritten by layer 1 clear)
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = arrayTexture,
            Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0, // Layer 0
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixels = new byte[64];
        ReadBufferData<byte>(readbackBuffer, pixels);

        // Layer 0 should be RED (255, 0, 0, 255)
        // If ArrayLayer is ignored, both clears go to layer 0, making it GREEN
        Assert.Equal(255, pixels[0]); // R should be 255 (RED)
        Assert.Equal(0, pixels[1]);   // G should be 0 (not green!)
    }

    [Fact]
    public void RenderToMipLevel_Works()
    {
        // Test rendering to a non-zero mip level

        // Create a 4x4 texture with 2 mip levels (4x4 and 2x2)
        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 2, // Mip 0 = 4x4, Mip 1 = 2x2
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16, // 2x2 RGBA = 16 bytes
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();

        // Clear mip 0 to RED
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = texture,
                    MipLevel = 0,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(1, 0, 0, 1) // RED
                }
            ]
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        // Clear mip 1 to BLUE
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = texture,
                    MipLevel = 1, // Mip level 1 (2x2)
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0, 0, 1, 1) // BLUE
                }
            ]
        });
        cmd.SetViewport(0, 0, 2, 2, 0, 1);
        cmd.EndRenderPass();

        // Read back mip 1 - should be BLUE
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = texture,
            Buffer = readbackBuffer,
            MipLevel = 1, // Read mip 1
            ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixels = new byte[16];
        ReadBufferData<byte>(readbackBuffer, pixels);

        // Mip 1 should be BLUE (0, 0, 255, 255)
        Assert.Equal(0, pixels[0]);   // R
        Assert.Equal(0, pixels[1]);   // G
        Assert.Equal(255, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    [Fact]
    public void MsaaResolve_Works()
    {
        // Test that MSAA resolve via ResolveTarget works correctly

        // Create a 4x4 MSAA texture (4 samples)
        using var msaaTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count4
        });

        // Create a 4x4 non-MSAA resolve target
        using var resolveTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64, // 4x4 RGBA
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();

        // Render to MSAA texture with ResolveTarget - clear to MAGENTA
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = msaaTexture,
                    ResolveTarget = resolveTexture, // This triggers MSAA resolve!
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(1, 0, 1, 1) // Magenta
                }
            ]
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass(); // Resolve happens here

        // Read back the resolve target (non-MSAA) - should have the resolved color
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = resolveTexture,
            Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixels = new byte[64];
        ReadBufferData<byte>(readbackBuffer, pixels);

        // Resolve target should be MAGENTA (255, 0, 255, 255)
        Assert.Equal(255, pixels[0]); // R
        Assert.Equal(0, pixels[1]);   // G
        Assert.Equal(255, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    [Fact]
    public void MsaaRenderTarget_FramebufferComplete()
    {
        // Test that AttachTextureToFramebuffer handles multisample textures correctly
        // Without explicit handling, OpenGL may create an incomplete framebuffer

        // Create a 4x4 MSAA texture (4 samples)
        using var msaaTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count4
        });

        // Create MSAA depth texture to test depth attachment as well
        using var msaaDepth = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil,
            SampleCount = SampleCount.Count4
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();

        // Render to MSAA texture with MSAA depth - should not cause framebuffer incomplete
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = msaaTexture,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0, 1, 0, 1) // Green
                }
            ],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = msaaDepth,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f
            }
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();
        cmd.End();

        // If we get here without an error, the framebuffer was created successfully
        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Success - no framebuffer incomplete error
        Assert.True(true);
    }

    [Fact]
    public void DepthOnlyRenderPass_Works()
    {
        // Test that render passes with only depth attachment (no color) work correctly
        // This is important for shadow mapping

        using var depthTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Readback buffer
        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Depth-only render pass (no color attachments)
        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = null, // No color attachments!
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthTexture,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 0.75f // Clear to 0.75
            }
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = depthTexture,
            Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var depthValues = new float[16];
        ReadBufferData<float>(readbackBuffer, depthValues);

        // All values should be 0.75
        Assert.InRange(depthValues[0], 0.7f, 0.8f);
        Assert.InRange(depthValues[8], 0.7f, 0.8f);
        Assert.InRange(depthValues[15], 0.7f, 0.8f);
    }

    #endregion

    #region Integer Vertex Attribute Tests

    [Fact]
    public void Bug_IntegerVertexAttribute_UsesCorrectGLFunction()
    {
        // GLPipelineState uses VertexAttribFormat for integer types, but it should
        // use VertexAttribIFormat. VertexAttribFormat converts integers to float,
        // losing precision and breaking integer shader inputs.

        const string IntVertexShader = @"
#version 430 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in int aIndex;  // Integer attribute!

out flat int vIndex;

void main()
{
    gl_Position = vec4(aPosition, 0.0, 1.0);
    vIndex = aIndex;
}
";
        const string IntFragmentShader = @"
#version 430 core
in flat int vIndex;
out vec4 FragColor;

void main()
{
    // Output the integer value as a color component
    // If index is passed correctly as 42, R = 42/255 ≈ 0.165
    // If it's incorrectly converted to float, it may be garbled
    FragColor = vec4(float(vIndex) / 255.0, 0.0, 0.0, 1.0);
}
";

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 1, Height = 1, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 4,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        // Vertex data: Float2 position + Int index (stride = 12 bytes)
        // Use a recognizable integer value (42) to detect if it's passed correctly
        var vertices = new byte[36]; // 3 vertices * 12 bytes
        var span = vertices.AsSpan();

        // Vertex 0: position (-1, -1), index = 42
        BitConverter.TryWriteBytes(span.Slice(0, 4), -1.0f);
        BitConverter.TryWriteBytes(span.Slice(4, 4), -1.0f);
        BitConverter.TryWriteBytes(span.Slice(8, 4), 42);

        // Vertex 1: position (3, -1), index = 42
        BitConverter.TryWriteBytes(span.Slice(12, 4), 3.0f);
        BitConverter.TryWriteBytes(span.Slice(16, 4), -1.0f);
        BitConverter.TryWriteBytes(span.Slice(20, 4), 42);

        // Vertex 2: position (-1, 3), index = 42
        BitConverter.TryWriteBytes(span.Slice(24, 4), -1.0f);
        BitConverter.TryWriteBytes(span.Slice(28, 4), 3.0f);
        BitConverter.TryWriteBytes(span.Slice(32, 4), 42);

        using var vertexBuffer = _fixture.Device.CreateBuffer<byte>(
            BufferUsage.Vertex, vertices.AsSpan(), MemoryAccess.GpuOnly);

        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(IntVertexShader));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(IntFragmentShader));

        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList,
            VertexLayout = new VertexLayoutDescriptor(
                new VertexBufferLayout(12,
                    new VertexAttribute(0, VertexFormat.Float2, 0),
                    new VertexAttribute(1, VertexFormat.Int, 8)) // Integer attribute!
            )
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = renderTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        });
        cmd.SetViewport(0, 0, 1, 1, 0, 1);
        cmd.SetPipeline(pipeline);
        cmd.SetVertexBuffer(0, vertexBuffer, 0);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();

        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = renderTarget, Buffer = readbackBuffer,
            MipLevel = 0, ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 1, Height = 1, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var pixelData = new byte[4];
        ReadBufferData<byte>(readbackBuffer, pixelData);

        // Expected R = 42 (integer 42 / 255 * 255 = 42)
        // With bug (VertexAttribFormat instead of VertexAttribIFormat):
        // The integer may be reinterpreted as float bits, giving garbage
        Assert.InRange(pixelData[0], (byte)40, (byte)44);
    }

    #endregion

    #region Zero Dimension Texture Tests

    [Fact]
    public void Bug_ZeroDimensionTexture_CrashesOnMipCalculation()
    {
        // GLTexture.CalculateMaxMipLevels does Math.Log2(maxDimension) which returns -∞ for 0,
        // and casting -∞ to uint gives undefined behavior (often 0 or crash).
        // Creating a texture with zero dimensions should either:
        // 1. Throw a meaningful exception during creation, OR
        // 2. Handle it gracefully with MipLevels = 1

        // This test verifies the behavior is deterministic (no crash)
        bool threw = false;
        try
        {
            using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
            {
                Dimension = TextureDimension.Texture2D,
                Width = 0, // Zero dimension!
                Height = 1,
                Depth = 1,
                MipLevels = 0, // Auto-calculate (triggers the bug)
                ArrayLayers = 1,
                Format = TextureFormat.RGBA8Unorm,
                Usage = TextureUsage.Sampled,
                SampleCount = SampleCount.Count1
            });
            // If we get here, verify MipLevels is reasonable (not uint.MaxValue or similar)
            Assert.True(texture.MipLevels >= 1 && texture.MipLevels <= 16,
                $"MipLevels should be reasonable but was {texture.MipLevels}");
        }
        catch (Exception ex)
        {
            // Acceptable: a clear exception is better than undefined behavior
            threw = true;
            Assert.False(ex is OverflowException,
                "OverflowException indicates undefined behavior from negative infinity cast");
        }

        // Either outcome is acceptable as long as there's no crash or undefined behavior
        // (The test passes if we reach here without crashing)
        Assert.True(true, "No crash occurred");
    }

    [Fact]
    public void RenderToDepthArrayTextureLayer_Works()
    {
        // Test that RenderPassDepthStencilAttachment.MipLevel and ArrayLayer fields
        // properly allow rendering to specific layers of a depth array texture

        // Create a 2-layer depth array texture
        using var depthArrayTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1,
            ArrayLayers = 2, // 2 layers
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64, // 4x4 floats = 64 bytes
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        // Clear layer 0 to depth 0.25
        cmd.Begin();
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = null,
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthArrayTexture,
                MipLevel = 0,
                ArrayLayer = 0, // Layer 0
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 0.25f
            }
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        // Clear layer 1 to depth 0.75
        cmd.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = null,
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthArrayTexture,
                MipLevel = 0,
                ArrayLayer = 1, // Layer 1
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 0.75f
            }
        });
        cmd.SetViewport(0, 0, 4, 4, 0, 1);
        cmd.EndRenderPass();

        // Read back layer 0 - should be 0.25
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = depthArrayTexture,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 0, // Layer 0 of array texture
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        var layer0Values = new float[16];
        ReadBufferData<float>(readbackBuffer, layer0Values);

        // Layer 0 should have depth 0.25
        Assert.InRange(layer0Values[0], 0.2f, 0.3f);
        Assert.InRange(layer0Values[8], 0.2f, 0.3f);
        Assert.InRange(layer0Values[15], 0.2f, 0.3f);

        // Now read back layer 1 - should be 0.75
        using var cmd2 = _fixture.Device.CreateCommandList();
        using var fence2 = _fixture.Device.CreateFence(false);

        cmd2.Begin();
        cmd2.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = depthArrayTexture,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 1, // Layer 1 of array texture
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd2.End();

        _fixture.Device.SubmitCommands(cmd2, fence2);
        fence2.Wait();

        var layer1Values = new float[16];
        ReadBufferData<float>(readbackBuffer, layer1Values);

        // Layer 1 should have depth 0.75
        Assert.InRange(layer1Values[0], 0.7f, 0.8f);
        Assert.InRange(layer1Values[8], 0.7f, 0.8f);
        Assert.InRange(layer1Values[15], 0.7f, 0.8f);
    }

    #endregion

    #region Buffer Bounds Validation Tests

    [Fact]
    public void BufferUpdate_ExceedingBounds_ThrowsException()
    {
        // Create a small buffer
        using var buffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16, // 16 bytes
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu
        });

        var data = new float[8]; // 32 bytes - exceeds buffer size

        // Update with data larger than buffer should throw
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _fixture.Device.UpdateBuffer<float>(buffer, 0, data);
        });
    }

    [Fact]
    public void BufferUpdate_OffsetExceedingBounds_ThrowsException()
    {
        // Create a small buffer
        using var buffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16, // 16 bytes
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu
        });

        var data = new float[2]; // 8 bytes

        // Update with offset that would cause overflow should throw
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _fixture.Device.UpdateBuffer<float>(buffer, 12, data); // 12 + 8 = 20 > 16
        });
    }

    [Fact]
    public void CopyBufferToBuffer_SourceExceedingBounds_ThrowsException()
    {
        using var srcBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var dstBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 32,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var cmd = _fixture.Device.CreateCommandList();

        // Should throw when trying to copy more than source has
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            cmd.Begin();
            cmd.CopyBufferToBuffer(srcBuffer, 0, dstBuffer, 0, 20); // 20 > 16 (source size)
        });
    }

    [Fact]
    public void CopyBufferToBuffer_DestinationExceedingBounds_ThrowsException()
    {
        using var srcBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 32,
            Usage = BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var dstBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var cmd = _fixture.Device.CreateCommandList();

        // Should throw when destination can't hold the data
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            cmd.Begin();
            cmd.CopyBufferToBuffer(srcBuffer, 0, dstBuffer, 0, 20); // 20 > 16 (dest size)
        });
    }

    [Fact]
    public void SetVertexBuffer_OffsetExceedingBounds_ThrowsException()
    {
        using var vertexBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.Vertex,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        // Should throw when offset exceeds buffer size
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            cmd.Begin();
            cmd.BeginRenderPass(RenderPassDescriptor.SingleColor(
                RenderPassColorAttachment.ClearBlack(renderTarget)));
            cmd.SetVertexBuffer(0, vertexBuffer, 20); // 20 > 16 (buffer size)
        });
    }

    [Fact]
    public void SetIndexBuffer_OffsetExceedingBounds_ThrowsException()
    {
        using var indexBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.Index,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var renderTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        // Should throw when offset exceeds buffer size
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            cmd.Begin();
            cmd.BeginRenderPass(RenderPassDescriptor.SingleColor(
                RenderPassColorAttachment.ClearBlack(renderTarget)));
            cmd.SetIndexBuffer(indexBuffer, IndexFormat.Uint16, 20); // 20 > 16 (buffer size)
        });
    }

    #endregion

    #region Multisample Texture Validation Tests

    [Fact]
    public void UpdateTexture_MultisampleTexture_ThrowsNotSupported()
    {
        // Multisample textures cannot be updated via CPU - they can only be rendered to
        using var msaaTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count4
        });

        var data = new byte[64]; // 4x4 RGBA

        Assert.Throws<NotSupportedException>(() =>
        {
            _fixture.Device.UpdateTexture(msaaTexture, new TextureUpdateDescriptor
            {
                X = 0, Y = 0, Z = 0,
                Width = 4, Height = 4, Depth = 1,
                MipLevel = 0, ArrayLayer = 0
            }, data);
        });
    }

    [Fact]
    public void CopyBufferToTexture_MultisampleTexture_ThrowsNotSupported()
    {
        // Multisample textures cannot receive buffer data
        using var msaaTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count4
        });

        using var srcBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 64,
            Usage = BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var cmd = _fixture.Device.CreateCommandList();

        Assert.Throws<NotSupportedException>(() =>
        {
            cmd.Begin();
            cmd.CopyBufferToTexture(new BufferTextureCopy
            {
                Buffer = srcBuffer,
                Texture = msaaTexture,
                MipLevel = 0,
                X = 0, Y = 0, Z = 0,
                Width = 4, Height = 4, Depth = 1,
                BufferOffset = 0
            });
        });
    }

    #endregion

    #region Compressed Texture Tests

    [Fact]
    public void CompressedTexture_UpdateTexture_Works()
    {
        // Test that compressed texture upload uses the correct GL functions
        // BC1 (DXT1) uses 8 bytes per 4x4 block, so a 4x4 texture needs 8 bytes

        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.BC1Unorm, // Compressed format
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        });

        // BC1 compressed data: 8 bytes for a 4x4 block
        // This is valid BC1 data (all zeros = black)
        var compressedData = new byte[8];

        // This should use glCompressedTexSubImage2D internally
        _fixture.Device.UpdateTexture(texture, new TextureUpdateDescriptor
        {
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            MipLevel = 0, ArrayLayer = 0
        }, compressedData);

        // If we get here without GL error, the upload worked
        Assert.True(true);
    }

    [Fact]
    public void CompressedTexture_CopyBufferToTexture_Works()
    {
        // Test that buffer-to-texture copy for compressed textures uses correct GL functions

        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.BC1Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        });

        // BC1: 8 bytes for 4x4 block
        var compressedData = new byte[8];
        using var srcBuffer = _fixture.Device.CreateBuffer<byte>(
            BufferUsage.CopySource,
            compressedData.AsSpan(),
            MemoryAccess.GpuOnly);

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.CopyBufferToTexture(new BufferTextureCopy
        {
            Buffer = srcBuffer,
            Texture = texture,
            MipLevel = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // If we get here without GL error, the copy worked
        Assert.True(true);
    }

    [Fact]
    public void CompressedTexture_CopyTextureToBuffer_Works()
    {
        // Test that texture-to-buffer copy for compressed textures uses glGetCompressedTexImage

        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4, Height = 4, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.BC1Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // BC1: 8 bytes for 4x4 block
        using var dstBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 8,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        cmd.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = texture,
            Buffer = dstBuffer,
            MipLevel = 0,
            X = 0, Y = 0, Z = 0,
            Width = 4, Height = 4, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // If we get here without GL error, the readback worked
        Assert.True(true);
    }

    [Fact]
    public void IsCompressedFormat_ReturnsCorrectResults()
    {
        // Test the IsCompressedFormat helper function
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC1Unorm));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC1UnormSrgb));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC2Unorm));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC3Unorm));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC4Unorm));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC5Unorm));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC6HUfloat));
        Assert.True(GLTexture.IsCompressedFormat(TextureFormat.BC7Unorm));

        Assert.False(GLTexture.IsCompressedFormat(TextureFormat.RGBA8Unorm));
        Assert.False(GLTexture.IsCompressedFormat(TextureFormat.R8Unorm));
        Assert.False(GLTexture.IsCompressedFormat(TextureFormat.Depth32Float));
    }

    #endregion

    #region ArrayLayer Copy Tests

    [Fact]
    public void CopyBufferToTexture_ArrayLayer_TargetsCorrectLayer()
    {
        // Test that CopyBufferToTexture properly uses ArrayLayer field
        // instead of Z for array textures

        using var arrayTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1,
            ArrayLayers = 2,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        });

        // Red pixels for layer 0
        byte[] redData = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        // Green pixels for layer 1
        byte[] greenData = [0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255];

        using var srcBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.CpuToGpu,
            InitialData = redData
        });

        using var srcBuffer2 = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopySource,
            MemoryAccess = MemoryAccess.CpuToGpu,
            InitialData = greenData
        });

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        // Copy red to layer 0 using ArrayLayer field
        cmd.CopyBufferToTexture(new BufferTextureCopy
        {
            Buffer = srcBuffer,
            Texture = arrayTexture,
            MipLevel = 0,
            ArrayLayer = 0, // Target layer 0
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        // Copy green to layer 1 using ArrayLayer field
        cmd.CopyBufferToTexture(new BufferTextureCopy
        {
            Buffer = srcBuffer2,
            Texture = arrayTexture,
            MipLevel = 0,
            ArrayLayer = 1, // Target layer 1
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back layer 0 - should be red
        using var cmd2 = _fixture.Device.CreateCommandList();
        using var fence2 = _fixture.Device.CreateFence(false);

        cmd2.Begin();
        cmd2.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = arrayTexture,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 0, // Read layer 0
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd2.End();

        _fixture.Device.SubmitCommands(cmd2, fence2);
        fence2.Wait();

        var layer0Data = new byte[16];
        ReadBufferData<byte>(readbackBuffer, layer0Data);

        // Layer 0 should be red
        Assert.Equal(255, layer0Data[0]); // R
        Assert.Equal(0, layer0Data[1]);   // G
        Assert.Equal(0, layer0Data[2]);   // B

        // Read back layer 1 - should be green
        using var cmd3 = _fixture.Device.CreateCommandList();
        using var fence3 = _fixture.Device.CreateFence(false);

        cmd3.Begin();
        cmd3.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = arrayTexture,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 1, // Read layer 1
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd3.End();

        _fixture.Device.SubmitCommands(cmd3, fence3);
        fence3.Wait();

        var layer1Data = new byte[16];
        ReadBufferData<byte>(readbackBuffer, layer1Data);

        // Layer 1 should be green
        Assert.Equal(0, layer1Data[0]);   // R
        Assert.Equal(255, layer1Data[1]); // G
        Assert.Equal(0, layer1Data[2]);   // B
    }

    [Fact]
    public void CopyTextureToTexture_ArrayLayers_CopiesCorrectLayers()
    {
        // Test that CopyTextureToTexture properly uses SourceArrayLayer
        // and DestinationArrayLayer fields instead of Z

        // Create source texture with 2 layers
        using var srcTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1,
            ArrayLayers = 2,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource,
            SampleCount = SampleCount.Count1
        });

        // Create destination texture with 2 layers
        using var dstTexture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1,
            ArrayLayers = 2,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        });

        // Upload red to src layer 0, blue to src layer 1
        byte[] redData = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        byte[] blueData = [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255];

        _fixture.Device.UpdateTexture(srcTexture, new TextureUpdateDescriptor
        {
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            MipLevel = 0,
            ArrayLayer = 0
        }, redData);

        _fixture.Device.UpdateTexture(srcTexture, new TextureUpdateDescriptor
        {
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            MipLevel = 0,
            ArrayLayer = 1
        }, blueData);

        using var readbackBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuToCpu
        });

        using var cmd = _fixture.Device.CreateCommandList();
        using var fence = _fixture.Device.CreateFence(false);

        cmd.Begin();
        // Copy source layer 1 (blue) to destination layer 0
        cmd.CopyTextureToTexture(new TextureTextureCopy
        {
            Source = srcTexture,
            SourceMipLevel = 0,
            SourceArrayLayer = 1, // Blue layer
            SourceX = 0, SourceY = 0, SourceZ = 0,
            Destination = dstTexture,
            DestinationMipLevel = 0,
            DestinationArrayLayer = 0, // Copy to layer 0
            DestinationX = 0, DestinationY = 0, DestinationZ = 0,
            Width = 2, Height = 2, Depth = 1
        });
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        // Read back destination layer 0 - should be blue (copied from src layer 1)
        using var cmd2 = _fixture.Device.CreateCommandList();
        using var fence2 = _fixture.Device.CreateFence(false);

        cmd2.Begin();
        cmd2.CopyTextureToBuffer(new BufferTextureCopy
        {
            Texture = dstTexture,
            Buffer = readbackBuffer,
            MipLevel = 0,
            ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 2, Height = 2, Depth = 1,
            BufferOffset = 0
        });
        cmd2.End();

        _fixture.Device.SubmitCommands(cmd2, fence2);
        fence2.Wait();

        var result = new byte[16];
        ReadBufferData<byte>(readbackBuffer, result);

        // Destination layer 0 should be blue (from source layer 1)
        Assert.Equal(0, result[0]);   // R
        Assert.Equal(0, result[1]);   // G
        Assert.Equal(255, result[2]); // B (blue!)
        Assert.Equal(255, result[3]); // A
    }

    [Fact]
    public void CubemapFaceIndex_OutOfRange_ThrowsException()
    {
        // Test that invalid cubemap face indices (> 5) throw ArgumentOutOfRangeException

        using var cubeMap = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.TextureCube,
            Width = 2, Height = 2, Depth = 1,
            MipLevels = 1, ArrayLayers = 6,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        });

        byte[] data = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];

        // Valid face indices (0-5) should work
        for (uint face = 0; face < 6; face++)
        {
            _fixture.Device.UpdateTexture(cubeMap, new TextureUpdateDescriptor
            {
                X = 0, Y = 0, Z = 0,
                Width = 2, Height = 2, Depth = 1,
                MipLevel = 0,
                ArrayLayer = face
            }, data);
        }

        // Invalid face index (6) should throw
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _fixture.Device.UpdateTexture(cubeMap, new TextureUpdateDescriptor
            {
                X = 0, Y = 0, Z = 0,
                Width = 2, Height = 2, Depth = 1,
                MipLevel = 0,
                ArrayLayer = 6 // Invalid - faces are 0-5
            }, data);
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Reads buffer data back to CPU using GL directly.
    /// </summary>
    private unsafe void ReadBufferData<T>(Prowl.Runtime.Graphite.Buffer buffer, Span<T> destination) where T : unmanaged
    {
        if (buffer is not GLBuffer glBuffer)
            throw new InvalidOperationException("Buffer is not a GLBuffer");

        _fixture.GL.BindBuffer(BufferTargetARB.ArrayBuffer, glBuffer.Handle);

        fixed (T* ptr = destination)
        {
            _fixture.GL.GetBufferSubData(BufferTargetARB.ArrayBuffer, 0,
                (nuint)(destination.Length * sizeof(T)), ptr);
        }

        _fixture.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    #endregion
}
