using Prowl.PaperUI.Graphics;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTKSample
{
    public class BufferObject<T> : IDisposable where T : unmanaged
    {
        private readonly int _handle;
        private readonly BufferTarget _bufferType;
        private readonly int _size;

        public int Size => _size;

        public unsafe BufferObject(int size, BufferTarget bufferType, bool isDynamic)
        {
            _bufferType = bufferType;
            _size = size;

            _handle = GL.GenBuffer();
            GLUtility.CheckError();

            Bind();

            var elementSizeInBytes = Marshal.SizeOf<T>();
            GL.BufferData(bufferType, size * elementSizeInBytes, nint.Zero, isDynamic ? BufferUsage.StreamDraw : BufferUsage.StaticDraw);
            GLUtility.CheckError();
        }

        public void Bind()
        {
            GL.BindBuffer(_bufferType, _handle);
            GLUtility.CheckError();
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_handle);
            GLUtility.CheckError();
        }

        public unsafe void SetData(T[] data, int startIndex, int elementCount)
        {
            Bind();

            fixed (T* dataPtr = &data[startIndex])
            {
                var elementSizeInBytes = sizeof(T);

                GL.BufferSubData(_bufferType, nint.Zero, elementCount * elementSizeInBytes, new nint(dataPtr));
                GLUtility.CheckError();
            }
        }
    }


    public class Shader : IDisposable
    {
        private int _handle;

        private Dictionary<string, int> uniformCache = new Dictionary<string, int>();

        public Shader(string vertexPath, string fragmentPath, Dictionary<string, string> defines)
        {
            int vertex = LoadShader(ShaderType.VertexShader, vertexPath, defines);
            int fragment = LoadShader(ShaderType.FragmentShader, fragmentPath, defines);
            _handle = GL.CreateProgram();
            GLUtility.CheckError();

            GL.AttachShader(_handle, vertex);
            GLUtility.CheckError();

            GL.AttachShader(_handle, fragment);
            GLUtility.CheckError();

            GL.LinkProgram(_handle);
            GL.GetProgrami(_handle, ProgramProperty.LinkStatus, out var status);
            if (status == 0)
            {
                GL.GetProgramInfoLog(_handle, out var err);
                throw new Exception($"Program failed to link with error: {err}");
            }

            GL.DetachShader(_handle, vertex);
            GL.DetachShader(_handle, fragment);

            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
        }

        public void Use()
        {
            GL.UseProgram(_handle);
            GLUtility.CheckError();
        }

        public int GetUniformLoc(string name)
        {
            if (uniformCache.TryGetValue(name, out var location))
            {
                return location;
            }
            location = GL.GetUniformLocation(_handle, name);
            GLUtility.CheckError();
            if (location == -1)
            {
                throw new Exception($"{name} uniform not found on shader.");
            }
            uniformCache[name] = location;
            return location;
        }

        public void SetUniform(int location, int value)
        {
            GL.Uniform1i(location, value);
            GLUtility.CheckError();
        }

        public void SetUniform(int location, float value)
        {
            GL.Uniform1f(location, value);
            GLUtility.CheckError();
        }

        public void SetUniform(int location, Vector2 value)
        {
            GL.Uniform2f(location, value.X, value.Y);
            GLUtility.CheckError();
        }

        public void SetUniform(int location, Vector4 value)
        {
            GL.Uniform4f(location, value.X, value.Y, value.Z, value.W);
            GLUtility.CheckError();
        }

        public unsafe void SetUniform(int location, Matrix4x4 value)
        {
            GL.UniformMatrix4f(location, 1, false, in value);
            GLUtility.CheckError();
        }

        public void Dispose()
        {
            GL.DeleteProgram(_handle);
        }

        private int LoadShader(ShaderType type, string path, Dictionary<string, string> defines)
        {
            var sb = new StringBuilder();

            if (defines != null)
            {
                foreach (var pair in defines)
                {
                    sb.Append("#define " + pair.Key + " " + pair.Value + "\n");
                }
            }

            string src = File.ReadAllText(path);
            sb.Append(src);

            int handle = GL.CreateShader(type);
            GLUtility.CheckError();

            GL.ShaderSource(handle, sb.ToString());
            GLUtility.CheckError();

            GL.CompileShader(handle);
            GL.GetShaderInfoLog(handle, out var infoLog);
            if (!string.IsNullOrWhiteSpace(infoLog))
            {
                throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
            }

            return handle;
        }

        public int GetAttribLocation(string attribName)
        {
            var result = GL.GetAttribLocation(_handle, attribName);
            GLUtility.CheckError();
            return result;
        }
    }


    public unsafe class Texture : IDisposable
    {
        private readonly int _handle;

        public readonly int Width;
        public readonly int Height;

        public Texture(int width, int height)
        {
            Width = width;
            Height = height;

            _handle = GL.GenTexture();
            GLUtility.CheckError();
            Bind();

            //Reserve enough memory from the gpu for the whole image
            GL.TexImage2D(TextureTarget.Texture2d, 0, InternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            GLUtility.CheckError();

            SetParameters();
        }

        private void SetParameters()
        {
            //Setting some texture perameters so the texture behaves as expected.
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GLUtility.CheckError();

            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GLUtility.CheckError();

            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GLUtility.CheckError();

            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GLUtility.CheckError();

            //Generating mipmaps.
            GL.GenerateMipmap(TextureTarget.Texture2d);
            GLUtility.CheckError();
        }

        public void Bind(TextureUnit textureSlot = TextureUnit.Texture0)
        {
            //When we bind a texture we can choose which textureslot we can bind it to.
            GL.ActiveTexture(textureSlot);
            GLUtility.CheckError();

            GL.BindTexture(TextureTarget.Texture2d, _handle);
            GLUtility.CheckError();
        }

        public void Dispose()
        {
            //In order to dispose we need to delete the opengl handle for the texure.
            GL.DeleteTexture(_handle);
            GLUtility.CheckError();
        }

        public void SetData(Rectangle bounds, byte[] data)
        {
            Bind();
            fixed (byte* ptr = data)
            {
                GL.TexSubImage2D(
                    target: TextureTarget.Texture2d,
                    level: 0,
                    xoffset: bounds.Left,
                    yoffset: bounds.Top,
                    width: bounds.Width,
                    height: bounds.Height,
                    format: PixelFormat.Rgba,
                    type: PixelType.UnsignedByte,
                    pixels: new nint(ptr)
                );
                GLUtility.CheckError();
            }
        }
    }
    public class VertexArrayObject : IDisposable
    {
        private int _handle;
        private readonly int _stride;

        public VertexArrayObject(int stride)
        {
            if (stride <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stride));
            }

            _stride = stride;

            GL.GenVertexArrays(1, ref _handle);
            GLUtility.CheckError();
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(_handle);
            GLUtility.CheckError();
        }

        public void Bind()
        {
            GL.BindVertexArray(_handle);
            GLUtility.CheckError();
        }

        public unsafe void VertexAttribPointer(uint location, int size, VertexAttribPointerType type, bool normalized, int offset)
        {
            GL.EnableVertexAttribArray(location);
            GLUtility.CheckError();
            GL.VertexAttribPointer(location, size, type, normalized, _stride, new nint(offset));
            GLUtility.CheckError();
        }
    }

    internal static class GLUtility
    {
        public static void CheckError()
        {
            var error = GL.GetError();
            if (error != ErrorCode.NoError)
                throw new Exception("GL.GetError() returned " + error.ToString());
        }

        public static void DrawStroke(this FillStrokeInfo fillStrokeInfo, PrimitiveType primitiveType)
        {
            if (fillStrokeInfo.StrokeCount <= 0)
            {
                return;
            }

            GL.DrawArrays(primitiveType, fillStrokeInfo.StrokeOffset, fillStrokeInfo.StrokeCount);
            CheckError();
        }

        public static void DrawFill(this FillStrokeInfo fillStrokeInfo, PrimitiveType primitiveType)
        {
            if (fillStrokeInfo.FillCount <= 0)
            {
                return;
            }

            GL.DrawArrays(primitiveType, fillStrokeInfo.FillOffset, fillStrokeInfo.FillCount);
            CheckError();
        }

        public static void DrawTriangles(this CallInfo callInfo, PrimitiveType primitiveType)
        {
            if (callInfo.TriangleCount <= 0)
            {
                return;
            }

            GL.DrawArrays(primitiveType, callInfo.TriangleOffset, callInfo.TriangleCount);
            CheckError();
        }
    }
}
