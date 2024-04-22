using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Primitives;

namespace Prowl.Runtime;

public class GBuffer
{
    internal RenderTexture buffer;

    public GraphicsFrameBuffer frameBuffer { get { return buffer.frameBuffer; } }
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
        TextureImageFormat[] formats =
        [
            TextureImageFormat.Short4, // Albedo & AO
            TextureImageFormat.Short4, // Normal & Metalness
            TextureImageFormat.Short4, // Position & Roughness
            TextureImageFormat.Short3, // Emission
            TextureImageFormat.Short2, // Velocity
            TextureImageFormat.Short, // ObjectIDs
        ];
        buffer = new RenderTexture(width, height, 6, true, formats);
    }

    public void Begin(bool clear = true)
    {
        Graphics.Device.BindFramebuffer(frameBuffer);

        Graphics.Viewport(Width, Height);

        // Start with the initial GBuffer Clear
        if (clear)
            Graphics.Clear(0,0,0,0);
    }

    public void End()
    {
        Graphics.Device.UnbindFramebuffer();
    }

    public int GetObjectIDAt(Vector2 uv)
    {
        int x = (int)(uv.x * Width);
        int y = (int)(uv.y * Height);
        Graphics.Device.BindFramebuffer(frameBuffer);
        float result = Graphics.Device.ReadPixel<float>(5, x, y, TextureImageFormat.Short);
        return (int)result;
    }

    public Vector3 GetViewPositionAt(Vector2 uv)
    {
        int x = (int)(uv.x * Width);
        int y = (int)(uv.y * Height);
        Graphics.Device.BindFramebuffer(frameBuffer);
        Vector3 result = Graphics.Device.ReadPixel<System.Numerics.Vector3>(2, x, y, TextureImageFormat.Short3);
        return result;
    }

    public void UnloadGBuffer()
    {
        if (frameBuffer == null) return;
        AlbedoAO.Dispose();
        NormalMetallic.Dispose();
        PositionRoughness.Dispose();
        Emission.Dispose();
        Velocity.Dispose();
        ObjectIDs.Dispose();
        Depth.Dispose();
        frameBuffer.Dispose();
    }
}