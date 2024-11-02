// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public class CommandBuffer : IDisposable
{
    private static Material s_blit;

    internal CommandList _commandList;
    private bool _isRecording = false;

    private Framebuffer _activeFramebuffer;
    private KeywordState _keywordState;

    private PropertyState _bufferProperties;
    private IGeometryDrawData _activeDrawData;

    private ShaderPipelineDescription _pipelineDescription;

    private PolygonFillMode _fill;
    private PrimitiveTopology _topology;
    private bool _scissor;

    private ShaderPipeline _graphicsPipeline;
    private BindableResourceSet _pipelineResources;
    private Pipeline _actualActivePipeline;

    private List<IDisposable> _resourcesToDispose;


    public string Name
    {
        get => _commandList.Name;
        set => _commandList.Name = value;
    }


    private void ResetState()
    {
        _activeFramebuffer = null;
        _keywordState = KeywordState.Empty;

        _bufferProperties.Clear();
        _resourcesToDispose.Clear();
        _activeDrawData = null;

        _pipelineDescription = default;

        _fill = PolygonFillMode.Solid;
        _topology = PrimitiveTopology.TriangleList;
        _scissor = false;

        _graphicsPipeline = null;
        _pipelineResources = null;
        _actualActivePipeline = null;
    }


    public CommandBuffer() : this("New Command Buffer")
    { }

    public CommandBuffer(string name)
    {
        _commandList = Graphics.Factory.CreateCommandList();

        _bufferProperties = new();
        _resourcesToDispose = new();

        ResetState();

        Name = name;

        BeginRecording();
    }

    public void SetRenderTarget(Framebuffer framebuffer)
    {
        _activeFramebuffer = framebuffer;
        _pipelineDescription.output = _activeFramebuffer.OutputDescription;

        _commandList.SetFramebuffer(framebuffer);
    }

    public void SetRenderTarget(RenderTexture renderTarget)
        => SetRenderTarget(renderTarget.Framebuffer);

    public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, int attachment = -1, float depth = 1, byte stencil = 0)
    {
        if (clearDepth)
            _commandList.ClearDepthStencil(depth, stencil);

        RgbaFloat colorF = new RgbaFloat(backgroundColor);

        if (clearColor)
        {
            if (attachment < 0)
            {
                for (uint i = 0; i < _activeFramebuffer.ColorTargets.Length; i++)
                    _commandList.ClearColorTarget(i, colorF);
            }
            else
            {
                _commandList.ClearColorTarget((uint)attachment, colorF);
            }
        }
    }

    public void SetMaterial(Material material, int pass = 0)
    {
        _bufferProperties.ApplyOverride(material._properties);
        SetPass(material.Shader.Res.GetPass(pass));
        BindResources();
    }

    public void DrawSingle(IGeometryDrawData drawData, int indexCount = -1, uint indexOffset = 0)
    {
        SetDrawData(drawData);
        BindResources();
        DrawIndexed((uint)(indexCount <= 0 ? drawData.IndexCount : indexCount), indexOffset, 1, 0, 0);
    }

    public void SetDrawData(IGeometryDrawData drawData)
    {
        _topology = drawData.Topology;
        _activeDrawData = drawData;
        _activeDrawData.SetDrawData(_commandList, _graphicsPipeline);
    }

    public void DrawIndexed(uint indexCount, uint indexOffset, uint instanceCount, uint instanceStart, int vertexOffset)
    {
        UpdateActualPipeline();
        _commandList.DrawIndexed(indexCount, instanceCount, indexOffset, vertexOffset, instanceStart);
    }

    public void DrawIndirect(GraphicsBuffer buffer, uint offset, uint drawCount, uint stride)
    {
        UpdateActualPipeline();
        _commandList.DrawIndirect(buffer.Buffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(GraphicsBuffer buffer, uint offset, uint drawCount, uint stride)
    {
        UpdateActualPipeline();
        _commandList.DrawIndexedIndirect(buffer.Buffer, offset, drawCount, stride);
    }

    public void SetPass(ShaderPass pass)
    {
        _pipelineDescription.pass = pass;
        _pipelineDescription.variant = _pipelineDescription.pass.GetVariant(_keywordState);

        UpdatePipeline();
    }

    public void BindResources()
    {
        UpdatePipeline();
        ResourceSet set = _pipelineResources.BindResources(_commandList, _bufferProperties, _resourcesToDispose);
        _commandList.SetGraphicsResourceSet(0, set);
    }

    public void ApplyPropertyState(PropertyState state)
        => _bufferProperties.ApplyOverride(state);

    public void UpdateBuffer(string name)
        => _pipelineResources.UpdateBuffer(_commandList, name, _bufferProperties);

    public void PushDebugGroup(string name)
        => _commandList.PushDebugGroup(name);

    public void PopDebugGroup()
        => _commandList.PopDebugGroup();

    public void ResolveMultisampledTexture(Texture src, Texture dest)
    {
        if (!src.Equals(dest, false))
            throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

        _commandList.ResolveTexture(src.InternalTexture, dest.InternalTexture);
    }

    public void ResolveMultisampledTexture(RenderTexture src, RenderTexture dest)
    {
        if (!src.FormatEquals(dest, false))
            throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

        for (int i = 0; i < src.ColorBuffers.Length; i++)
            _commandList.ResolveTexture(src.ColorBuffers[i].InternalTexture, dest.ColorBuffers[i].InternalTexture);
    }

    public void SetViewport(uint viewport, int x, int y, int width, int height, float z, float depth)
        => _commandList.SetViewport(viewport, new Viewport(x, y, width, height, z, depth));

    public void SetViewports(int x, int y, int width, int height, float z, float depth)
        => _commandList.SetViewports(new Viewport(x, y, width, height, z, depth));

    public void SetFullViewport(uint index = 0)
        => _commandList.SetFullViewport(index);

    public void SetFullViewports()
        => _commandList.SetFullViewports();

    public void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        => _commandList.SetScissorRect(index, x, y, width, height);

    public void SetScissorRects(uint x, uint y, uint width, uint height)
        => _commandList.SetScissorRects(x, y, width, height);

    public void SetFullScissorRect(uint index)
        => _commandList.SetFullScissorRect(index);

    public void SetFullScissorRects()
        => _commandList.SetFullScissorRects();

    public void SetScissor(bool active)
        => _scissor = active;


    public void SetKeyword(string keyword, string value)
    {
        _keywordState.SetKey(keyword, value);
        _pipelineDescription.variant = _pipelineDescription.pass.GetVariant(_keywordState);
        UpdatePipeline();
    }

    public void SetWireframe(bool wireframe)
        => _fill = wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid;

    public void SetTexture(string name, Texture texture)
        => _bufferProperties.SetTexture(name, texture);

    public void SetTexture(string name, Veldrid.Texture texture)
        => _bufferProperties.SetRawTexture(name, texture);

    public void SetInt(string name, int value)
        => _bufferProperties.SetInt(name, value);

    public void SetFloat(string name, float value)
        => _bufferProperties.SetFloat(name, value);

    public void SetVector(string name, System.Numerics.Vector2 value)
        => _bufferProperties.SetVector(name, value);

    public void SetVector(string name, System.Numerics.Vector3 value)
        => _bufferProperties.SetVector(name, value);

    public void SetVector(string name, System.Numerics.Vector4 value)
        => _bufferProperties.SetVector(name, value);

    public void SetColor(string name, Color value)
        => _bufferProperties.SetVector(name, value);

    public void SetMatrix(string name, System.Numerics.Matrix4x4 value)
        => _bufferProperties.SetMatrix(name, value);

    public void SetBuffer(string name, GraphicsBuffer buffer, int start = 0, int length = -1)
        => _bufferProperties.SetBuffer(name, buffer, start, length);


    internal void UpdatePipeline()
    {
        ShaderPipeline newPipeline = ShaderPipelineCache.GetPipeline(_pipelineDescription);

        if (newPipeline != _graphicsPipeline)
        {
            _graphicsPipeline = newPipeline;

            _pipelineResources?.DisposeResources(_resourcesToDispose);
            _pipelineResources = _graphicsPipeline.CreateResources();
        }

        UpdateActualPipeline();
    }


    internal void UpdateActualPipeline()
    {
        _actualActivePipeline = _graphicsPipeline.GetPipeline(_fill, _topology, _scissor);
        _commandList.SetPipeline(_actualActivePipeline);
    }


    internal void BeginRecording()
    {
        if (!_isRecording)
            _commandList.Begin();

        _isRecording = true;
    }


    public void Clear()
    {
        if (_isRecording)
            _commandList.End();

        _isRecording = false;

        Graphics.SubmitResourcesForDisposal(_resourcesToDispose);

        ResetState();
    }


    public void Dispose()
    {
        _commandList.Dispose();
        _pipelineResources?.DisposeResources(_resourcesToDispose);

        Graphics.SubmitResourcesForDisposal(_resourcesToDispose);

        GC.SuppressFinalize(this);
    }


    ~CommandBuffer()
    {
        Dispose();
    }
}
