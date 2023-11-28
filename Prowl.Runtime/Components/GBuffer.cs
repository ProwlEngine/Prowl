using Prowl.Runtime.Resources;
using Raylib_cs;

namespace Prowl.Runtime.Components;

public class GBuffer
{
    internal RenderTexture buffer;
    internal RenderTexture lightBuffer;
    internal RenderTexture combinedBuffer;

    public uint fbo { get { return buffer.fboId; } }
    public uint lightingFBO { get { return lightBuffer.fboId; } }
    public uint combinedFBO { get { return combinedBuffer.fboId; } }
    public int Width { get { return buffer.Width; } }
    public int Height { get { return buffer.Height; } }
    public Raylib_cs.Texture2D AlbedoAO { get { return buffer.InternalTextures[0]; } }
    public Raylib_cs.Texture2D NormalMetallic { get { return buffer.InternalTextures[1]; } }
    public Raylib_cs.Texture2D PositionRoughness { get { return buffer.InternalTextures[2]; } }
    public Raylib_cs.Texture2D Emission { get { return buffer.InternalTextures[3]; } }
    public Raylib_cs.Texture2D Velocity { get { return buffer.InternalTextures[4]; } }
    public Raylib_cs.Texture2D Depth { get { return buffer.InternalDepth; } }

    public Raylib_cs.Texture2D Lighting { get { return lightBuffer.InternalTextures[0]; } }
    public Raylib_cs.Texture2D Combined { get { return combinedBuffer.InternalTextures[0]; } }

    public bool IsRendering;

    public GBuffer(int width, int height, float finalScale = 1f)
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
        ];
        buffer = new RenderTexture(width, height, 5, true, formats);

        formats = new PixelFormat[1];
        formats[0] = PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32; // Lighting
        lightBuffer = new RenderTexture(width, height, 1, false, formats);

        formats = new PixelFormat[1];
        formats[0] = PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32; // Combined
        combinedBuffer = new RenderTexture((int)(width / finalScale), (int)(height / finalScale), 1, false, formats);
    }

    public bool IsReady()
    {
        return (fbo > 0) &&
                Raylib.IsTextureReady(AlbedoAO) &&
                Raylib.IsTextureReady(NormalMetallic) &&
                Raylib.IsTextureReady(PositionRoughness) &&
                Raylib.IsTextureReady(Emission) &&
                Raylib.IsTextureReady(Velocity) &&
                Raylib.IsTextureReady(Depth) &&
                Raylib.IsTextureReady(Lighting) &&
                Raylib.IsTextureReady(Combined);
    }

    public void Begin(bool clear = true)
    {
        IsRendering = true;
        Raylib.BeginTextureMode(new RenderTexture2D() { id = fbo, texture = AlbedoAO, depth = Depth });
        Rlgl.rlActiveDrawBuffers(5);
        Rlgl.rlDisableColorBlend();

        //Raylib.BeginBlendMode(BlendMode.BLEND_ADDITIVE);

        // Start with the initial GBuffer Clear
        if (clear)
            Raylib.ClearBackground(Color.black);
    }

    public void End()
    {
        Rlgl.rlEnableColorBlend();
        Raylib.EndTextureMode();

        //Raylib.EndBlendMode();

        IsRendering = false;
    }

    public void BeginLighting(bool clear = true)
    {
        IsRendering = true;
        Raylib.BeginTextureMode(new RenderTexture2D() { id = lightingFBO, texture = Lighting });
        Rlgl.rlActiveDrawBuffers(1);

        //Rlgl.rlDisableDepthMask();
        Rlgl.rlDisableDepthTest();
        Rlgl.rlSetCullFace(0); // Cull the front faces for the lighting pass

        //Rlgl.rlSetBlendMode(BlendMode.BLEND_ADDITIVE);

        // Start with the initial Lighting Clear
        if (clear)
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
    }

    public void EndLighting()
    {
        //Rlgl.rlEnableDepthMask();
        Rlgl.rlEnableDepthTest();
        Rlgl.rlSetCullFace(1);

        //Rlgl.rlSetBlendMode(BlendMode.BLEND_ALPHA);

        Raylib.EndTextureMode();
        IsRendering = false;
    }

    public void BeginCombine(bool clear = true)
    {
        IsRendering = true;
        Raylib.BeginTextureMode(new RenderTexture2D() { id = combinedFBO, texture = Combined });
        Rlgl.rlActiveDrawBuffers(1); // Drawing only into Diffuse for the final Combine pass

        Rlgl.rlDisableDepthMask();
        Rlgl.rlDisableDepthTest();
        Rlgl.rlDisableBackfaceCulling();
        //Rlgl.rlDisableColorBlend();

        if(clear)
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
    }

    public void EndCombine()
    {
        //Rlgl.rlEnableColorBlend();
        Rlgl.rlEnableDepthMask();
        Rlgl.rlEnableDepthTest();
        Rlgl.rlEnableBackfaceCulling();

        Raylib.EndTextureMode();
        IsRendering = false;
    }

    public void UnloadGBuffer()
    {
        if (fbo <= 0) return;
        Rlgl.rlUnloadTexture(AlbedoAO.id);
        Rlgl.rlUnloadTexture(NormalMetallic.id);
        Rlgl.rlUnloadTexture(PositionRoughness.id);
        Rlgl.rlUnloadTexture(Emission.id);
        Rlgl.rlUnloadTexture(Velocity.id);
        // Depth should be automatically unloaded
        Rlgl.rlUnloadFramebuffer(fbo);

        Rlgl.rlUnloadTexture(Lighting.id);
        Rlgl.rlUnloadFramebuffer(lightingFBO);

        Rlgl.rlUnloadTexture(Combined.id);
        Rlgl.rlUnloadFramebuffer(combinedFBO);
    }
}