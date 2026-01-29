// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a command list.
/// Since OpenGL is an immediate-mode API, commands are recorded and executed later.
/// </summary>
public class GLCommandList : CommandList
{
    private readonly GLGraphiteDevice _device;
    private readonly List<Action> _commands = new();

    // Current state tracking
    private GLPipelineState? _currentPipeline;
    private GLComputePipelineState? _currentComputePipeline;
    private uint _currentFramebuffer;
    private readonly GLBindGroup?[] _boundGroups = new GLBindGroup?[8];

    // Index buffer state (set at execution time)
    private IndexFormat _currentIndexFormat;
    private uint _currentIndexBufferOffset;

    // Stencil state (tracked from pipeline for SetStencilReference)
    private StencilFunction _stencilFrontFunc = StencilFunction.Always;
    private StencilFunction _stencilBackFunc = StencilFunction.Always;
    private uint _stencilReadMask = 0xFF;

    // Vertex buffer tracking for re-application on pipeline (VAO) switch
    private readonly struct VertexBufferBinding
    {
        public readonly uint Handle;
        public readonly nint Offset;
        public readonly uint Stride;
        public VertexBufferBinding(uint handle, nint offset, uint stride)
        {
            Handle = handle;
            Offset = offset;
            Stride = stride;
        }
    }
    private readonly VertexBufferBinding?[] _boundVertexBuffers = new VertexBufferBinding?[16];
    private GLBuffer? _boundIndexBuffer;

    internal GLCommandList(GLGraphiteDevice device)
    {
        _device = device;
    }

    protected override void BeginRecording()
    {
        _commands.Clear();
        _currentPipeline = null;
        _currentComputePipeline = null;
        _currentFramebuffer = 0;
        Array.Clear(_boundGroups);
        Array.Clear(_boundVertexBuffers);
        _boundIndexBuffer = null;
    }

    protected override void EndRecording()
    {
        // Commands are ready to execute
    }

    /// <summary>
    /// Executes all recorded commands.
    /// </summary>
    internal void Execute()
    {
        foreach (var command in _commands)
        {
            command();
        }
    }

    #region Render Pass

    // Shared state between BeginRenderPass and EndRenderPass for the current pass
    private uint _pendingFramebufferToDelete;

    // MSAA resolve info stored during BeginRenderPass, executed in EndRenderPass
    private readonly struct MsaaResolveInfo
    {
        public readonly GLTexture SourceTexture;
        public readonly GLTexture DestTexture;
        public readonly uint SourceMipLevel;
        public readonly uint SourceArrayLayer;
        public readonly uint DestMipLevel;
        public readonly uint DestArrayLayer;
        public readonly uint Width;
        public readonly uint Height;

        public MsaaResolveInfo(GLTexture source, GLTexture dest, uint srcMip, uint srcLayer,
            uint dstMip, uint dstLayer, uint width, uint height)
        {
            SourceTexture = source;
            DestTexture = dest;
            SourceMipLevel = srcMip;
            SourceArrayLayer = srcLayer;
            DestMipLevel = dstMip;
            DestArrayLayer = dstLayer;
            Width = width;
            Height = height;
        }
    }
    private readonly List<MsaaResolveInfo> _pendingResolves = new();

    protected override void BeginRenderPassCore(in RenderPassDescriptor descriptor)
    {
        var desc = descriptor; // Capture for lambda

        // Collect MSAA resolve targets before adding to command list
        // This allows EndRenderPass to know what resolves are pending
        _pendingResolves.Clear();
        if (desc.ColorAttachments != null)
        {
            foreach (var attachment in desc.ColorAttachments)
            {
                if (attachment.ResolveTarget != null &&
                    attachment.Texture is GLTexture srcTex &&
                    attachment.ResolveTarget is GLTexture dstTex)
                {
                    // Calculate mip dimensions
                    uint width = Math.Max(1, srcTex.Width >> (int)attachment.MipLevel);
                    uint height = Math.Max(1, srcTex.Height >> (int)attachment.MipLevel);

                    _pendingResolves.Add(new MsaaResolveInfo(
                        srcTex, dstTex,
                        attachment.MipLevel, attachment.ArrayLayer,
                        0, 0, // Resolve target uses mip 0, layer 0 by default
                        width, height));
                }
            }
        }

        // Copy resolve list for the lambda (list may be modified by next render pass)
        var resolves = _pendingResolves.Count > 0 ? _pendingResolves.ToArray() : null;

        _commands.Add(() =>
        {
            // Create or get framebuffer
            uint fbo = CreateFramebuffer(desc);
            _device.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

            // Store for EndRenderPass to clean up (this runs at execution time)
            _pendingFramebufferToDelete = fbo;

            // Set viewport based on first attachment
            uint width = 0, height = 0;
            if (desc.ColorAttachments != null && desc.ColorAttachments.Length > 0)
            {
                var tex = desc.ColorAttachments[0].Texture;
                width = tex.Width;
                height = tex.Height;

                // Handle swapchain - get dimensions from device
                if (tex is GLSwapchainTexture)
                {
                    width = _device.SwapchainWidth;
                    height = _device.SwapchainHeight;
                }
            }
            else if (desc.DepthStencilAttachment?.Texture != null)
            {
                width = desc.DepthStencilAttachment.Value.Texture.Width;
                height = desc.DepthStencilAttachment.Value.Texture.Height;
            }

            _device.GL.Viewport(0, 0, width, height);

            // Reset scissor test (it may have been enabled by a previous pass)
            _device.GL.Disable(EnableCap.ScissorTest);

            // Enable/disable sRGB framebuffer based on render target format
            // This enables automatic linear-to-sRGB conversion on write
            bool hasSrgbTarget = false;
            if (desc.ColorAttachments != null)
            {
                foreach (var att in desc.ColorAttachments)
                {
                    if (IsSrgbFormat(att.Texture.Format))
                    {
                        hasSrgbTarget = true;
                        break;
                    }
                }
            }

            if (hasSrgbTarget)
                _device.GL.Enable(EnableCap.FramebufferSrgb);
            else
                _device.GL.Disable(EnableCap.FramebufferSrgb);

            // Handle clears
            ClearAttachments(desc);

            // Store resolves for EndRenderPass (captured in closure)
            _pendingResolvesForExecution = resolves;
        });
    }

