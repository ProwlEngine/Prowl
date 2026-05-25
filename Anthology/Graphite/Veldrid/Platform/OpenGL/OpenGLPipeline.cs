using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

using System.Text;
using System.Collections.Generic;
using System;

namespace Prowl.Veldrid.OpenGL;

internal unsafe class OpenGLPipeline : Pipeline, OpenGLDeferredResource
{
    private const uint GL_INVALID_INDEX = 0xFFFFFFFF;
    private readonly OpenGLGraphicsDevice _gd;
    private GL _gl => _gd.GL;

    private readonly OpenGLShaderProgram _graphicsProgram;
    private readonly OpenGLComputeProgram _computeProgram;

    private readonly PrimitiveTopology _primitiveTopology;
    private readonly OutputDescription _outputs;
    private readonly bool _isComputePipeline;

    private bool _disposeRequested;
    private bool _disposed;

    public override bool IsComputePipeline => _isComputePipeline;
    public override string Name { get; set; }
    public override bool IsDisposed => _disposeRequested;

    public OpenGLShaderProgram ShaderProgram => _graphicsProgram;
    public OpenGLComputeProgram ComputeProgram => _computeProgram;

    public uint Program => _isComputePipeline ? _computeProgram.GLProgram : _graphicsProgram.GLProgram;

    public BlendStateDescription BlendState => _graphicsProgram.BlendState;
    public DepthStencilStateDescription DepthStencilState => _graphicsProgram.DepthStencilState;
    public RasterizerStateDescription RasterizerState => _graphicsProgram.RasterizerState;
    public IReadOnlyList<VertexLayoutDescription> VertexLayouts => _graphicsProgram.VertexLayouts;
    public PrimitiveTopology PrimitiveTopology => _primitiveTopology;
    public int[] VertexStrides => _isComputePipeline ? Array.Empty<int>() : _graphicsProgram.VertexStrides;

    public int ResourceLayoutCount => _isComputePipeline
        ? _computeProgram.ResourceLayouts.Count
        : _graphicsProgram.ResourceLayouts.Count;

    public OpenGLPipeline(OpenGLGraphicsDevice gd, ref GraphicsPipelineDescription description)
        : base(gd.ResourceFactory, ref description)
    {
        _gd = gd;
        _graphicsProgram = Util.AssertSubtype<ShaderProgram, OpenGLShaderProgram>(description.Program);
        _primitiveTopology = description.PrimitiveTopology;
        _outputs = description.Outputs;
        _isComputePipeline = false;
    }

    public OpenGLPipeline(OpenGLGraphicsDevice gd, ref ComputePipelineDescription description)
        : base(gd.ResourceFactory, ref description)
    {
        _gd = gd;
        _computeProgram = Util.AssertSubtype<ComputeProgram, OpenGLComputeProgram>(description.Program);
        _isComputePipeline = true;
    }

    public bool Created => _isComputePipeline
        ? _computeProgram.Created
        : _graphicsProgram.Created;

    public void EnsureResourcesCreated()
    {
        if (_isComputePipeline)
        {
            _computeProgram.EnsureResourcesCreated();
        }
        else
        {
            _graphicsProgram.EnsureResourcesCreated();
        }
    }

    public bool GetUniformBindingForSlot(uint set, uint slot, out OpenGLUniformBinding binding)
        => _isComputePipeline
            ? _computeProgram.GetUniformBindingForSlot(set, slot, out binding)
            : _graphicsProgram.GetUniformBindingForSlot(set, slot, out binding);

    public bool GetTextureBindingInfo(uint set, uint slot, out OpenGLTextureBindingSlotInfo binding)
        => _isComputePipeline
            ? _computeProgram.GetTextureBindingInfo(set, slot, out binding)
            : _graphicsProgram.GetTextureBindingInfo(set, slot, out binding);

    public bool GetStorageBufferBindingForSlot(uint set, uint slot, out OpenGLShaderStorageBinding binding)
        => _isComputePipeline
            ? _computeProgram.GetStorageBufferBindingForSlot(set, slot, out binding)
            : _graphicsProgram.GetStorageBufferBindingForSlot(set, slot, out binding);

    public override void Dispose()
    {
        if (!_disposeRequested)
        {
            _disposeRequested = true;
            DisposeAdapterResourceLayouts();
            _gd.EnqueueDisposal(this);
        }
    }

    public void DestroyGLResources()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    internal static void BindVertexAttribLocations(GL gl, uint program, IReadOnlyList<VertexLayoutDescription> layouts)
    {
        foreach (VertexLayoutDescription layoutDesc in layouts)
        {
            for (int i = 0; i < layoutDesc.Elements.Length; i++)
            {
                string attribName = VertexAttributeID.ToString(layoutDesc.Elements[i].Name)
                    ?? throw new RenderException("Vertex attribute name was not interned.");
                gl.BindAttribLocation(program, layoutDesc.Location + (uint)i, attribName);
                CheckLastError();
            }
        }
    }

