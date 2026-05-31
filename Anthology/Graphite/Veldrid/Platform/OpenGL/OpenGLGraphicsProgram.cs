using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLGraphicsProgram : GraphicsProgram, OpenGLDeferredResource
{
    private const uint GL_INVALID_INDEX = 0xFFFFFFFF;

    private readonly OpenGLGraphicsDevice _gd;
    private GL _gl => _gd.GL;

    private readonly ShaderStageDescription[] _stageDescriptions;
    private readonly StagingBlock[] _stagingBlocks;
    private uint[] _shaderObjects;
    private SetBindingsInfo[] _setInfos;

    private uint _program;
    private bool _disposeRequested;
    private bool _disposed;
    private bool _created;
    private string _name;
    private bool _nameChanged;

    public override string Name { get => _name; set { _name = value; _nameChanged = true; } }
    public override bool IsDisposed => _disposeRequested;

    public uint GLProgram => _program;

    public int[] VertexStrides { get; }

    public OpenGLGraphicsProgram(OpenGLGraphicsDevice gd, ref ShaderDescription description)
        : base(ref description)
    {
        _gd = gd;
        _stageDescriptions = Util.ShallowClone(description.Stages) ?? Array.Empty<ShaderStageDescription>();
        _stagingBlocks = new StagingBlock[_stageDescriptions.Length];
        for (int i = 0; i < _stageDescriptions.Length; i++)
        {
            _stagingBlocks[i] = gd.StagingMemoryPool.Stage(_stageDescriptions[i].ShaderBytes);
        }

        int numVertexBuffers = VertexLayoutsArray.Length;
        VertexStrides = new int[numVertexBuffers];
        for (int i = 0; i < numVertexBuffers; i++)
        {
            VertexStrides[i] = (int)VertexLayoutsArray[i].Stride;
        }

        Constructor_RecordShaderAllocation(_stageDescriptions);
    }

    public bool Created => _created;

    public void EnsureResourcesCreated()
    {
        if (!_created)
        {
            CreateGLResources();
        }
        if (_nameChanged)
        {
            _nameChanged = false;
            if (_gd.Extensions.KHR_Debug)
            {
                SetObjectLabel(ObjectIdentifier.Program, _program, _name);
            }
        }
    }

    private void CreateGLResources()
    {
        _program = _gl.CreateProgram();
        CheckLastError();

        _shaderObjects = new uint[_stageDescriptions.Length];
        for (int i = 0; i < _stageDescriptions.Length; i++)
        {
            _shaderObjects[i] = CompileStage(i);
            _gl.AttachShader(_program, _shaderObjects[i]);
            CheckLastError();
        }

        OpenGLCachedPipeline.BindVertexAttribLocations(_gl, _program, VertexLayoutsArray);

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

        _setInfos = OpenGLCachedPipeline.BuildSetBindingsInfo(_gl, _program, ResourceLayoutsArray, _gd.BackendType);

        _created = true;
    }

    private uint CompileStage(int index)
    {
        ShaderType shaderType = OpenGLFormats.VdToGLShaderType(_stageDescriptions[index].Stage);
        uint shader = _gl.CreateShader(shaderType);
        CheckLastError();

        StagingBlock block = _stagingBlocks[index];
        byte* textPtr = (byte*)block.Data;
        int length = (int)block.SizeInBytes;
        string source = Encoding.UTF8.GetString(textPtr, length);

        _gl.ShaderSource(shader, source);
        CheckLastError();

        _gl.CompileShader(shader);
        CheckLastError();

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        CheckLastError();
        if (compileStatus != 1)
        {
            string message = _gl.GetShaderInfoLog(shader);
            CheckLastError();
            if (string.IsNullOrEmpty(message))
            {
                message = "<null>";
            }
            throw new RenderException($"Unable to compile shader [{_name}] of type {shaderType}: {message}");
        }

        _gd.StagingMemoryPool.Free(block);
        return shader;
    }

    public bool GetUniformBindingForSlot(uint set, uint slot, out OpenGLUniformBinding binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        return _setInfos[set].GetUniformBindingForSlot(slot, out binding);
    }

    public bool GetTextureBindingInfo(uint set, uint slot, out OpenGLTextureBindingSlotInfo binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        return _setInfos[set].GetTextureBindingInfo(slot, out binding);
    }

    public bool GetStorageBufferBindingForSlot(uint set, uint slot, out OpenGLShaderStorageBinding binding)
    {
        Debug.Assert(_setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
        return _setInfos[set].GetStorageBufferBindingForSlot(slot, out binding);
    }

    public override void Dispose()
    {
        if (!_disposeRequested)
        {
            _disposeRequested = true;
            _gd.EnqueueDisposal(this);
            Dispose_RecordShaderFree();
        }
    }

    public void DestroyGLResources()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_created)
        {
            if (_shaderObjects != null)
            {
                for (int i = 0; i < _shaderObjects.Length; i++)
                {
                    _gl.DeleteShader(_shaderObjects[i]);
                    CheckLastError();
                }
            }
            _gl.DeleteProgram(_program);
            CheckLastError();
        }
        else if (_stagingBlocks != null)
        {
            for (int i = 0; i < _stagingBlocks.Length; i++)
            {
                _gd.StagingMemoryPool.Free(_stagingBlocks[i]);
            }
        }
    }
}
