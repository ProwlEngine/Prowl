// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

public class GraphicsProgram : IDisposable
{
    private static int _nextId = 0;

    public int ID { get; }

    // Uniform cache - tracks what values are currently set in this shader program
    internal class UniformCache
    {
        public Dictionary<string, float> floats = [];
        public Dictionary<string, int> ints = [];
        public Dictionary<string, Float2> vectors2 = [];
        public Dictionary<string, Float3> vectors3 = [];
        public Dictionary<string, Float4> vectors4 = [];
        public Dictionary<string, Float4x4> matrices = [];
        public Dictionary<string, GraphicsBuffer> buffers = [];

        public void Clear()
        {
            floats.Clear();
            ints.Clear();
            vectors2.Clear();
            vectors3.Clear();
            vectors4.Clear();
            matrices.Clear();
            buffers.Clear();
        }
    }

    internal UniformCache uniformCache = new();

    // Per-program lookup caches walked by PropertyApply.
    internal readonly Dictionary<string, int> uniformLocations = [];
    internal readonly Dictionary<string, uint> blockIndices = [];

    // Every sampler the program declares, with the empty unit it falls back to when a draw binds
    // nothing to it, and the draw that last bound it.
    internal (int Location, int EmptyUnit)[] samplers = [];
    internal int[] samplerBoundDraw = [];
    internal readonly Dictionary<int, int> samplerIndexByLocation = [];

    // Units kept free of textures, one per sampler type, since two sampler types on one unit is an error.
    internal const int FirstEmptyUnit = 40;

    public bool IsDisposed { get; protected set; }

    public uint Handle { get; internal set; }

    // Held only until CreateGLObject runs on the render thread, then nulled.
    private string? _fragmentSource;
    private string? _vertexSource;
    private string? _geometrySource;

    public GraphicsProgram(string fragmentSource, string vertexSource, string geometrySource) : base()
    {
        ID = System.Threading.Interlocked.Increment(ref _nextId);
        _fragmentSource = fragmentSource;
        _vertexSource = vertexSource;
        _geometrySource = geometrySource;
        Handle = 0;

        // SubmitAndWait so compile / link errors surface synchronously to
        // ShaderPass.TryGetVariantProgram (it catches and falls back).
        using var cmd = Graphics.GetCommandBuffer("GraphicsProgram.Compile");
        cmd.EncodeCompileShader(this);
        Graphics.SubmitAndWait(cmd);
    }

    /// <summary>Invoked by the CompileShader executor handler on the render thread.
    /// Releases the source strings on success.</summary>
    internal void CreateGLObject()
    {
        int statusCode = -1;
        string info = string.Empty;

        Handle = Graphics.GL.CreateProgram();

        if (!string.IsNullOrEmpty(_fragmentSource))
        {
            uint fragmentShader = Graphics.GL.CreateShader(ShaderType.FragmentShader);
            Graphics.GL.ShaderSource(fragmentShader, _fragmentSource);
            Graphics.GL.CompileShader(fragmentShader);

            Graphics.GL.GetShaderInfoLog(fragmentShader, out info);
            Graphics.GL.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

            if (statusCode != 1)
            {
                IsDisposed = true;
                Graphics.GL.DeleteShader(fragmentShader);
                Graphics.GL.DeleteProgram(Handle);
                Handle = 0;
                throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                    info + "\n\nStatus Code: " + statusCode.ToString());
            }

            Graphics.GL.AttachShader(Handle, fragmentShader);
            Graphics.GL.DeleteShader(fragmentShader);
        }

        if (!string.IsNullOrEmpty(_vertexSource))
        {
            uint vertexShader = Graphics.GL.CreateShader(ShaderType.VertexShader);
            Graphics.GL.ShaderSource(vertexShader, _vertexSource);
            Graphics.GL.CompileShader(vertexShader);

            Graphics.GL.GetShaderInfoLog(vertexShader, out info);
            Graphics.GL.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

            if (statusCode != 1)
            {
                IsDisposed = true;
                Graphics.GL.DeleteShader(vertexShader);
                Graphics.GL.DeleteProgram(Handle);
                Handle = 0;
                throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                    info + "\n\nStatus Code: " + statusCode.ToString());
            }

            Graphics.GL.AttachShader(Handle, vertexShader);
            Graphics.GL.DeleteShader(vertexShader);
        }

        if (!string.IsNullOrEmpty(_geometrySource))
        {
            uint geometryShader = Graphics.GL.CreateShader(ShaderType.GeometryShader);
            Graphics.GL.ShaderSource(geometryShader, _geometrySource);
            Graphics.GL.CompileShader(geometryShader);

            Graphics.GL.GetShaderInfoLog(geometryShader, out info);
            Graphics.GL.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

            if (statusCode != 1)
            {
                IsDisposed = true;
                Graphics.GL.DeleteShader(geometryShader);
                Graphics.GL.DeleteProgram(Handle);
                Handle = 0;
                throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                    info + "\n\nStatus Code: " + statusCode.ToString());
            }

            Graphics.GL.AttachShader(Handle, geometryShader);
            Graphics.GL.DeleteShader(geometryShader);
        }

        Graphics.GL.LinkProgram(Handle);
        Graphics.GL.GetProgramInfoLog(Handle, out info);
        Graphics.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out statusCode);
        if (statusCode != 1)
        {
            IsDisposed = true;
            Graphics.GL.DeleteProgram(Handle);
            Handle = 0;
            throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                    info + "\n\nStatus Code: " + statusCode.ToString());
        }

        FindSamplers();

        Graphics.GL.Flush();

        // Release sources we won't recompile.
        _fragmentSource = null;
        _vertexSource = null;
        _geometrySource = null;
    }

    private void FindSamplers()
    {
        Graphics.GL.GetProgram(Handle, ProgramPropertyARB.ActiveUniforms, out int count);
        var found = new List<(int Location, int EmptyUnit)>();
        for (uint i = 0; i < count; i++)
        {
            string name = Graphics.GL.GetActiveUniform(Handle, i, out int size, out UniformType type);
            int emptyUnit = type switch
            {
                UniformType.Sampler2D or UniformType.IntSampler2D or UniformType.UnsignedIntSampler2D => FirstEmptyUnit,
                UniformType.Sampler2DShadow => FirstEmptyUnit + 1,
                UniformType.Sampler3D => FirstEmptyUnit + 2,
                UniformType.SamplerCube => FirstEmptyUnit + 3,
                UniformType.Sampler2DArray => FirstEmptyUnit + 4,
                _ => -1,
            };
            if (emptyUnit < 0) continue;

            // Arrays report their first element as "name[0]", the rest are looked up one by one.
            string baseName = name.EndsWith("[0]") ? name[..^3] : name;
            for (int element = 0; element < size; element++)
            {
                int location = Graphics.GL.GetUniformLocation(Handle, size > 1 ? $"{baseName}[{element}]" : name);
                if (location < 0) continue;
                samplerIndexByLocation[location] = found.Count;
                found.Add((location, emptyUnit));
            }
        }

        samplers = [.. found];
        samplerBoundDraw = new int[samplers.Length];
    }

    public static GraphicsProgram? currentProgram = null;
    public void Use()
    {
        if (currentProgram != null && currentProgram.Handle == Handle)
            return;

        Graphics.GL.UseProgram(Handle);
        currentProgram = this;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;

        using var cmd = Graphics.GetCommandBuffer("GraphicsProgram.Dispose");
        cmd.EncodeDisposeShader(this);
        Graphics.Submit(cmd);
    }

    public override string ToString()
    {
        return Handle.ToString();
    }
}
