using Prowl.Vector;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Xunit;

namespace Prowl.Veldrid.Tests;

[StructLayout(LayoutKind.Sequential)]
struct FillValueStruct
{
    /// <summary>
    /// The value we fill the 3d texture with.
    /// </summary>
    public float FillValue;
    public float pad1, pad2, pad3;

    public FillValueStruct(float fillValue)
    {
        FillValue = fillValue;
        pad1 = pad2 = pad3 = 0;
    }
}


public abstract class ComputeTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void ComputeShader3dTexture()
    {
        // Just a dumb compute shader that fills a 3D texture with the same value from a uniform multiplied by the depth.
        string shaderText = @"
#version 450
layout(set = 0, binding = 0, rgba32f) uniform image3D TextureToFill;
layout(set = 0, binding = 1) uniform FillValueBuffer
{
    float FillValue;
    float Padding1;
    float Padding2;
    float Padding3;
};
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;
void main()
{
    ivec3 textureCoordinate = ivec3(gl_GlobalInvocationID.xyz);
    float dataToStore = FillValue * (textureCoordinate.z + 1);

    imageStore(TextureToFill, textureCoordinate, vec4(dataToStore));
}
";

        const float FillValue = 42.42f;
        const uint OutputTextureSize = 32;

        ShaderProgram computeShader = null; // SPIRV cross-compilation handled separately; see TestShaders.cs.
        // using Shader computeShader = RF.CreateFromSpirv(new ShaderDescription(
        //     ShaderStages.Compute,
        //     Encoding.ASCII.GetBytes(shaderText),
        //     "main"));

        using ResourceLayout computeLayout = RF.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("TextureToFill", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0),
            new ResourceLayoutElementDescription("FillValueBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute, 1)));

        using Pipeline computePipeline = RF.CreateComputePipeline(new ComputePipelineDescription(
            computeShader,
            computeLayout,
            16, 16, 1));

        using DeviceBuffer fillValueBuffer = RF.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FillValueStruct>(), BufferUsage.UniformBuffer));

        // Create our output texture.
        using Texture computeTargetTexture = RF.CreateTexture(TextureDescription.Texture3D(
            OutputTextureSize,
            OutputTextureSize,
            OutputTextureSize,
            1,
            PixelFormat.R32_G32_B32_A32_Float,
            TextureUsage.Sampled | TextureUsage.Storage));

        using TextureView computeTargetTextureView = RF.CreateTextureView(computeTargetTexture);

        using ResourceSet computeResourceSet = RF.CreateResourceSet(new ResourceSetDescription(
            computeLayout,
            computeTargetTextureView,
            fillValueBuffer));

        using CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();

        cl.UpdateBuffer(fillValueBuffer, 0, new FillValueStruct(FillValue));

        // Use the compute shader to fill the texture.
        cl.SetPipeline(computePipeline);
        cl.SetComputeResourceSet(0, computeResourceSet);
        const uint GroupDivisorXY = 16;
        cl.Dispatch(OutputTextureSize / GroupDivisorXY, OutputTextureSize / GroupDivisorXY, OutputTextureSize);

        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        // Read back from our texture and make sure it has been properly filled.
        for (uint depth = 0; depth < computeTargetTexture.Depth; depth++)
        {
            float v = FillValue * (depth + 1);
            Color expectedFillValue = new(v, v, v, v);
            int notFilledCount = CountTexelsNotFilledAtDepth(GD, computeTargetTexture, expectedFillValue, depth);

            Assert.Equal(0, notFilledCount);
        }
    }


    /// <summary>
    /// Returns the number of texels in the texture that DO NOT match the fill value.
    /// </summary>
    private static int CountTexelsNotFilledAtDepth<TexelType>(GraphicsDevice device, Texture texture, TexelType fillValue, uint depth)
        where TexelType : unmanaged
    {
        ResourceFactory factory = device.ResourceFactory;

        // We need to create a staging texture and copy into it.
        TextureDescription description = new(texture.Width, texture.Height, depth: 1,
            texture.MipLevels, texture.ArrayLayers,
            texture.Format, TextureUsage.Staging,
            texture.Type, texture.SampleCount);

        Texture staging = factory.CreateTexture(ref description);

        using CommandBuffer cl = factory.CreateCommandBuffer();
        cl.Begin();

        cl.CopyTexture(texture,
            srcX: 0, srcY: 0, srcZ: depth,
            srcMipLevel: 0, srcBaseArrayLayer: 0,
            staging,
            dstX: 0, dstY: 0, dstZ: 0,
            dstMipLevel: 0, dstBaseArrayLayer: 0,
            staging.Width, staging.Height,
            depth: 1, layerCount: 1);

        cl.End();
        { Frame _f = device.BeginFrame(); _f.SubmitCommands(cl); device.EndFrame(_f); }
        device.WaitForIdle();

        try
        {
            MappedResourceView<TexelType> mapped = device.Map<TexelType>(staging, MapMode.Read);

            int notFilledCount = 0;
            for (int y = 0; y < staging.Height; y++)
            {
                for (int x = 0; x < staging.Width; x++)
                {
                    TexelType actual = mapped[x, y];
                    if (!fillValue.Equals(actual))
                    {
                        notFilledCount++;
                    }
                }
            }

            return notFilledCount;
        }
        finally
        {
            device.Unmap(staging);
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct BasicComputeTestParams
    {
        public uint Width;
        public uint Height;
        private uint _padding1;
        private uint _padding2;
    }

    [SkippableFact]
    public void BasicCompute()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0),
            new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
            new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2)));

        uint width = 1024;
        uint height = 1024;
        DeviceBuffer paramsBuffer = RF.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<BasicComputeTestParams>(), BufferUsage.UniformBuffer));
        DeviceBuffer sourceBuffer = RF.CreateBuffer(new BufferDescription(width * height * 4, BufferUsage.StructuredBufferReadWrite, 4));
        DeviceBuffer destinationBuffer = RF.CreateBuffer(new BufferDescription(width * height * 4, BufferUsage.StructuredBufferReadWrite, 4));

        GD.UpdateBuffer(paramsBuffer, 0, new BasicComputeTestParams { Width = width, Height = height });

        float[] sourceData = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * (int)width + x;
                sourceData[index] = index;
            }
        GD.UpdateBuffer(sourceBuffer, 0, sourceData);

        ResourceSet rs = RF.CreateResourceSet(new ResourceSetDescription(layout, paramsBuffer, sourceBuffer, destinationBuffer));

        Pipeline pipeline = RF.CreateComputePipeline(new ComputePipelineDescription(
            TestShaders.LoadCompute(RF, "BasicComputeTest"),
            layout,
            16, 16, 1));

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetPipeline(pipeline);
        cl.SetComputeResourceSet(0, rs);
        cl.Dispatch(width / 16, width / 16, 1);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        DeviceBuffer sourceReadback = GetReadback(sourceBuffer);
        DeviceBuffer destinationReadback = GetReadback(destinationBuffer);

        MappedResourceView<float> sourceReadView = GD.Map<float>(sourceReadback, MapMode.Read);
        MappedResourceView<float> destinationReadView = GD.Map<float>(destinationReadback, MapMode.Read);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * (int)width + x;
                Assert.Equal(2 * sourceData[index], sourceReadView[index]);
                Assert.Equal(sourceData[index], destinationReadView[index]);
            }

        GD.Unmap(sourceReadback);
        GD.Unmap(destinationReadback);
    }

    [SkippableFact]
    public void ComputeCubemapGeneration()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.If(GD.GetD3D11Info(out _), "D3D11 doesn't support Storage Cubemaps");

        const int TexSize = 32;
        const uint MipLevels = 1;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize,
            MipLevels,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.Cubemap);
        Texture computeOutput = RF.CreateTexture(texDesc);

        Float4[] faceColors = [
            new Float4(0 * 42),
            new Float4(1 * 42),
            new Float4(2 * 42),
            new Float4(3 * 42),
            new Float4(4 * 42),
            new Float4(5 * 42)
        ];

        ResourceLayout computeLayout = RF.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)));
        ResourceSet computeSet = RF.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutput));

        Pipeline computePipeline = RF.CreateComputePipeline(new ComputePipelineDescription(
            TestShaders.LoadCompute(RF, "ComputeCubemapGenerator"),
            computeLayout,
            32, 32, 1));

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetPipeline(computePipeline);
        cl.SetComputeResourceSet(0, computeSet);
        cl.Dispatch(TexSize / 32, TexSize / 32, 6);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        using (Texture readback = GetReadback(computeOutput))
        {
            for (uint mip = 0; mip < MipLevels; mip++)
            {
                for (uint face = 0; face < 6; face++)
                {
                    var subresource = readback.CalculateSubresource(mip, face);
                    var mipSize = (TexSize >> (int)mip);
                    var expectedColor = new Color32((byte)faceColors[face].X, (byte)faceColors[face].Y, (byte)faceColors[face].Z, (byte)faceColors[face].Z);
                    MappedResourceView<Color32> readView = GD.Map<Color32>(readback, MapMode.Read, subresource);
                    for (int y = 0; y < mipSize; y++)
                        for (int x = 0; x < mipSize; x++)
                        {
                            Assert.Equal(expectedColor, readView[x, y]);
                        }
                    GD.Unmap(readback, subresource);
                }
            }
        }
    }

    [SkippableFact]
    public void ComputeCubemapBindSingleTextureMipLevelOutput()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.If(GD.GetD3D11Info(out _), "D3D11 doesn't support Storage Cubemaps");

        const int TexSize = 128;
        const uint MipLevels = 7;

        const uint BoundMipLevel = 2;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize,
            MipLevels,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.Cubemap);
        Texture computeOutput = RF.CreateTexture(texDesc);

        TextureView computeOutputMipLevel = RF.CreateTextureView(new TextureViewDescription(computeOutput, BoundMipLevel, 1, 0, 1));

        Float4[] faceColors = [
            new Float4(0 * 42),
            new Float4(1 * 42),
            new Float4(2 * 42),
            new Float4(3 * 42),
            new Float4(4 * 42),
            new Float4(5 * 42)
        ];

        ResourceLayout computeLayout = RF.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)));
        ResourceSet computeSet = RF.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutputMipLevel));

        Pipeline computePipeline = RF.CreateComputePipeline(new ComputePipelineDescription(
            TestShaders.LoadCompute(RF, "ComputeCubemapGenerator"),
            computeLayout,
            32, 32, 1));

        using (Texture readback = GetReadback(computeOutput))
        {
            for (uint mip = 0; mip < MipLevels; mip++)
            {
                for (uint face = 0; face < 6; face++)
                {
                    var subresource = readback.CalculateSubresource(mip, face);
                    var mipSize = (uint)(TexSize / (1 << (int)mip));
                    var expectedColor = new Color32(0, 0, 0, 0);
                    MappedResourceView<Color32> readView = GD.Map<Color32>(readback, MapMode.Read, subresource);
                    for (int y = 0; y < mipSize; y++)
                        for (int x = 0; x < mipSize; x++)
                        {
                            Assert.Equal(expectedColor, readView[x, y]);
                        }
                    GD.Unmap(readback, subresource);
                }
            }
        }

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetPipeline(computePipeline);
        cl.SetComputeResourceSet(0, computeSet);
        cl.Dispatch((TexSize >> (int)BoundMipLevel) / 32, (TexSize >> (int)BoundMipLevel) / 32, 6);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        using (Texture readback = GetReadback(computeOutput))
        {
            for (uint mip = 0; mip < MipLevels; mip++)
            {
                for (uint face = 0; face < 6; face++)
                {
                    var subresource = readback.CalculateSubresource(mip, face);
                    var mipSize = (uint)(TexSize / (1 << (int)mip));
                    Color32 expectedColor = mip == BoundMipLevel ? new Color32((byte)faceColors[face].X, (byte)faceColors[face].Y, (byte)faceColors[face].Z, (byte)faceColors[face].Z) : default(Color32);
                    MappedResourceView<Color32> readView = GD.Map<Color32>(readback, MapMode.Read, subresource);
                    for (int y = 0; y < mipSize; y++)
                        for (int x = 0; x < mipSize; x++)
                        {
                            Assert.Equal(expectedColor, readView[x, y]);
                        }
                    GD.Unmap(readback, subresource);
                }
            }
        }
    }

}

#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
public class OpenGLComputeTests : ComputeTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
public class OpenGLESComputeTests : ComputeTests<OpenGLESDeviceCreator> { }
#endif
#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
public class VulkanComputeTests : ComputeTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
public class D3D11ComputeTests : ComputeTests<D3D11DeviceCreatorWithMainSwapchain> { }
#endif
