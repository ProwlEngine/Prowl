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

    public bool IsDisposed { get; protected set; }

    public uint Handle { get; private set; }

    public GraphicsProgram(string fragmentSource, string vertexSource, string geometrySource) : base()
    {
        ID = System.Threading.Interlocked.Increment(ref _nextId);

        // Initialize compilation log info variables
        int statusCode = -1;
        string info = string.Empty;

        Handle = Graphics.GL.CreateProgram();

        // Create fragment shader if requested
        if (!string.IsNullOrEmpty(fragmentSource))
        {
            // Create and compile the shader
            uint fragmentShader = Graphics.GL.CreateShader(ShaderType.FragmentShader);
            Graphics.GL.ShaderSource(fragmentShader, fragmentSource);
            Graphics.GL.CompileShader(fragmentShader);

            // Check the compile log
            Graphics.GL.GetShaderInfoLog(fragmentShader, out info);
            Graphics.GL.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                Graphics.GL.DeleteShader(fragmentShader);
                Graphics.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            Graphics.GL.AttachShader(Handle, fragmentShader);
            Graphics.GL.DeleteShader(fragmentShader);
        }

        // Create vertex shader if requested
        if (!string.IsNullOrEmpty(vertexSource))
        {
            // Create and compile the shader
            uint vertexShader = Graphics.GL.CreateShader(ShaderType.VertexShader);
            Graphics.GL.ShaderSource(vertexShader, vertexSource);
            Graphics.GL.CompileShader(vertexShader);

            // Check the compile log
            Graphics.GL.GetShaderInfoLog(vertexShader, out info);
            Graphics.GL.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                Graphics.GL.DeleteShader(vertexShader);
                Graphics.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            Graphics.GL.AttachShader(Handle, vertexShader);
            Graphics.GL.DeleteShader(vertexShader);
        }

        // Create geometry shader if requested
        if (!string.IsNullOrEmpty(geometrySource))
        {
            // Create and compile the shader
            uint geometryShader = Graphics.GL.CreateShader(ShaderType.GeometryShader);
            Graphics.GL.ShaderSource(geometryShader, geometrySource);
            Graphics.GL.CompileShader(geometryShader);

            // Check the compile log
            Graphics.GL.GetShaderInfoLog(geometryShader, out info);
            Graphics.GL.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                Graphics.GL.DeleteShader(geometryShader);
                Graphics.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            Graphics.GL.AttachShader(Handle, geometryShader);
            Graphics.GL.DeleteShader(geometryShader);
        }

        // Link the compiled program
        Graphics.GL.LinkProgram(Handle);

        // Check for link status
        Graphics.GL.GetProgramInfoLog(Handle, out info);
        Graphics.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out statusCode);
        if (statusCode != 1)
        {
            // Delete the handles when failed to link the program
            IsDisposed = true;
            Graphics.GL.DeleteProgram(Handle);

            throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
        }

        // Force an OpenGL flush, so that the shader will appear updated
        // in all contexts immediately (solves problems in multi-threaded apps)
        Graphics.GL.Flush();
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

        if (currentProgram != null && currentProgram.Handle == Handle)
            currentProgram = null;

        Graphics.GL.DeleteProgram(Handle);
        IsDisposed = true;
    }

    public override string ToString()
    {
        return Handle.ToString();
    }
}
