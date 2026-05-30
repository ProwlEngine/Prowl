using System.Collections.Generic;

using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

namespace Prowl.Veldrid.OpenGL;

/// <summary>
/// Static helpers used by <see cref="OpenGLShaderProgram"/> and <see cref="OpenGLComputeProgram"/>
/// to bind vertex attribute locations and to build the per-set GL binding tables for a linked
/// GL program at link time.
/// </summary>
internal static class OpenGLCachedPipeline
{
    private const uint GL_INVALID_INDEX = 0xFFFFFFFF;

    /// <summary>
    /// Wires each <see cref="VertexElementDescription"/> to its shader attribute location
    /// before the GL program is linked.
    /// </summary>
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

    /// <summary>
    /// Queries the linked GL program for the uniform / texture / storage-block bindings declared
    /// in each <see cref="ResourceLayoutDescription"/>, producing a <see cref="SetBindingsInfo"/>
    /// per set slot that the executor can later use at draw / dispatch time.
    /// </summary>
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

            Dictionary<uint, OpenGLUniformBinding> uniformBindings = [];
            Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings = [];
            Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings = [];

            for (uint i = 0; i < resources.Length; i++)
            {
                ResourceLayoutElementDescription resource = resources[i];
                int bindingIndex = resource.BindingIndex;
                string resourceName = resource.GLUniformName
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

    public readonly bool GetTextureBindingInfo(uint slot, out OpenGLTextureBindingSlotInfo binding)
    {
        return _textureBindings.TryGetValue(slot, out binding);
    }

    public readonly bool GetUniformBindingForSlot(uint slot, out OpenGLUniformBinding binding)
    {
        return _uniformBindings.TryGetValue(slot, out binding);
    }

    public readonly bool GetStorageBufferBindingForSlot(uint slot, out OpenGLShaderStorageBinding binding)
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
