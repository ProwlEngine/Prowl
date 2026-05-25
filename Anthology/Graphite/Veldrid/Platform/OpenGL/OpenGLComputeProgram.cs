using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

using System.Diagnostics;
using System.Text;
using System;

namespace Prowl.Veldrid.OpenGL;

internal unsafe class OpenGLComputeProgram : ComputeProgram, OpenGLDeferredResource
{
    private readonly OpenGLGraphicsDevice _gd;
    private GL _gl => _gd.GL;

    private readonly ShaderStageDescription _stageDescription;
    private StagingBlock _stagingBlock;
    private uint _shader;
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

    public OpenGLComputeProgram(OpenGLGraphicsDevice gd, ref ComputeDescription description)
        : base(ref description)
    {
        _gd = gd;
        _stageDescription = description.Stage;
        _stagingBlock = gd.StagingMemoryPool.Stage(_stageDescription.ShaderBytes);
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
        ShaderType shaderType = OpenGLFormats.VdToGLShaderType(_stageDescription.Stage);
        _shader = _gl.CreateShader(shaderType);
        CheckLastError();

        byte* textPtr = (byte*)_stagingBlock.Data;
        int length = (int)_stagingBlock.SizeInBytes;
        string source = Encoding.UTF8.GetString(textPtr, length);

        _gl.ShaderSource(_shader, source);
        CheckLastError();
        _gl.CompileShader(_shader);
        CheckLastError();

        _gl.GetShader(_shader, ShaderParameterName.CompileStatus, out int compileStatus);
        CheckLastError();
        if (compileStatus != 1)
        {
            string message = _gl.GetShaderInfoLog(_shader);
            CheckLastError();
            if (string.IsNullOrEmpty(message))
            {
                message = "<null>";
            }
            throw new RenderException($"Unable to compile compute shader [{_name}]: {message}");
        }

        _gd.StagingMemoryPool.Free(_stagingBlock);

        _program = _gl.CreateProgram();
        CheckLastError();
        _gl.AttachShader(_program, _shader);
        CheckLastError();
        _gl.LinkProgram(_program);
        CheckLastError();

        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        CheckLastError();
        if (linkStatus != 1)
        {
            string log = _gl.GetProgramInfoLog(_program);
            CheckLastError();
            throw new RenderException($"Error linking GL compute program: {log}");
        }

        _setInfos = OpenGLPipeline.BuildSetBindingsInfo(_gl, _program, ResourceLayoutsArray, _gd.BackendType);
        _created = true;
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
        }
    }

    public void DestroyGLResources()
    {
        if (_disposed) return;
        _disposed = true;
        if (_created)
        {
            _gl.DeleteShader(_shader);
            CheckLastError();
            _gl.DeleteProgram(_program);
            CheckLastError();
        }
        else
        {
            _gd.StagingMemoryPool.Free(_stagingBlock);
        }
    }
}