    // Resolves captured during BeginRenderPass, used during EndRenderPass execution
    private MsaaResolveInfo[]? _pendingResolvesForExecution;

    private uint CreateFramebuffer(in RenderPassDescriptor descriptor)
    {
        // Check for default framebuffer (swapchain)
        bool isSwapchain = false;
        if (descriptor.ColorAttachments != null && descriptor.ColorAttachments.Length > 0)
        {
            if (descriptor.ColorAttachments[0].Texture is GLSwapchainTexture)
                isSwapchain = true;
        }

        if (isSwapchain)
            return 0; // Default framebuffer

        uint fbo = _device.GL.GenFramebuffer();
        _device.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        // Attach color targets
        if (descriptor.ColorAttachments != null && descriptor.ColorAttachments.Length > 0)
        {
            var drawBuffers = new DrawBufferMode[descriptor.ColorAttachments.Length];

            for (int i = 0; i < descriptor.ColorAttachments.Length; i++)
            {
                var attachment = descriptor.ColorAttachments[i];
                if (attachment.Texture is GLTexture glTex)
                {
                    var attachmentPoint = FramebufferAttachment.ColorAttachment0 + i;
                    AttachTextureToFramebuffer(glTex, attachmentPoint, attachment.MipLevel, attachment.ArrayLayer);
                }
                drawBuffers[i] = DrawBufferMode.ColorAttachment0 + i;
            }

            _device.GL.DrawBuffers((uint)drawBuffers.Length, drawBuffers);
        }
        else
        {
            // For depth-only passes, explicitly specify no color output
            _device.GL.DrawBuffer(DrawBufferMode.None);
        }

        // Attach depth/stencil
        if (descriptor.DepthStencilAttachment.HasValue)
        {
            var attachment = descriptor.DepthStencilAttachment.Value;
            if (attachment.Texture is GLTexture glTex)
            {
                var attachmentPoint = glTex.HasStencil
                    ? FramebufferAttachment.DepthStencilAttachment
                    : FramebufferAttachment.DepthAttachment;

                AttachTextureToFramebuffer(glTex, attachmentPoint, attachment.MipLevel, attachment.ArrayLayer);
            }
        }

        // Verify framebuffer completeness
        var status = _device.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            _device.GL.DeleteFramebuffer(fbo);
            throw new InvalidOperationException($"Framebuffer incomplete: {status}. Check that all attachments have compatible formats and dimensions.");
        }

