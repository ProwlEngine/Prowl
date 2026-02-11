// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.GraphicsBackend;

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

        Handle = GraphicsDevice.GL.CreateProgram();

        // Create fragment shader if requested
        if (!string.IsNullOrEmpty(fragmentSource))
        {
            // Create and compile the shader
            uint fragmentShader = GraphicsDevice.GL.CreateShader(ShaderType.FragmentShader);
            GraphicsDevice.GL.ShaderSource(fragmentShader, fragmentSource);
            GraphicsDevice.GL.CompileShader(fragmentShader);

            // Check the compile log
            GraphicsDevice.GL.GetShaderInfoLog(fragmentShader, out info);
            GraphicsDevice.GL.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                GraphicsDevice.GL.DeleteShader(fragmentShader);
                GraphicsDevice.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            GraphicsDevice.GL.AttachShader(Handle, fragmentShader);
            GraphicsDevice.GL.DeleteShader(fragmentShader);
        }

        // Create vertex shader if requested
        if (!string.IsNullOrEmpty(vertexSource))
        {
            // Create and compile the shader
            uint vertexShader = GraphicsDevice.GL.CreateShader(ShaderType.VertexShader);
            GraphicsDevice.GL.ShaderSource(vertexShader, vertexSource);
            GraphicsDevice.GL.CompileShader(vertexShader);

            // Check the compile log
            GraphicsDevice.GL.GetShaderInfoLog(vertexShader, out info);
            GraphicsDevice.GL.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                GraphicsDevice.GL.DeleteShader(vertexShader);
                GraphicsDevice.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            GraphicsDevice.GL.AttachShader(Handle, vertexShader);
            GraphicsDevice.GL.DeleteShader(vertexShader);
        }

        // Create geometry shader if requested
        if (!string.IsNullOrEmpty(geometrySource))
        {
            // Create and compile the shader
            uint geometryShader = GraphicsDevice.GL.CreateShader(ShaderType.GeometryShader);
            GraphicsDevice.GL.ShaderSource(geometryShader, geometrySource);
            GraphicsDevice.GL.CompileShader(geometryShader);

            // Check the compile log
            GraphicsDevice.GL.GetShaderInfoLog(geometryShader, out info);
            GraphicsDevice.GL.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

            // Check the compile log
            if (statusCode != 1)
            {
                // Delete every handles when compilation failed
                IsDisposed = true;
                GraphicsDevice.GL.DeleteShader(geometryShader);
                GraphicsDevice.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
            }

            // Attach the shader to the program, and delete it (not needed anymore)
            GraphicsDevice.GL.AttachShader(Handle, geometryShader);
            GraphicsDevice.GL.DeleteShader(geometryShader);
        }

        // Link the compiled program
        GraphicsDevice.GL.LinkProgram(Handle);

        // Check for link status
        GraphicsDevice.GL.GetProgramInfoLog(Handle, out info);
        GraphicsDevice.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out statusCode);
        if (statusCode != 1)
        {
            // Delete the handles when failed to link the program
            IsDisposed = true;
            GraphicsDevice.GL.DeleteProgram(Handle);

            throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                    info + "\n\n" +
                    "Status Code: " + statusCode.ToString());
        }

        // Force an OpenGL flush, so that the shader will appear updated
        // in all contexts immediately (solves problems in multi-threaded apps)
        GraphicsDevice.GL.Flush();
    }

    public static GraphicsProgram? currentProgram = null;
    public void Use()
    {
        if (currentProgram != null && currentProgram.Handle == Handle)
            return;

        GraphicsDevice.GL.UseProgram(Handle);
        currentProgram = this;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        if (currentProgram != null && currentProgram.Handle == Handle)
            currentProgram = null;

        GraphicsDevice.GL.DeleteProgram(Handle);
        IsDisposed = true;
    }

    public override string ToString()
    {
        return Handle.ToString();
    }
}
