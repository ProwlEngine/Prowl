using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.GraphicsBackend.OpenGL
{
    public class GLProgram : GraphicsProgram
    {
        public override bool IsDisposed { get; protected set; }

        public uint Handle { get; private set; }

        public GLProgram(string fragmentSource, string vertexSource, string geometrySource)
        {
            // Initialize compilation log info variables
            int statusCode = -1;
            string info = string.Empty;

            Handle = GLDevice.GL.CreateProgram();

            // Create fragment shader if requested
            if (!string.IsNullOrEmpty(fragmentSource))
            {
                // Create and compile the shader
                uint fragmentShader = GLDevice.GL.CreateShader(ShaderType.FragmentShader);
                GLDevice.GL.ShaderSource(fragmentShader, fragmentSource);
                GLDevice.GL.CompileShader(fragmentShader);

                // Check the compile log
                GLDevice.GL.GetShaderInfoLog(fragmentShader, out info);
                GLDevice.GL.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    IsDisposed = true;
                    GLDevice.GL.DeleteShader(fragmentShader);
                    GLDevice.GL.DeleteProgram(Handle);

                    throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                GLDevice.GL.AttachShader(Handle, fragmentShader);
                GLDevice.GL.DeleteShader(fragmentShader);
            }

            // Create vertex shader if requested
            if (!string.IsNullOrEmpty(vertexSource))
            {
                // Create and compile the shader
                uint vertexShader = GLDevice.GL.CreateShader(ShaderType.VertexShader);
                GLDevice.GL.ShaderSource(vertexShader, vertexSource);
                GLDevice.GL.CompileShader(vertexShader);

                // Check the compile log
                GLDevice.GL.GetShaderInfoLog(vertexShader, out info);
                GLDevice.GL.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    IsDisposed = true;
                    GLDevice.GL.DeleteShader(vertexShader);
                    GLDevice.GL.DeleteProgram(Handle);

                    throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                GLDevice.GL.AttachShader(Handle, vertexShader);
                GLDevice.GL.DeleteShader(vertexShader);
            }

            // Create geometry shader if requested
            if (!string.IsNullOrEmpty(geometrySource))
            {
                // Create and compile the shader
                uint geometryShader = GLDevice.GL.CreateShader(ShaderType.GeometryShader);
                GLDevice.GL.ShaderSource(geometryShader, geometrySource);
                GLDevice.GL.CompileShader(geometryShader);

                // Check the compile log
                GLDevice.GL.GetShaderInfoLog(geometryShader, out info);
                GLDevice.GL.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    IsDisposed = true;
                    GLDevice.GL.DeleteShader(geometryShader);
                    GLDevice.GL.DeleteProgram(Handle);

                    throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                GLDevice.GL.AttachShader(Handle, geometryShader);
                GLDevice.GL.DeleteShader(geometryShader);
            }

            // Link the compiled program
            GLDevice.GL.LinkProgram(Handle);

            // Check for link status
            GLDevice.GL.GetProgramInfoLog(Handle, out info);
            GLDevice.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out statusCode);
            if (statusCode != 1)
            {
                // Delete the handles when failed to link the program
                IsDisposed = true;
                GLDevice.GL.DeleteProgram(Handle);

                throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
            }

            // Force an OpenGL flush, so that the shader will appear updated
            // in all contexts immediately (solves problems in multi-threaded apps)
            GLDevice.GL.Flush();
        }

        public static GLProgram? currentProgram = null;
        public void Use()
        {
            if (currentProgram != null && currentProgram.Handle == Handle)
                return;

            GLDevice.GL.UseProgram(Handle);
            currentProgram = this;
        }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            if (currentProgram != null && currentProgram.Handle == Handle)
                currentProgram = null;

            GLDevice.GL.DeleteProgram(Handle);
            IsDisposed = true;
        }

        public override string ToString()
        {
            return Handle.ToString();
        }
    }
}