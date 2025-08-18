// TextureSilk.cs - Simplified
using Silk.NET.OpenGL;
using Prowl.Vector;
using StbImageSharp;

namespace SilkExample
{
    public class TextureSilk : IDisposable
    {
        private readonly GL _gl;
        public readonly uint Handle;
        public uint Width { get; }
        public uint Height { get; }

        private TextureSilk(GL gl, uint handle, uint width, uint height)
        {
            _gl = gl;
            Handle = handle;
            Width = width;
            Height = height;
        }

        public static TextureSilk LoadFromFile(GL gl, string path)
        {
            uint handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, handle);

            // Configure image loading
            StbImage.stbi_set_flip_vertically_on_load(1);

            // Load image data
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Upload image data to GPU
            unsafe
            {
                fixed (byte* pixelPtr = image.Data)
                {
                    gl.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        InternalFormat.Rgba,
                        (uint)image.Width,
                        (uint)image.Height,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixelPtr
                    );
                }
            }

            // Configure texture settings
            SetTextureParameters(gl);

            return new TextureSilk(gl, handle, (uint)image.Width, (uint)image.Height);
        }

        public static TextureSilk CreateNew(GL gl, uint width, uint height)
        {
            uint handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, handle);
            
            // Create empty texture
            unsafe
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    width,
                    height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    null
                );
            }
            
            // Configure texture settings
            SetTextureParameters(gl);
            
            return new TextureSilk(gl, handle, width, height);
        }

        private static void SetTextureParameters(GL gl)
        {
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.GenerateMipmap(TextureTarget.Texture2D);
        }

        public unsafe void SetData(IntRect bounds, byte[] data)
        {
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            
            fixed (byte* pixelPtr = data)
            {
                _gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    bounds.x,
                    bounds.y,
                    (uint)bounds.width,
                    (uint)bounds.height,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixelPtr
                );
            }
        }

        public void Use(TextureUnit unit = TextureUnit.Texture0)
        {
            _gl.ActiveTexture(unit);
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
        }

        public void Dispose()
        {
            _gl.DeleteTexture(Handle);
        }
    }
}