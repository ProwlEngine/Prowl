using Silk.NET.OpenGL;
using static Veldrid.OpenGL.OpenGLUtil;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLPipeline : Pipeline, OpenGLDeferredResource
    {
        private const uint GL_INVALID_INDEX = 0xFFFFFFFF;
        private readonly OpenGLGraphicsDevice _gd;
        private GL _gl => _gd.GL;

#if !VALIDATE_USAGE
        public ResourceLayout[] ResourceLayouts { get; }
#endif

        // Graphics Pipeline
        public Shader[] GraphicsShaders { get; }
        public VertexLayoutDescription[] VertexLayouts { get; }
        public BlendStateDescription BlendState { get; }
        public DepthStencilStateDescription DepthStencilState { get; }
        public RasterizerStateDescription RasterizerState { get; }
        public PrimitiveTopology PrimitiveTopology { get; }

        // Compute Pipeline
        public override bool IsComputePipeline { get; }
        public Shader ComputeShader { get; }

        private uint _program;
        private bool _disposeRequested;
        private bool _disposed;

        private SetBindingsInfo[] _setInfos;

        public int[] VertexStrides { get; }

        public uint Program => _program;

        public uint GetUniformBufferCount(uint setSlot) => _setInfos[setSlot].UniformBufferCount;
        public uint GetShaderStorageBufferCount(uint setSlot) => _setInfos[setSlot].ShaderStorageBufferCount;

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

#if !VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(description.ResourceLayouts);
#endif
        }

        public OpenGLPipeline(OpenGLGraphicsDevice gd, ref ComputePipelineDescription description)
            : base(ref description)
        {
            _gd = gd;
            IsComputePipeline = true;
            ComputeShader = description.ComputeShader;
            VertexStrides = Array.Empty<int>();
#if !VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(description.ResourceLayouts);
#endif
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
            foreach (Shader stage in GraphicsShaders)
            {
                OpenGLShader glShader = Util.AssertSubtype<Shader, OpenGLShader>(stage);
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

#if DEBUG && GL_VALIDATE_VERTEX_INPUT_ELEMENTS
            slot = 0;
            foreach (VertexLayoutDescription layoutDesc in VertexLayouts)
            {
                for (int i = 0; i < layoutDesc.Elements.Length; i++)
                {
                    int location = GetAttribLocation(layoutDesc.Elements[i].Name);
                    if (location == -1)
                    {
                        throw new VeldridException("There was no attribute variable with the name " + layoutDesc.Elements[i].Name);
                    }

                    slot += 1;
                }
            }
#endif

            int linkStatus;
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out linkStatus);
            CheckLastError();
            if (linkStatus != 1)
            {
                string log = _gl.GetProgramInfoLog(_program);
                CheckLastError();
                throw new VeldridException($"Error linking GL program: {log}");
            }

            ProcessResourceSetLayouts(ResourceLayouts);
        }

        int GetAttribLocation(string elementName)
        {
            int location = _gl.GetAttribLocation(_program, elementName);
            return location;
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
            int lastTextureLocation = -1;
            int relativeTextureIndex = -1;
            int relativeImageIndex = -1;
            uint storageBlockIndex = 0; // Tracks OpenGL ES storage buffers.
            for (uint setSlot = 0; setSlot < resourceLayoutCount; setSlot++)
            {
                ResourceLayout setLayout = layouts[setSlot];
                OpenGLResourceLayout glSetLayout = Util.AssertSubtype<ResourceLayout, OpenGLResourceLayout>(setLayout);
                ResourceLayoutElementDescription[] resources = glSetLayout.Elements;

                Dictionary<uint, OpenGLUniformBinding> uniformBindings = new Dictionary<uint, OpenGLUniformBinding>();
                Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings = new Dictionary<uint, OpenGLTextureBindingSlotInfo>();
                Dictionary<uint, OpenGLSamplerBindingSlotInfo> samplerBindings = new Dictionary<uint, OpenGLSamplerBindingSlotInfo>();
                Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings = new Dictionary<uint, OpenGLShaderStorageBinding>();

                List<int> samplerTrackedRelativeTextureIndices = new List<int>();
                for (uint i = 0; i < resources.Length; i++)
                {
                    ResourceLayoutElementDescription resource = resources[i];
                    if (resource.Kind == ResourceKind.UniformBuffer)
                    {
                        uint blockIndex = GetUniformBlockIndex(resource.Name);
                        if (blockIndex != GL_INVALID_INDEX)
                        {
                            int blockSize;
                            _gl.GetActiveUniformBlock(_program, blockIndex, UniformBlockPName.DataSize, out blockSize);
                            CheckLastError();
                            uniformBindings[i] = new OpenGLUniformBinding(_program, blockIndex, (uint)blockSize);
                        }
                    }
                    else if (resource.Kind == ResourceKind.TextureReadOnly)
                    {
                        int location = GetUniformLocation(resource.Name);
                        relativeTextureIndex += 1;
                        textureBindings[i] = new OpenGLTextureBindingSlotInfo() { RelativeIndex = relativeTextureIndex, UniformLocation = location };
                        lastTextureLocation = location;
                        samplerTrackedRelativeTextureIndices.Add(relativeTextureIndex);
                    }
                    else if (resource.Kind == ResourceKind.TextureReadWrite)
                    {
                        int location = GetUniformLocation(resource.Name);
                        relativeImageIndex += 1;
                        textureBindings[i] = new OpenGLTextureBindingSlotInfo() { RelativeIndex = relativeImageIndex, UniformLocation = location };
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
                            storageBlockBinding = storageBlockIndex;
                            storageBlockIndex += 1;
                        }

                        storageBufferBindings[i] = new OpenGLShaderStorageBinding(storageBlockBinding);
                    }
                    else
                    {
                        Debug.Assert(resource.Kind == ResourceKind.Sampler);

                        int[] relativeIndices = samplerTrackedRelativeTextureIndices.ToArray();
                        samplerTrackedRelativeTextureIndices.Clear();
                        samplerBindings[i] = new OpenGLSamplerBindingSlotInfo()
                        {
                            RelativeIndices = relativeIndices
                        };
                    }
                }

                _setInfos[setSlot] = new SetBindingsInfo(uniformBindings, textureBindings, samplerBindings, storageBufferBindings);
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

#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
            if (location == -1)
            {
                ReportInvalidUniformName(resourceName);
            }
#endif
            return location;
        }

        uint GetProgramResourceIndex(string resourceName, ProgramInterface resourceType)
        {
            uint binding = _gl.GetProgramResourceIndex(_program, resourceType, resourceName);
            CheckLastError();
#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
            if (binding == GL_INVALID_INDEX)
            {
                ReportInvalidResourceName(resourceName, resourceType);
            }
#endif
            return binding;
        }

