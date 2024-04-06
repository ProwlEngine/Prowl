using Silk.NET.OpenGL;
using System.Reflection.Metadata;
using System;
using System.Numerics;

namespace Prowl.Runtime;

public class GBuffer
{
    internal RenderTexture buffer;

    public uint fbo { get { return buffer.fboId; } }
    public int Width { get { return buffer.Width; } }
    public int Height { get { return buffer.Height; } }
    public Texture2D AlbedoAO { get { return buffer.InternalTextures[0]; } }
    public Texture2D NormalMetallic { get { return buffer.InternalTextures[1]; } }
    public Texture2D PositionRoughness { get { return buffer.InternalTextures[2]; } }
    public Texture2D Emission { get { return buffer.InternalTextures[3]; } }
    public Texture2D Velocity { get { return buffer.InternalTextures[4]; } }
    public Texture2D ObjectIDs { get { return buffer.InternalTextures[5]; } }
    public Texture2D Depth { get { return buffer.InternalDepth; } }
    
    public GBuffer(int width, int height)
    {
#warning TODO: Dont always use 32bits, optomize this and use only whats absolutely needed, some precision loss is ok as long as it doesnt hurt visuals much, normals for example could probably be 16
#warning TODO: Switch to a singular 16bit "Material" buffer, AO, Rough, Metal, the final channel would be 16 bools, Lit, Fog, etc
        Texture.TextureImageFormat[] formats =
        [
            Texture.TextureImageFormat.Float4, // Albedo & AO
            Texture.TextureImageFormat.Float4, // Normal & Metalness
            Texture.TextureImageFormat.Float4, // Position & Roughness
            Texture.TextureImageFormat.Float3, // Emission
            Texture.TextureImageFormat.Float2, // Velocity
            Texture.TextureImageFormat.Float, // ObjectIDs
        ];
        buffer = new RenderTexture(width, height, 6, true, formats);
    }

    public void Begin(bool clear = true)
    {
        Graphics.Device.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, fbo);
        Graphics.ActivateDrawBuffers(6);
        Graphics.Device.Disable(EnableCap.Blend);

        Graphics.Viewport(Width, Height);

        // Start with the initial GBuffer Clear
        if (clear)
            Graphics.Clear(0,0,0,0);
    }

    public void End()
    {
        Graphics.Device.Enable(EnableCap.Blend);
        Graphics.Device.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, 0);
    }

    public int GetObjectIDAt(Vector2 uv)
    {
        int x = (int)(uv.x * Width);
        int y = (int)(uv.y * Height);
        Graphics.Device.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, fbo);
        Graphics.Device.ReadBuffer(ReadBufferMode.ColorAttachment5);
        float result = Graphics.Device.ReadPixels<float>(x, y, 1, 1, PixelFormat.Red, PixelType.Float);
        return (int)result;
    }

    public Vector3 GetViewPositionAt(Vector2 uv)
    {
        int x = (int)(uv.x * Width);
        int y = (int)(uv.y * Height);
        Graphics.Device.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, fbo);
        Graphics.Device.ReadBuffer(ReadBufferMode.ColorAttachment2);
        float[] result = new float[4];
        unsafe {
            fixed (float* ptr = result) {
                Graphics.Device.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.Float, ptr);
            }
        }
        return new Vector3(result[0], result[1], result[2]);
    }

    public void UnloadGBuffer()
    {
        if (fbo <= 0) return;
        AlbedoAO.Dispose();
        NormalMetallic.Dispose();
        PositionRoughness.Dispose();
        Emission.Dispose();
        Velocity.Dispose();
        ObjectIDs.Dispose();
        Depth.Dispose();
        Graphics.Device.DeleteFramebuffer(fbo);
    }
}