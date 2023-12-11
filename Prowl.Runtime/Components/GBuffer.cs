using Raylib_cs;

namespace Prowl.Runtime;

public class GBuffer
{
    internal RenderTexture buffer;

    public uint fbo { get { return buffer.fboId; } }
    public int Width { get { return buffer.Width; } }
    public int Height { get { return buffer.Height; } }
    public Raylib_cs.Texture2D AlbedoAO { get { return buffer.InternalTextures[0]; } }
    public Raylib_cs.Texture2D NormalMetallic { get { return buffer.InternalTextures[1]; } }
    public Raylib_cs.Texture2D PositionRoughness { get { return buffer.InternalTextures[2]; } }
    public Raylib_cs.Texture2D Emission { get { return buffer.InternalTextures[3]; } }
    public Raylib_cs.Texture2D Velocity { get { return buffer.InternalTextures[4]; } }
    public Raylib_cs.Texture2D ObjectIDs { get { return buffer.InternalTextures[5]; } }
    public Raylib_cs.Texture2D Depth { get { return buffer.InternalDepth; } }
    
    public GBuffer(int width, int height)
    {
#warning TODO: Dont always use 32bits, optomize this and use only whats absolutely needed, some precision loss is ok as long as it doesnt hurt visuals much, normals for example could probably be 16
#warning TODO: Switch to a singular 16bit "Material" buffer, AO, Rough, Metal, the final channel would be 16 bools, Lit, Fog, etc
        PixelFormat[] formats =
        [
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, // Albedo & AO
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, // Normal & Metalness
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, // Position & Roughness
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, // Emission
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, // Velocity
            PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32, // ObjectIDs
        ];
        buffer = new RenderTexture(width, height, 6, true, formats);


    }

    public bool IsReady()
    {
        return fbo > 0 &&
                Raylib.IsTextureReady(AlbedoAO) &&
                Raylib.IsTextureReady(NormalMetallic) &&
                Raylib.IsTextureReady(PositionRoughness) &&
                Raylib.IsTextureReady(Emission) &&
                Raylib.IsTextureReady(Velocity) &&
                Raylib.IsTextureReady(ObjectIDs) &&
                Raylib.IsTextureReady(Depth);
    }

    public void Begin(bool clear = true)
    {
        Raylib.BeginTextureMode(new RenderTexture2D() { id = fbo, texture = AlbedoAO, depth = Depth });
        Rlgl.rlActiveDrawBuffers(6);
        Rlgl.rlDisableColorBlend();

        // Start with the initial GBuffer Clear
        if (clear)
            Raylib.ClearBackground(Color.black);
    }

    public void End()
    {
        Rlgl.rlEnableColorBlend();
        Raylib.EndTextureMode();
    }

    public void UnloadGBuffer()
    {
        if (fbo <= 0) return;
        Rlgl.rlUnloadTexture(AlbedoAO.id);
        Rlgl.rlUnloadTexture(NormalMetallic.id);
        Rlgl.rlUnloadTexture(PositionRoughness.id);
        Rlgl.rlUnloadTexture(Emission.id);
        Rlgl.rlUnloadTexture(Velocity.id);
        Rlgl.rlUnloadTexture(ObjectIDs.id);
        // Depth should be automatically unloaded
        Rlgl.rlUnloadFramebuffer(fbo);
    }
}