#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
        void ReportInvalidUniformName(string uniformName)
        {
            uint uniformIndex = 0;
            var names = new List<string>();
            while (true)
            {
                try
                {
                    _gl.GetActiveUniform(_program, uniformIndex, 64, out int actualLength, out int size, out UniformType type, out string name);

                    if (_gl.GetError() != 0)
                    {
                        break;
                    }

                    names.Add(name);
                    uniformIndex++;
                }
                catch
                {
                    break;
                }
            }

            throw new VeldridException($"Unable to bind uniform \"{uniformName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
        }

        void ReportInvalidResourceName(string resourceName, ProgramInterface resourceType)
        {
            // glGetProgramInterfaceiv and glGetProgramResourceName are only available in 4.3+
            if (_gd.ApiVersion.Major < 4 || (_gd.ApiVersion.Major == 4 && _gd.ApiVersion.Minor < 3))
            {
                return;
            }

            int maxLength = 0;
            int resourceCount = 0;
            _gl.GetProgramInterface(_program, resourceType, ProgramInterfacePName.MaxNameLength, out maxLength);
            _gl.GetProgramInterface(_program, resourceType, ProgramInterfacePName.ActiveResources, out resourceCount);

            var names = new List<string>();
            for (uint resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
            {
                string name = _gl.GetProgramResourceName(_program, resourceType, resourceIndex);

                if (_gl.GetError() != 0)
                {
                    break;
                }

                names.Add(name);
            }

            throw new VeldridException($"Unable to bind {resourceType} \"{resourceName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
        }
#endif

        private void CreateComputeGLResources()
        {
            _program = _gl.CreateProgram();
            CheckLastError();
            OpenGLShader glShader = Util.AssertSubtype<Shader, OpenGLShader>(ComputeShader);
            glShader.EnsureResourcesCreated();
            _gl.AttachShader(_program, glShader.Shader);
            CheckLastError();

            _gl.LinkProgram(_program);
            CheckLastError();

            int linkStatus;
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out linkStatus);
            CheckLastError();
            if (linkStatus != 1)
            {
                string log = _gl.GetProgramInfoLog(_program);
                CheckLastError();
                throw new VeldridException($"Error linking GL program: {log}");
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

        public bool GetSamplerBindingInfo(uint set, uint slot, out OpenGLSamplerBindingSlotInfo binding)
        {
            Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
            SetBindingsInfo setInfo = _setInfos[set];
            return setInfo.GetSamplerBindingInfo(slot, out binding);
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
        private readonly Dictionary<uint, OpenGLSamplerBindingSlotInfo> _samplerBindings;
        private readonly Dictionary<uint, OpenGLShaderStorageBinding> _storageBufferBindings;

        public uint UniformBufferCount { get; }
        public uint ShaderStorageBufferCount { get; }

        public SetBindingsInfo(
            Dictionary<uint, OpenGLUniformBinding> uniformBindings,
            Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings,
            Dictionary<uint, OpenGLSamplerBindingSlotInfo> samplerBindings,
            Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings)
        {
            _uniformBindings = uniformBindings;
            UniformBufferCount = (uint)uniformBindings.Count;
            _textureBindings = textureBindings;
            _samplerBindings = samplerBindings;
            _storageBufferBindings = storageBufferBindings;
            ShaderStorageBufferCount = (uint)storageBufferBindings.Count;
        }

        public bool GetTextureBindingInfo(uint slot, out OpenGLTextureBindingSlotInfo binding)
        {
            return _textureBindings.TryGetValue(slot, out binding);
        }

        public bool GetSamplerBindingInfo(uint slot, out OpenGLSamplerBindingSlotInfo binding)
        {
            return _samplerBindings.TryGetValue(slot, out binding);
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
        /// <summary>
        /// The relative index of this binding with relation to the other textures used by a shader.
        /// Generally, this is the texture unit that the binding will be placed into.
        /// </summary>
        public int RelativeIndex;
        /// <summary>
        /// The uniform location of the binding in the shader program.
        /// </summary>
        public int UniformLocation;
    }

    internal struct OpenGLSamplerBindingSlotInfo
    {
        /// <summary>
        /// The relative indices of this binding with relation to the other textures used by a shader.
        /// Generally, these are the texture units that the sampler will be bound to.
        /// </summary>
        public int[] RelativeIndices;
    }

    internal class OpenGLUniformBinding
    {
        public uint Program { get; }
        public uint BlockLocation { get; }
        public uint BlockSize { get; }

        public OpenGLUniformBinding(uint program, uint blockLocation, uint blockSize)
        {
            Program = program;
            BlockLocation = blockLocation;
            BlockSize = blockSize;
        }
    }

    internal class OpenGLShaderStorageBinding
    {
        public uint StorageBlockBinding { get; }

        public OpenGLShaderStorageBinding(uint storageBlockBinding)
        {
            StorageBlockBinding = storageBlockBinding;
        }
    }
}