    internal static SetBindingsInfo[] BuildSetBindingsInfo(
        GL gl,
        uint program,
        IReadOnlyList<ResourceLayoutDescription> resourceLayouts,
        GraphicsBackend backend)
    {
        int resourceLayoutCount = resourceLayouts.Count;
        SetBindingsInfo[] setInfos = new SetBindingsInfo[resourceLayoutCount];
        for (uint setSlot = 0; setSlot < resourceLayoutCount; setSlot++)
        {
            ResourceLayoutDescription layoutDesc = resourceLayouts[(int)setSlot];
            ResourceLayoutElementDescription[] resources = layoutDesc.Elements;

            Dictionary<uint, OpenGLUniformBinding> uniformBindings = new();
            Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings = new();
            Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings = new();

            for (uint i = 0; i < resources.Length; i++)
            {
                ResourceLayoutElementDescription resource = resources[i];
                int bindingIndex = resource.BindingIndex;
                string resourceName = ResourceID.ToString(resource.Name)
                    ?? throw new RenderException("Resource layout element name was not interned.");
                if (resource.Kind == ResourceKind.UniformBuffer)
                {
                    uint blockIndex = gl.GetUniformBlockIndex(program, resourceName);
                    CheckLastError();
                    if (blockIndex != GL_INVALID_INDEX)
                    {
                        gl.GetActiveUniformBlock(program, blockIndex, UniformBlockPName.DataSize, out int blockSize);
                        CheckLastError();
                        uniformBindings[i] = new OpenGLUniformBinding(program, blockIndex, (uint)blockSize, (uint)bindingIndex);
                    }
                }
                else if (resource.Kind == ResourceKind.TextureReadOnly
                    || resource.Kind == ResourceKind.TextureReadWrite)
                {
                    int location = gl.GetUniformLocation(program, resourceName);
                    CheckLastError();
                    textureBindings[i] = new OpenGLTextureBindingSlotInfo()
                    {
                        RelativeIndex = bindingIndex,
                        UniformLocation = location
                    };
                }
                else if (resource.Kind == ResourceKind.StructuredBufferReadOnly
                    || resource.Kind == ResourceKind.StructuredBufferReadWrite)
                {
                    uint storageBlockBinding;
                    if (backend == GraphicsBackend.OpenGL)
                    {
                        storageBlockBinding = gl.GetProgramResourceIndex(program, ProgramInterface.ShaderStorageBlock, resourceName);
                        CheckLastError();
                    }
                    else
                    {
                        storageBlockBinding = (uint)bindingIndex;
                    }
                    storageBufferBindings[i] = new OpenGLShaderStorageBinding(storageBlockBinding, (uint)bindingIndex);
                }
            }

            setInfos[setSlot] = new SetBindingsInfo(uniformBindings, textureBindings, storageBufferBindings);
        }
        return setInfos;
    }
}

internal struct SetBindingsInfo
{
    private readonly Dictionary<uint, OpenGLUniformBinding> _uniformBindings;
    private readonly Dictionary<uint, OpenGLTextureBindingSlotInfo> _textureBindings;
    private readonly Dictionary<uint, OpenGLShaderStorageBinding> _storageBufferBindings;

    public SetBindingsInfo(
        Dictionary<uint, OpenGLUniformBinding> uniformBindings,
        Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings,
        Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings)
    {
        _uniformBindings = uniformBindings;
        _textureBindings = textureBindings;
        _storageBufferBindings = storageBufferBindings;
    }

    public bool GetTextureBindingInfo(uint slot, out OpenGLTextureBindingSlotInfo binding)
    {
        return _textureBindings.TryGetValue(slot, out binding);
    }

    public bool GetUniformBindingForSlot(uint slot, out OpenGLUniformBinding binding)
    {
        return _uniformBindings.TryGetValue(slot, out binding);
    }

    public bool GetStorageBufferBindingForSlot(uint slot, out OpenGLShaderStorageBinding binding)
    {
        return _storageBufferBindings.TryGetValue(slot, out binding);
    }
}

internal struct OpenGLTextureBindingSlotInfo
{
    public int RelativeIndex;
    public int UniformLocation;
}

internal class OpenGLUniformBinding
{
    public uint Program { get; }
    public uint BlockLocation { get; }
    public uint BlockSize { get; }
    public uint BindingPoint { get; }

    public OpenGLUniformBinding(uint program, uint blockLocation, uint blockSize, uint bindingPoint)
    {
        Program = program;
        BlockLocation = blockLocation;
        BlockSize = blockSize;
        BindingPoint = bindingPoint;
    }
}

internal class OpenGLShaderStorageBinding
{
    public uint StorageBlockBinding { get; }
    public uint BindingPoint { get; }

    public OpenGLShaderStorageBinding(uint storageBlockBinding, uint bindingPoint)
    {
        StorageBlockBinding = storageBlockBinding;
        BindingPoint = bindingPoint;
    }
}
