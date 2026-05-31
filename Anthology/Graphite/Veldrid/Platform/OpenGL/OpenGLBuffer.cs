using System;
using System.Diagnostics;

using Silk.NET.OpenGL;

using static Prowl.Veldrid.OpenGL.OpenGLUtil;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLBuffer : DeviceBuffer, OpenGLDeferredResource
{
    private readonly OpenGLGraphicsDevice _gd;
    private GL _gl => _gd.GL;
    private uint _buffer;
    private bool _dynamic;
    private bool _disposeRequested;

    private string _name;
    public override string Name { get; set; }

    public override uint SizeInBytes { get; }
    public override BufferUsage Usage { get; }

    public uint Buffer => _buffer;

    public bool Created { get; private set; }

    public override bool IsDisposed => _disposeRequested;


    public OpenGLBuffer(OpenGLGraphicsDevice gd, uint sizeInBytes, BufferUsage usage)
    {
        _gd = gd;
        _name = "";
        _dynamic = (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

        Name = _name;
        SizeInBytes = sizeInBytes;
        Usage = usage;

        _gd.RecordBufferAllocation(Usage, SizeInBytes);
    }


    public void EnsureResourcesCreated()
    {
        if (!Created)
            CreateGLResources();

        if (_name != Name)
        {
            _name = Name;

            if (_gd.Extensions.KHR_Debug)
                SetObjectLabel(ObjectIdentifier.Buffer, _buffer, _name);
        }
    }

    public void CreateGLResources()
    {
        Debug.Assert(!Created);

        if (_gd.Extensions.ARB_DirectStateAccess)
        {
            uint buffer;
            _gl.CreateBuffers(1, &buffer);
            CheckLastError();
            _buffer = buffer;

            _gl.NamedBufferData(
                _buffer,
                SizeInBytes,
                null,
                _dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw);
            CheckLastError();
        }
        else
        {
            _buffer = _gl.GenBuffer();
            CheckLastError();

            _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _buffer);
            CheckLastError();

            _gl.BufferData(
                BufferTargetARB.CopyReadBuffer,
                (UIntPtr)SizeInBytes,
                null,
                _dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw);
            CheckLastError();
        }

        Created = true;
    }

    public override void Dispose()
    {
        if (!_disposeRequested)
        {
            _disposeRequested = true;
            _gd.EnqueueDisposal(this);
            _gd.RecordBufferFree(Usage, SizeInBytes);
        }
    }

    public void DestroyGLResources()
    {
        _gl.DeleteBuffer(_buffer);
        CheckLastError();
    }
}