        return fbo;
    }

    /// <summary>
    /// Attaches a texture to a framebuffer, handling array textures, cubemaps, and 3D textures properly.
    /// </summary>
    private void AttachTextureToFramebuffer(GLTexture texture, FramebufferAttachment attachment, uint mipLevel, uint arrayLayer)
    {
        switch (texture.Target)
        {
            case TextureTarget.Texture1D:
                _device.GL.FramebufferTexture1D(FramebufferTarget.Framebuffer, attachment,
                    texture.Target, texture.Handle, (int)mipLevel);
                break;

            case TextureTarget.Texture1DArray:
            case TextureTarget.Texture2DArray:
            case TextureTarget.Texture3D:
                // Use FramebufferTextureLayer for layered textures
                _device.GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, attachment,
                    texture.Handle, (int)mipLevel, (int)arrayLayer);
                break;

            case TextureTarget.TextureCubeMap:
                // For cubemaps, arrayLayer specifies the face (0-5: +X, -X, +Y, -Y, +Z, -Z)
                var faceTarget = GetCubemapFaceTarget(arrayLayer);
                _device.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment,
                    faceTarget, texture.Handle, (int)mipLevel);
                break;

            case TextureTarget.TextureCubeMapArray:
                // For cubemap arrays, layer = array_index * 6 + face
                _device.GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, attachment,
                    texture.Handle, (int)mipLevel, (int)arrayLayer);
                break;

            case TextureTarget.Texture2DMultisample:
                // Multisample textures always use mip level 0
                _device.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment,
                    texture.Target, texture.Handle, 0);
                break;

            case TextureTarget.Texture2DMultisampleArray:
                // Multisample array textures use layer, but always mip level 0
                _device.GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, attachment,
                    texture.Handle, 0, (int)arrayLayer);
                break;

            default:
                // Texture2D and other simple targets
                _device.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment,
                    texture.Target, texture.Handle, (int)mipLevel);
                break;
        }
    }

    private void ClearAttachments(in RenderPassDescriptor descriptor)
    {
        // Color clears
        if (descriptor.ColorAttachments != null)
        {
            for (int i = 0; i < descriptor.ColorAttachments.Length; i++)
            {
                var attachment = descriptor.ColorAttachments[i];
                if (attachment.LoadOp == LoadOp.Clear)
                {
                    var c = attachment.ClearColor;
                    _device.GL.ClearBuffer(GLEnum.Color, i, [c.X, c.Y, c.Z, c.W]);
                }
            }
        }

        // Depth/stencil clear
        if (descriptor.DepthStencilAttachment.HasValue)
        {
            var attachment = descriptor.DepthStencilAttachment.Value;

            if (attachment.DepthLoadOp == LoadOp.Clear && attachment.StencilLoadOp == LoadOp.Clear)
            {
                _device.GL.ClearBuffer(GLEnum.DepthStencil, 0, attachment.DepthClearValue, attachment.StencilClearValue);
            }
            else if (attachment.DepthLoadOp == LoadOp.Clear)
            {
                _device.GL.ClearBuffer(GLEnum.Depth, 0, [attachment.DepthClearValue]);
            }
            else if (attachment.StencilLoadOp == LoadOp.Clear)
            {
                Span<int> stencilValue = [attachment.StencilClearValue];
                _device.GL.ClearBuffer(GLEnum.Stencil, 0, stencilValue);
            }
        }
    }

    protected override void EndRenderPassCore()
    {
        _commands.Add(() =>
        {
            // Perform MSAA resolve if any attachments had ResolveTarget
            if (_pendingResolvesForExecution != null && _pendingResolvesForExecution.Length > 0)
            {
                PerformMsaaResolves(_pendingResolvesForExecution);
                _pendingResolvesForExecution = null;
            }

            // Delete framebuffer if it was created (not default)
            if (_pendingFramebufferToDelete != 0)
            {
                _device.GL.DeleteFramebuffer(_pendingFramebufferToDelete);
                _pendingFramebufferToDelete = 0;
            }
            _device.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        });
    }

    /// <summary>
    /// Performs MSAA resolve operations using glBlitFramebuffer.
    /// </summary>
    private void PerformMsaaResolves(MsaaResolveInfo[] resolves)
    {
        // Create temporary framebuffers for the blit operation
        uint readFbo = _device.GL.GenFramebuffer();
        uint drawFbo = _device.GL.GenFramebuffer();

        foreach (var resolve in resolves)
        {
            // Attach source (MSAA) texture to read framebuffer
            _device.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFbo);
            AttachTextureToFramebufferForResolve(resolve.SourceTexture, FramebufferAttachment.ColorAttachment0,
                resolve.SourceMipLevel, resolve.SourceArrayLayer, FramebufferTarget.ReadFramebuffer);
            _device.GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

            // Attach destination (non-MSAA) texture to draw framebuffer
            _device.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFbo);
            AttachTextureToFramebufferForResolve(resolve.DestTexture, FramebufferAttachment.ColorAttachment0,
                resolve.DestMipLevel, resolve.DestArrayLayer, FramebufferTarget.DrawFramebuffer);
            _device.GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            // Perform the blit (resolve)
            _device.GL.BlitFramebuffer(
                0, 0, (int)resolve.Width, (int)resolve.Height,  // Source rect
                0, 0, (int)resolve.Width, (int)resolve.Height,  // Dest rect (same size)
                ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Nearest); // Use Nearest for MSAA resolve
        }

        // Clean up temporary framebuffers
        _device.GL.DeleteFramebuffer(readFbo);
        _device.GL.DeleteFramebuffer(drawFbo);

        // Rebind the render pass framebuffer
        _device.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pendingFramebufferToDelete);
    }

    /// <summary>
    /// Attaches a texture to a framebuffer for MSAA resolve (similar to AttachTextureToFramebuffer but takes target).
    /// </summary>
    private void AttachTextureToFramebufferForResolve(GLTexture texture, FramebufferAttachment attachment,
        uint mipLevel, uint arrayLayer, FramebufferTarget target)
    {
        switch (texture.Target)
        {
            case TextureTarget.Texture2DMultisample:
                // MSAA textures always use mip level 0
                _device.GL.FramebufferTexture2D(target, attachment,
                    texture.Target, texture.Handle, 0);
                break;

            case TextureTarget.Texture2DMultisampleArray:
                _device.GL.FramebufferTextureLayer(target, attachment,
                    texture.Handle, 0, (int)arrayLayer);
                break;

            case TextureTarget.Texture2DArray:
            case TextureTarget.Texture3D:
                _device.GL.FramebufferTextureLayer(target, attachment,
                    texture.Handle, (int)mipLevel, (int)arrayLayer);
                break;

            case TextureTarget.TextureCubeMap:
                _device.GL.FramebufferTexture2D(target, attachment,
                    GetCubemapFaceTarget(arrayLayer), texture.Handle, (int)mipLevel);
                break;

            default:
                _device.GL.FramebufferTexture2D(target, attachment,
                    texture.Target, texture.Handle, (int)mipLevel);
                break;
        }
    }

    #endregion

    #region Pipeline & Binding

    protected override void SetPipelineCore(PipelineState pipeline)
    {
        var glPipeline = pipeline as GLPipelineState;
        _currentPipeline = glPipeline;
        _commands.Add(() =>
        {
            glPipeline?.Apply();

            // Track stencil compare functions for SetStencilReference
            if (glPipeline != null)
            {
                var stencilState = glPipeline.Descriptor.DepthStencilState;
                _stencilFrontFunc = GetStencilFunc(stencilState.StencilFront.Compare);
                _stencilBackFunc = GetStencilFunc(stencilState.StencilBack.Compare);
                _stencilReadMask = stencilState.StencilReadMask;
            }

            // Re-apply vertex buffer bindings since each pipeline has its own VAO in OpenGL.
            // Without this, switching pipelines would lose vertex buffer bindings.
            // Use the NEW pipeline's stride so the binding matches the new VAO's vertex layout.
            for (int i = 0; i < _boundVertexBuffers.Length; i++)
            {
                if (_boundVertexBuffers[i] is VertexBufferBinding binding)
                {
                    uint stride = binding.Stride;
                    if (glPipeline?.Descriptor.VertexLayout.Buffers != null &&
                        i < glPipeline.Descriptor.VertexLayout.Buffers.Length)
                    {
                        stride = glPipeline.Descriptor.VertexLayout.Buffers[i].Stride;
                    }
                    _device.GL.BindVertexBuffer((uint)i, binding.Handle, binding.Offset, stride);
                }
            }

            // Re-apply index buffer binding (also tied to VAO)
            if (_boundIndexBuffer != null)
            {
                _device.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, _boundIndexBuffer.Handle);
            }
        });
    }

    private static StencilFunction GetStencilFunc(CompareFunction func) => func switch
    {
        Graphite.CompareFunction.Never => StencilFunction.Never,
        Graphite.CompareFunction.Less => StencilFunction.Less,
        Graphite.CompareFunction.Equal => StencilFunction.Equal,
        Graphite.CompareFunction.LessEqual => StencilFunction.Lequal,
        Graphite.CompareFunction.Greater => StencilFunction.Greater,
        Graphite.CompareFunction.NotEqual => StencilFunction.Notequal,
        Graphite.CompareFunction.GreaterEqual => StencilFunction.Gequal,
        Graphite.CompareFunction.Always => StencilFunction.Always,
        _ => StencilFunction.Always,
    };

    protected override void SetBindGroupCore(uint index, BindGroup bindGroup, ReadOnlySpan<uint> dynamicOffsets)
    {
        var glBindGroup = bindGroup as GLBindGroup;
        _boundGroups[index] = glBindGroup;
        var idx = index;
        // Copy dynamic offsets since ReadOnlySpan can't be captured in lambda
        var offsets = dynamicOffsets.Length > 0 ? dynamicOffsets.ToArray() : null;
        _commands.Add(() => glBindGroup?.Apply(idx, offsets));
    }

    protected override void SetVertexBufferCore(uint slot, Buffer buffer, uint offset)
    {
        // Validate offset at record time
        if (offset > buffer.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"Vertex buffer offset exceeds buffer size. Offset: {offset}, Buffer size: {buffer.SizeInBytes}");
        }

        var glBuffer = buffer as GLBuffer;
        var pipeline = _currentPipeline;
        var s = slot;
        var off = offset;
        _commands.Add(() =>
        {
            if (glBuffer == null || pipeline == null) return;

            // Get stride from pipeline's vertex layout
            uint stride = 0;
            if (pipeline.Descriptor.VertexLayout.Buffers != null &&
                s < pipeline.Descriptor.VertexLayout.Buffers.Length)
            {
                stride = pipeline.Descriptor.VertexLayout.Buffers[s].Stride;
            }

            _device.GL.BindVertexBuffer(s, glBuffer.Handle, (nint)off, stride);

            // Track binding so it can be re-applied on pipeline (VAO) switch
            if (s < _boundVertexBuffers.Length)
            {
                _boundVertexBuffers[s] = new VertexBufferBinding(glBuffer.Handle, (nint)off, stride);
            }
        });
    }

    protected override void SetIndexBufferCore(Buffer buffer, IndexFormat format, uint offset)
    {
        // Validate offset at record time
        if (offset > buffer.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"Index buffer offset exceeds buffer size. Offset: {offset}, Buffer size: {buffer.SizeInBytes}");
        }

        var glBuffer = buffer as GLBuffer;
        var fmt = format;
        var off = offset;
        _commands.Add(() =>
        {
            if (glBuffer == null) return;
            _device.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, glBuffer.Handle);
            _currentIndexFormat = fmt;
            _currentIndexBufferOffset = off;
            _boundIndexBuffer = glBuffer;
        });
    }

    #endregion

    #region Dynamic State

    protected override void SetViewportCore(float x, float y, float width, float height, float minDepth, float maxDepth)
    {
        _commands.Add(() =>
        {
            _device.GL.Viewport((int)x, (int)y, (uint)width, (uint)height);
            _device.GL.DepthRange(minDepth, maxDepth);
        });
    }

    protected override void SetScissorCore(int x, int y, uint width, uint height)
    {
        _commands.Add(() =>
        {
            _device.GL.Enable(EnableCap.ScissorTest);
            _device.GL.Scissor(x, y, width, height);
        });
    }

    protected override void SetBlendConstantsCore(Float4 color)
    {
        _commands.Add(() => _device.GL.BlendColor(color.X, color.Y, color.Z, color.W));
    }

    protected override void SetStencilReferenceCore(uint reference)
    {
        var r = (int)reference;
        _commands.Add(() =>
        {
            // Use the compare functions from the current pipeline, not Always
            _device.GL.StencilFuncSeparate(TriangleFace.Front, _stencilFrontFunc, r, _stencilReadMask);
            _device.GL.StencilFuncSeparate(TriangleFace.Back, _stencilBackFunc, r, _stencilReadMask);
        });
    }

    #endregion

    #region Draw Commands

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        var pipeline = _currentPipeline;
        _commands.Add(() =>
        {
            if (pipeline == null) return;
            var primitiveType = GLPipelineState.GetPrimitiveType(pipeline.Topology);

            if (instanceCount == 1 && firstInstance == 0)
            {
                _device.GL.DrawArrays(primitiveType, (int)firstVertex, vertexCount);
            }
            else if (firstInstance == 0)
            {
                _device.GL.DrawArraysInstanced(primitiveType, (int)firstVertex, vertexCount, instanceCount);
            }
            else
            {
                _device.GL.DrawArraysInstancedBaseInstance(primitiveType, (int)firstVertex, vertexCount, instanceCount, firstInstance);
            }
        });
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        var pipeline = _currentPipeline;
        _commands.Add(() =>
        {
            if (pipeline == null) return;
            var primitiveType = GLPipelineState.GetPrimitiveType(pipeline.Topology);

            // Use the format set by SetIndexBuffer
            var indexType = _currentIndexFormat == IndexFormat.Uint16
                ? DrawElementsType.UnsignedShort
                : DrawElementsType.UnsignedInt;
            var indexSize = _currentIndexFormat == IndexFormat.Uint16 ? 2u : 4u;

            unsafe
            {
                // Calculate offset: buffer offset + (firstIndex * indexSize)
                void* offset = (void*)(_currentIndexBufferOffset + firstIndex * indexSize);

                if (instanceCount == 1 && firstInstance == 0 && vertexOffset == 0)
                {
                    _device.GL.DrawElements(primitiveType, indexCount, indexType, offset);
                }
                else if (firstInstance == 0)
                {
                    _device.GL.DrawElementsInstancedBaseVertex(primitiveType, indexCount, indexType, offset, instanceCount, vertexOffset);
                }
                else
                {
                    _device.GL.DrawElementsInstancedBaseVertexBaseInstance(primitiveType, indexCount, indexType, offset, instanceCount, vertexOffset, firstInstance);
                }
            }
        });
    }

    protected override void DrawIndirectCore(Buffer indirectBuffer, uint offset)
    {
        var glBuffer = indirectBuffer as GLBuffer;
        var pipeline = _currentPipeline;
        _commands.Add(() =>
        {
            if (glBuffer == null || pipeline == null) return;
            _device.GL.BindBuffer(BufferTargetARB.DrawIndirectBuffer, glBuffer.Handle);
            var primitiveType = GLPipelineState.GetPrimitiveType(pipeline.Topology);
            unsafe
            {
                _device.GL.DrawArraysIndirect(primitiveType, (void*)offset);
            }
        });
    }

    protected override void DrawIndexedIndirectCore(Buffer indirectBuffer, uint offset)
    {
        var glBuffer = indirectBuffer as GLBuffer;
        var pipeline = _currentPipeline;
        _commands.Add(() =>
        {
            if (glBuffer == null || pipeline == null) return;
            _device.GL.BindBuffer(BufferTargetARB.DrawIndirectBuffer, glBuffer.Handle);
            var primitiveType = GLPipelineState.GetPrimitiveType(pipeline.Topology);
            var indexType = _currentIndexFormat == IndexFormat.Uint16
                ? DrawElementsType.UnsignedShort
                : DrawElementsType.UnsignedInt;
            unsafe
            {
                _device.GL.DrawElementsIndirect(primitiveType, indexType, (void*)offset);
            }
        });
    }

    #endregion

    #region Compute Commands

    protected override void SetComputePipelineCore(ComputePipelineState pipeline)
    {
        var glPipeline = pipeline as GLComputePipelineState;
        _currentComputePipeline = glPipeline;
        _commands.Add(() => glPipeline?.Apply());
    }

    protected override void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        _commands.Add(() => _device.GL.DispatchCompute(groupCountX, groupCountY, groupCountZ));
    }

    protected override void DispatchIndirectCore(Buffer indirectBuffer, uint offset)
    {
        var glBuffer = indirectBuffer as GLBuffer;
        _commands.Add(() =>
        {
            if (glBuffer == null) return;
            _device.GL.BindBuffer(BufferTargetARB.DispatchIndirectBuffer, glBuffer.Handle);
            unsafe
            {
                _device.GL.DispatchComputeIndirect((nint)offset);
            }
        });
    }

    #endregion

    #region Copy Commands

    protected override void CopyBufferToBufferCore(Buffer source, uint sourceOffset, Buffer destination, uint destinationOffset, uint size)
    {
        // Validate bounds at record time for immediate error detection
        if (sourceOffset + size > source.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceOffset),
                $"Copy source would exceed buffer bounds. Offset: {sourceOffset}, Size: {size}, Buffer size: {source.SizeInBytes}");
        }
        if (destinationOffset + size > destination.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset),
                $"Copy destination would exceed buffer bounds. Offset: {destinationOffset}, Size: {size}, Buffer size: {destination.SizeInBytes}");
        }

        var srcBuffer = source as GLBuffer;
        var dstBuffer = destination as GLBuffer;
        _commands.Add(() =>
        {
            if (srcBuffer == null || dstBuffer == null) return;
            _device.GL.BindBuffer(BufferTargetARB.CopyReadBuffer, srcBuffer.Handle);
            _device.GL.BindBuffer(BufferTargetARB.CopyWriteBuffer, dstBuffer.Handle);
            _device.GL.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer,
                (nint)sourceOffset, (nint)destinationOffset, size);
        });
    }

    protected override void CopyBufferToTextureCore(in BufferTextureCopy copy)
    {
        // Validate at record time for immediate error detection
        if (copy.Texture is GLTexture tex)
        {
            if (tex.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
            {
                throw new NotSupportedException(
                    "Cannot copy buffer data to multisample textures. Multisample textures can only be rendered to.");
            }
        }

        var c = copy;
        _commands.Add(() =>
        {
            if (c.Buffer is not GLBuffer glBuffer || c.Texture is not GLTexture glTexture) return;

            _device.GL.BindBuffer(BufferTargetARB.PixelUnpackBuffer, glBuffer.Handle);
            _device.GL.BindTexture(glTexture.Target, glTexture.Handle);

            // Set pixel store parameters (not used for compressed textures, but doesn't hurt)
            _device.GL.PixelStore(PixelStoreParameter.UnpackRowLength, (int)c.BufferRowLength);
            _device.GL.PixelStore(PixelStoreParameter.UnpackImageHeight, (int)c.BufferImageHeight);

            unsafe
            {
                void* offset = (void*)c.BufferOffset;

                if (GLTexture.IsCompressedFormat(glTexture.Format))
                {
                    // Use compressed texture upload - calculate image size from buffer
                    var internalFormat = (InternalFormat)GLTexture.GetInternalFormatPublic(glTexture.Format);
                    uint imageSize = CalculateCompressedImageSize(glTexture.Format, c.Width, c.Height, c.Depth);

                    switch (glTexture.Target)
                    {
                        case TextureTarget.Texture2D:
                            _device.GL.CompressedTexSubImage2D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, c.Width, c.Height, internalFormat, imageSize, offset);
                            break;

                        case TextureTarget.Texture2DArray:
                            _device.GL.CompressedTexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.ArrayLayer, c.Width, c.Height, c.Depth, internalFormat, imageSize, offset);
                            break;

                        case TextureTarget.Texture3D:
                            _device.GL.CompressedTexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.Z, c.Width, c.Height, c.Depth, internalFormat, imageSize, offset);
                            break;

                        case TextureTarget.TextureCubeMap:
                            _device.GL.CompressedTexSubImage2D(GetCubemapFaceTarget(c.ArrayLayer), (int)c.MipLevel,
                                (int)c.X, (int)c.Y, c.Width, c.Height, internalFormat, imageSize, offset);
                            break;

                        case TextureTarget.TextureCubeMapArray:
                            _device.GL.CompressedTexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.ArrayLayer, c.Width, c.Height, 1, internalFormat, imageSize, offset);
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Compressed texture format not supported for target: {glTexture.Target}");
                    }
                }
                else
                {
                    // Use standard texture upload
                    var format = GetPixelFormat(glTexture.Format);
                    var type = GetPixelType(glTexture.Format);

                    switch (glTexture.Target)
                    {
                        case TextureTarget.Texture1D:
                            _device.GL.TexSubImage1D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, c.Width, format, type, offset);
                            break;

                        case TextureTarget.Texture1DArray:
                            _device.GL.TexSubImage2D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.ArrayLayer, c.Width, 1, format, type, offset);
                            break;

                        case TextureTarget.Texture2D:
                            _device.GL.TexSubImage2D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, c.Width, c.Height, format, type, offset);
                            break;

                        case TextureTarget.Texture3D:
                            _device.GL.TexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.Z, c.Width, c.Height, c.Depth, format, type, offset);
                            break;

                        case TextureTarget.Texture2DArray:
                            _device.GL.TexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.ArrayLayer, c.Width, c.Height, c.Depth, format, type, offset);
                            break;

                        case TextureTarget.TextureCubeMap:
                            _device.GL.TexSubImage2D(GetCubemapFaceTarget(c.ArrayLayer), (int)c.MipLevel,
                                (int)c.X, (int)c.Y, c.Width, c.Height, format, type, offset);
                            break;

                        case TextureTarget.TextureCubeMapArray:
                            _device.GL.TexSubImage3D(glTexture.Target, (int)c.MipLevel,
                                (int)c.X, (int)c.Y, (int)c.ArrayLayer, c.Width, c.Height, 1, format, type, offset);
                            break;

                        case TextureTarget.Texture2DMultisample:
                        case TextureTarget.Texture2DMultisampleArray:
                            throw new NotSupportedException(
                                "Cannot copy buffer data to multisample textures. Multisample textures can only be rendered to.");

                        default:
                            throw new NotSupportedException($"Unsupported texture target for buffer copy: {glTexture.Target}");
                    }
                }
            }

            // Reset pixel store
            _device.GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            _device.GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 0);
            _device.GL.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        });
    }

    /// <summary>
    /// Calculates the size of compressed texture data in bytes.
    /// </summary>
    private static uint CalculateCompressedImageSize(TextureFormat format, uint width, uint height, uint depth)
    {
        // BC formats use 4x4 blocks
        uint blocksX = (width + 3) / 4;
        uint blocksY = (height + 3) / 4;
        uint blockSize = GetCompressedBlockSize(format);
        return blocksX * blocksY * depth * blockSize;
    }

    /// <summary>
    /// Returns the byte size of a single compressed block for the given format.
    /// </summary>
    private static uint GetCompressedBlockSize(TextureFormat format) => format switch
    {
        // BC1 (DXT1) - 8 bytes per 4x4 block
        TextureFormat.BC1Unorm or TextureFormat.BC1UnormSrgb => 8,
        // BC2 (DXT3) - 16 bytes per 4x4 block
        TextureFormat.BC2Unorm or TextureFormat.BC2UnormSrgb => 16,
        // BC3 (DXT5) - 16 bytes per 4x4 block
        TextureFormat.BC3Unorm or TextureFormat.BC3UnormSrgb => 16,
        // BC4 (RGTC1) - 8 bytes per 4x4 block
        TextureFormat.BC4Unorm or TextureFormat.BC4Snorm => 8,
        // BC5 (RGTC2) - 16 bytes per 4x4 block
        TextureFormat.BC5Unorm or TextureFormat.BC5Snorm => 16,
        // BC6H (BPTC float) - 16 bytes per 4x4 block
        TextureFormat.BC6HUfloat or TextureFormat.BC6HSfloat => 16,
        // BC7 (BPTC) - 16 bytes per 4x4 block
        TextureFormat.BC7Unorm or TextureFormat.BC7UnormSrgb => 16,
        _ => 16, // Default to 16 for unknown compressed formats
    };

    protected override void CopyTextureToBufferCore(in BufferTextureCopy copy)
    {
        var c = copy;
        _commands.Add(() =>
        {
            if (c.Buffer is not GLBuffer glBuffer || c.Texture is not GLTexture glTexture) return;

            _device.GL.BindBuffer(BufferTargetARB.PixelPackBuffer, glBuffer.Handle);

            if (GLTexture.IsCompressedFormat(glTexture.Format))
            {
                // Compressed textures require glGetCompressedTexImage
                // Note: This reads the entire mip level - sub-region reads not supported for compressed
                _device.GL.BindTexture(glTexture.Target, glTexture.Handle);

                unsafe
                {
                    void* offset = (void*)c.BufferOffset;

                    switch (glTexture.Target)
                    {
                        case TextureTarget.TextureCubeMap:
                            // For cubemaps, read the specific face
                            _device.GL.GetCompressedTexImage(GetCubemapFaceTarget(c.ArrayLayer), (int)c.MipLevel, offset);
                            break;

                        default:
                            // For other texture types, read directly
                            _device.GL.GetCompressedTexImage(glTexture.Target, (int)c.MipLevel, offset);
                            break;
                    }
                }

                _device.GL.BindTexture(glTexture.Target, 0);
            }
            else
            {
                // Non-compressed textures use framebuffer + glReadPixels for sub-region support
                var format = GetPixelFormat(glTexture.Format);
                var type = GetPixelType(glTexture.Format);

                uint tempFbo = _device.GL.GenFramebuffer();
                _device.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, tempFbo);

                // Determine the correct attachment point based on texture format
                var attachment = GetFramebufferAttachment(glTexture.Format);

                // Attach the texture mip level to the framebuffer based on texture type
                switch (glTexture.Target)
                {
                    case TextureTarget.Texture1D:
                        _device.GL.FramebufferTexture1D(FramebufferTarget.ReadFramebuffer,
                            attachment, glTexture.Target, glTexture.Handle, (int)c.MipLevel);
                        break;

                    case TextureTarget.Texture1DArray:
                    case TextureTarget.Texture2DArray:
                        _device.GL.FramebufferTextureLayer(FramebufferTarget.ReadFramebuffer,
                            attachment, glTexture.Handle, (int)c.MipLevel, (int)c.ArrayLayer);
                        break;

                    case TextureTarget.Texture3D:
                        _device.GL.FramebufferTextureLayer(FramebufferTarget.ReadFramebuffer,
                            attachment, glTexture.Handle, (int)c.MipLevel, (int)c.Z);
                        break;

                    case TextureTarget.TextureCubeMap:
                        _device.GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer,
                            attachment, GetCubemapFaceTarget(c.ArrayLayer), glTexture.Handle, (int)c.MipLevel);
                        break;

                    case TextureTarget.TextureCubeMapArray:
                        _device.GL.FramebufferTextureLayer(FramebufferTarget.ReadFramebuffer,
                            attachment, glTexture.Handle, (int)c.MipLevel, (int)c.ArrayLayer);
                        break;

                    default:
                        _device.GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer,
                            attachment, glTexture.Target, glTexture.Handle, (int)c.MipLevel);
                        break;
                }

                // Set pixel store parameters for row padding
                if (c.BufferRowLength > 0)
                    _device.GL.PixelStore(PixelStoreParameter.PackRowLength, (int)c.BufferRowLength);

                unsafe
                {
                    void* offset = (void*)c.BufferOffset;
                    _device.GL.ReadPixels((int)c.X, (int)c.Y, c.Width, c.Height, format, type, offset);
                }

                // Reset state
                _device.GL.PixelStore(PixelStoreParameter.PackRowLength, 0);
                _device.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                _device.GL.DeleteFramebuffer(tempFbo);
            }

            _device.GL.BindBuffer(BufferTargetARB.PixelPackBuffer, 0);
        });
    }

    protected override void CopyTextureToTextureCore(in TextureTextureCopy copy)
    {
        var c = copy;
        _commands.Add(() =>
        {
            if (c.Source is not GLTexture srcTex || c.Destination is not GLTexture dstTex) return;

            // For glCopyImageSubData, the Z parameter means:
            // - For 3D textures: actual Z coordinate
            // - For array textures (1DArray, 2DArray, CubeMapArray): array layer
            // - For cubemaps: face index (0-5)
            int srcZ = srcTex.Target switch
            {
                TextureTarget.Texture3D => (int)c.SourceZ,
                _ => (int)c.SourceArrayLayer
            };

            int dstZ = dstTex.Target switch
            {
                TextureTarget.Texture3D => (int)c.DestinationZ,
                _ => (int)c.DestinationArrayLayer
            };

            _device.GL.CopyImageSubData(
                srcTex.Handle, (CopyImageSubDataTarget)srcTex.Target, (int)c.SourceMipLevel,
                (int)c.SourceX, (int)c.SourceY, srcZ,
                dstTex.Handle, (CopyImageSubDataTarget)dstTex.Target, (int)c.DestinationMipLevel,
                (int)c.DestinationX, (int)c.DestinationY, dstZ,
                c.Width, c.Height, c.Depth);
        });
    }

    /// <summary>
    /// Converts a cubemap face index (0-5) to the corresponding OpenGL TextureTarget.
    /// Face order: +X, -X, +Y, -Y, +Z, -Z
    /// </summary>
    private static TextureTarget GetCubemapFaceTarget(uint faceIndex)
    {
        if (faceIndex > 5)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), $"Cubemap face index must be 0-5, got {faceIndex}");
        return (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + (int)faceIndex);
    }

    // Complete pixel format mapping matching GLTexture.GetPixelFormat
    private static PixelFormat GetPixelFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm or TextureFormat.R8Snorm => PixelFormat.Red,
        TextureFormat.R8Uint or TextureFormat.R8Sint => PixelFormat.RedInteger,
        TextureFormat.R16Uint or TextureFormat.R16Sint => PixelFormat.RedInteger,
        TextureFormat.R16Float or TextureFormat.R32Float => PixelFormat.Red,
        TextureFormat.R32Uint or TextureFormat.R32Sint => PixelFormat.RedInteger,
        TextureFormat.RG8Unorm or TextureFormat.RG8Snorm => PixelFormat.RG,
        TextureFormat.RG8Uint or TextureFormat.RG8Sint => PixelFormat.RGInteger,
        TextureFormat.RG16Uint or TextureFormat.RG16Sint => PixelFormat.RGInteger,
        TextureFormat.RG16Float or TextureFormat.RG32Float => PixelFormat.RG,
        TextureFormat.RG32Uint or TextureFormat.RG32Sint => PixelFormat.RGInteger,
        TextureFormat.RGBA8Unorm or TextureFormat.RGBA8UnormSrgb or TextureFormat.RGBA8Snorm => PixelFormat.Rgba,
        TextureFormat.RGBA8Uint or TextureFormat.RGBA8Sint => PixelFormat.RgbaInteger,
        TextureFormat.BGRA8Unorm or TextureFormat.BGRA8UnormSrgb => PixelFormat.Bgra,
        TextureFormat.RGBA16Uint or TextureFormat.RGBA16Sint => PixelFormat.RgbaInteger,
        TextureFormat.RGBA16Float => PixelFormat.Rgba,
        TextureFormat.RGBA32Uint or TextureFormat.RGBA32Sint => PixelFormat.RgbaInteger,
        TextureFormat.RGBA32Float => PixelFormat.Rgba,
        TextureFormat.RGB10A2Unorm or TextureFormat.RG11B10Float => PixelFormat.Rgba,
        TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32Float => PixelFormat.DepthComponent,
        TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32FloatStencil8 => PixelFormat.DepthStencil,
        _ => throw new NotSupportedException($"Unsupported texture format for pixel operations: {format}"),
    };

    // Complete pixel type mapping matching GLTexture.GetPixelType
    private static PixelType GetPixelType(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm or TextureFormat.R8Uint or TextureFormat.RG8Unorm or TextureFormat.RG8Uint
            or TextureFormat.RGBA8Unorm or TextureFormat.RGBA8UnormSrgb or TextureFormat.RGBA8Uint
            or TextureFormat.BGRA8Unorm or TextureFormat.BGRA8UnormSrgb => PixelType.UnsignedByte,
        TextureFormat.R8Snorm or TextureFormat.R8Sint or TextureFormat.RG8Snorm or TextureFormat.RG8Sint
            or TextureFormat.RGBA8Snorm or TextureFormat.RGBA8Sint => PixelType.Byte,
        TextureFormat.R16Uint or TextureFormat.RG16Uint or TextureFormat.RGBA16Uint => PixelType.UnsignedShort,
        TextureFormat.R16Sint or TextureFormat.RG16Sint or TextureFormat.RGBA16Sint => PixelType.Short,
        TextureFormat.R16Float or TextureFormat.RG16Float or TextureFormat.RGBA16Float => PixelType.HalfFloat,
        TextureFormat.R32Uint or TextureFormat.RG32Uint or TextureFormat.RGBA32Uint => PixelType.UnsignedInt,
        TextureFormat.R32Sint or TextureFormat.RG32Sint or TextureFormat.RGBA32Sint => PixelType.Int,
        TextureFormat.R32Float or TextureFormat.RG32Float or TextureFormat.RGBA32Float => PixelType.Float,
        TextureFormat.RGB10A2Unorm => PixelType.UnsignedInt2101010Rev,
        TextureFormat.RG11B10Float => PixelType.UnsignedInt10f11f11fRev,
        TextureFormat.Depth16Unorm => PixelType.UnsignedShort,
        TextureFormat.Depth24Plus or TextureFormat.Depth32Float => PixelType.Float,
        TextureFormat.Depth24PlusStencil8 => PixelType.UnsignedInt248,
        TextureFormat.Depth32FloatStencil8 => PixelType.Float32UnsignedInt248Rev,
        _ => throw new NotSupportedException($"Unsupported texture format for pixel operations: {format}"),
    };

    // Returns the correct framebuffer attachment point for a texture format
    private static FramebufferAttachment GetFramebufferAttachment(TextureFormat format) => format switch
    {
        TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32Float
            => FramebufferAttachment.DepthAttachment,
        TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32FloatStencil8
            => FramebufferAttachment.DepthStencilAttachment,
        _ => FramebufferAttachment.ColorAttachment0,
    };

    // Returns true if the texture format is an sRGB format
    private static bool IsSrgbFormat(TextureFormat format) => format switch
    {
        TextureFormat.RGBA8UnormSrgb or TextureFormat.BGRA8UnormSrgb
            or TextureFormat.BC1UnormSrgb or TextureFormat.BC2UnormSrgb
            or TextureFormat.BC3UnormSrgb or TextureFormat.BC7UnormSrgb => true,
        _ => false,
    };

    #endregion

    #region Synchronization

    protected override void ResourceBarrierCore(in ResourceBarrier barrier)
    {
        // OpenGL handles most synchronization implicitly
        // For explicit barriers, we use glMemoryBarrier
        // Capture the value to avoid capturing the 'in' parameter
        var stateAfter = barrier.StateAfter;
        _commands.Add(() =>
        {
            MemoryBarrierMask mask = stateAfter switch
            {
                ResourceState.ShaderResource => MemoryBarrierMask.TextureFetchBarrierBit,
                ResourceState.UnorderedAccess => MemoryBarrierMask.ShaderImageAccessBarrierBit,
                ResourceState.RenderTarget => MemoryBarrierMask.FramebufferBarrierBit,
                ResourceState.CopySource or ResourceState.CopyDestination => MemoryBarrierMask.TextureUpdateBarrierBit,
                ResourceState.VertexBuffer => MemoryBarrierMask.VertexAttribArrayBarrierBit,
                ResourceState.IndexBuffer => MemoryBarrierMask.ElementArrayBarrierBit,
                ResourceState.UniformBuffer => MemoryBarrierMask.UniformBarrierBit,
                _ => MemoryBarrierMask.AllBarrierBits,
            };
            _device.GL.MemoryBarrier(mask);
        });
    }

    protected override void MemoryBarrierCore()
    {
        _commands.Add(() => _device.GL.MemoryBarrier(MemoryBarrierMask.AllBarrierBits));
    }

    #endregion

    #region Debug Markers

    protected override void PushDebugGroupCore(string name)
    {
        _commands.Add(() => _device.GL.PushDebugGroup(DebugSource.DebugSourceApplication, 0, (uint)name.Length, name));
    }

    protected override void PopDebugGroupCore()
    {
        _commands.Add(() => _device.GL.PopDebugGroup());
    }

    protected override void InsertDebugMarkerCore(string name)
    {
        _commands.Add(() => _device.GL.DebugMessageInsert(DebugSource.DebugSourceApplication, DebugType.DebugTypeMarker, 0, DebugSeverity.DebugSeverityNotification, (uint)name.Length, name));
    }

    #endregion

    protected override void DisposeResources()
    {
        _commands.Clear();
    }
}
