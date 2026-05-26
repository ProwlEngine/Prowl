using Silk.NET.OpenGL;

using System;

namespace Prowl.Veldrid.OpenGL;

internal class OpenGLResourceFactory : ResourceFactory
{
    private readonly OpenGLGraphicsDevice _gd;
    private readonly StagingMemoryPool _pool;

    public override GraphicsBackend BackendType => _gd.BackendType;

    public unsafe OpenGLResourceFactory(OpenGLGraphicsDevice gd)
        : base(gd.Features)
    {
        _gd = gd;
        _pool = gd.StagingMemoryPool;
    }

    public override CommandBuffer CreateCommandBuffer(ref CommandBufferDescription description)
    {
        return new OpenGLCommandBuffer(_gd, ref description);
    }

    public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
    {
        return new OpenGLFramebuffer(_gd, ref description);
    }

    protected override Sampler CreateSamplerCore(ref SamplerDescription description)
    {
        return new OpenGLSampler(_gd, ref description);
    }

    protected override ShaderProgram CreateShaderProgramCore(ref ShaderDescription description)
    {
        OpenGLShaderProgram program = new OpenGLShaderProgram(_gd, ref description);
        _gd.EnsureResourceInitialized(program);
        return program;
    }

    protected override ComputeProgram CreateComputeProgramCore(ref ComputeDescription description)
    {
        OpenGLComputeProgram program = new OpenGLComputeProgram(_gd, ref description);
        _gd.EnsureResourceInitialized(program);
        return program;
    }

    protected override Texture CreateTextureCore(ref TextureDescription description)
    {
        return new OpenGLTexture(_gd, ref description);
    }

    protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
    {
        return new OpenGLTexture(_gd, (uint)nativeTexture, ref description);
    }

    protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
    {
        return new OpenGLTextureView(_gd, ref description);
    }

    protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
    {
        return new OpenGLBuffer(
            _gd,
            description.SizeInBytes,
            description.Usage);
    }

    public override Fence CreateFence(bool signaled)
    {
        return new OpenGLFence(signaled);
    }

    public override Swapchain CreateSwapchain(ref SwapchainDescription description)
    {
        throw new NotSupportedException("OpenGL does not support creating Swapchain objects.");
    }
}
