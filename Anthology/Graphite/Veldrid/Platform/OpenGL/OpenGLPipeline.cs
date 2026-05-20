using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;

using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLPipeline : Pipeline, OpenGLDeferredResource
{
    private const uint GL_INVALID_INDEX = 0xFFFFFFFF;
    private readonly OpenGLGraphicsDevice _gd;
    private GL _gl => _gd.GL;

    // Graphics Pipeline
    public ShaderProgram[] GraphicsShaders { get; }
    public VertexLayoutDescription[] VertexLayouts { get; }
    public BlendStateDescription BlendState { get; }
    public DepthStencilStateDescription DepthStencilState { get; }
    public RasterizerStateDescription RasterizerState { get; }
    public PrimitiveTopology PrimitiveTopology { get; }

    // Compute Pipeline
    public override bool IsComputePipeline { get; }
    public ShaderProgram ComputeShader { get; }

    private uint _program;
    private bool _disposeRequested;
    private bool _disposed;

    private SetBindingsInfo[] _setInfos;

    public int[] VertexStrides { get; }

    public uint Program => _program;

    public override string Name { get; set; }

    public override bool IsDisposed => _disposeRequested;

    public OpenGLPipeline(OpenGLGraphicsDevice gd, ref GraphicsPipelineDescription description)
        : base(ref description)
    {
        _gd = gd;
        GraphicsShaders = Util.ShallowClone(description.ShaderSet.Shaders);
        VertexLayouts = Util.ShallowClone(description.ShaderSet.VertexLayouts);
        BlendState = description.BlendState.ShallowClone();
        DepthStencilState = description.DepthStencilState;
        RasterizerState = description.RasterizerState;
        PrimitiveTopology = description.PrimitiveTopology;

        int numVertexBuffers = description.ShaderSet.VertexLayouts.Length;
        VertexStrides = new int[numVertexBuffers];
        for (int i = 0; i < numVertexBuffers; i++)
        {
            VertexStrides[i] = (int)description.ShaderSet.VertexLayouts[i].Stride;
        }

        OpenGLPipeline_StoreResourceLayouts(description.ResourceLayouts);
    }

    public OpenGLPipeline(OpenGLGraphicsDevice gd, ref ComputePipelineDescription description)
        : base(ref description)
    {
        _gd = gd;
        IsComputePipeline = true;
        ComputeShader = description.ComputeShader;
        VertexStrides = Array.Empty<int>();
        OpenGLPipeline_StoreResourceLayouts(description.ResourceLayouts);
    }

    public bool Created { get; private set; }

    public void EnsureResourcesCreated()
    {
        if (!Created)
        {
            CreateGLResources();
        }
    }

    private void CreateGLResources()
    {
        if (!IsComputePipeline)
        {
            CreateGraphicsGLResources();
        }
        else
        {
            CreateComputeGLResources();
        }

        Created = true;
    }

    private void CreateGraphicsGLResources()
    {
        _program = _gl.CreateProgram();
        CheckLastError();
        foreach (ShaderProgram stage in GraphicsShaders)
        {
            OpenGLShader glShader = Util.AssertSubtype<ShaderProgram, OpenGLShader>(stage);
            glShader.EnsureResourcesCreated();
            _gl.AttachShader(_program, glShader.Shader);
            CheckLastError();
        }

        uint slot = 0;
        foreach (VertexLayoutDescription layoutDesc in VertexLayouts)
        {
            for (int i = 0; i < layoutDesc.Elements.Length; i++)
            {
                BindAttribLocation(slot, layoutDesc.Elements[i].Name);
                slot += 1;
            }
        }

        _gl.LinkProgram(_program);
        CheckLastError();

        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        CheckLastError();
        if (linkStatus != 1)
        {
            string log = _gl.GetProgramInfoLog(_program);
            CheckLastError();
            throw new RenderException($"Error linking GL program: {log}");
        }

        ProcessResourceSetLayouts(ResourceLayouts);
    }

    void BindAttribLocation(uint slot, string elementName)
    {
        _gl.BindAttribLocation(_program, slot, elementName);
        CheckLastError();
    }

    private void ProcessResourceSetLayouts(ResourceLayout[] layouts)
    {
        int resourceLayoutCount = layouts.Length;
        _setInfos = new SetBindingsInfo[resourceLayoutCount];
        for (uint setSlot = 0; setSlot < resourceLayoutCount; setSlot++)
        {
            OpenGLResourceLayout glSetLayout = Util.AssertSubtype<ResourceLayout, OpenGLResourceLayout>(layouts[setSlot]);
            ResourceLayoutElementDescription[] resources = glSetLayout.Elements;

            Dictionary<uint, OpenGLUniformBinding> uniformBindings = new Dictionary<uint, OpenGLUniformBinding>();
            Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings = new Dictionary<uint, OpenGLTextureBindingSlotInfo>();
            Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings = new Dictionary<uint, OpenGLShaderStorageBinding>();

            for (uint i = 0; i < resources.Length; i++)
            {
                ResourceLayoutElementDescription resource = resources[i];
                int bindingIndex = resource.BindingIndex;
                if (resource.Kind == ResourceKind.UniformBuffer)
                {
                    uint blockIndex = GetUniformBlockIndex(resource.Name);
                    if (blockIndex != GL_INVALID_INDEX)
                    {
                        _gl.GetActiveUniformBlock(_program, blockIndex, UniformBlockPName.DataSize, out int blockSize);
                        CheckLastError();
                        uniformBindings[i] = new OpenGLUniformBinding(_program, blockIndex, (uint)blockSize, (uint)bindingIndex);
                    }
                }
                else if (resource.Kind == ResourceKind.TextureReadOnly
                    || resource.Kind == ResourceKind.TextureReadWrite)
                {
                    int location = GetUniformLocation(resource.Name);
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
                    if (_gd.BackendType == GraphicsBackend.OpenGL)
                    {
                        storageBlockBinding = GetProgramResourceIndex(resource.Name, ProgramInterface.ShaderStorageBlock);
                    }
                    else
                    {
                        storageBlockBinding = (uint)bindingIndex;
                    }

                    storageBufferBindings[i] = new OpenGLShaderStorageBinding(storageBlockBinding, (uint)bindingIndex);
                }
                // Sampler elements are looked up at bind-time alongside their paired texture.
            }

            _setInfos[setSlot] = new SetBindingsInfo(uniformBindings, textureBindings, storageBufferBindings);
        }
    }

    uint GetUniformBlockIndex(string resourceName)
    {
        uint blockIndex = _gl.GetUniformBlockIndex(_program, resourceName);
        CheckLastError();
#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
        if (blockIndex == GL_INVALID_INDEX)
        {
            uint uniformBufferIndex = 0;
            uint bufferNameByteCount = 64;
            byte* bufferNamePtr = stackalloc byte[(int)bufferNameByteCount];
            var names = new List<string>();
            while (true)
            {
                uint actualLength;
                _gl.GetActiveUniformBlockName(_program, uniformBufferIndex, bufferNameByteCount, &actualLength, bufferNamePtr);

                if (_gl.GetError() != 0)
                {
                    break;
                }

                string name = Encoding.UTF8.GetString(bufferNamePtr, (int)actualLength);
                names.Add(name);
                uniformBufferIndex++;
            }

            throw new VeldridException($"Unable to bind uniform buffer \"{resourceName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
        }
#endif
        return blockIndex;
    }

    int GetUniformLocation(string resourceName)
    {
        int location = _gl.GetUniformLocation(_program, resourceName);
        CheckLastError();
        return location;
    }

    uint GetProgramResourceIndex(string resourceName, ProgramInterface resourceType)
    {
        uint binding = _gl.GetProgramResourceIndex(_program, resourceType, resourceName);
        CheckLastError();
        return binding;
    }

    private void CreateComputeGLResources()
    {
        _program = _gl.CreateProgram();
        CheckLastError();
        OpenGLShader glShader = Util.AssertSubtype<ShaderProgram, OpenGLShader>(ComputeShader);
        glShader.EnsureResourcesCreated();
        _gl.AttachShader(_program, glShader.Shader);
        CheckLastError();

        _gl.LinkProgram(_program);
        CheckLastError();

        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        CheckLastError();
        if (linkStatus != 1)
        {
            string log = _gl.GetProgramInfoLog(_program);
            CheckLastError();
            throw new RenderException($"Error linking GL program: {log}");
        }

        ProcessResourceSetLayouts(ResourceLayouts);
    }

    public bool GetUniformBindingForSlot(uint set, uint slot, out OpenGLUniformBinding binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        SetBindingsInfo setInfo = _setInfos[set];
        return setInfo.GetUniformBindingForSlot(slot, out binding);
    }

    public bool GetTextureBindingInfo(uint set, uint slot, out OpenGLTextureBindingSlotInfo binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        SetBindingsInfo setInfo = _setInfos[set];
        return setInfo.GetTextureBindingInfo(slot, out binding);
    }

    public bool GetStorageBufferBindingForSlot(uint set, uint slot, out OpenGLShaderStorageBinding binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        SetBindingsInfo setInfo = _setInfos[set];
        return setInfo.GetStorageBufferBindingForSlot(slot, out binding);
    }

    public override void Dispose()
    {
        if (!_disposeRequested)
        {
            _disposeRequested = true;
            _gd.EnqueueDisposal(this);
        }
    }

    public void DestroyGLResources()
    {
        if (!_disposed)
        {
            _disposed = true;
            _gl.DeleteProgram(_program);
            CheckLastError();
        }
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
    /// <summary>The texture unit this binding occupies (the layout element's BindingIndex).</summary>
    public int RelativeIndex;
    /// <summary>The uniform location of the binding in the shader program.</summary>
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
