using System;

using Prowl.Graphite.Samples;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


public static class Program
{
    static GraphicsDevice device;
    static CommandBuffer buffer;
    static RenderMSTracker tracker;

    static ModelAsset model;
    static GraphicsProgram shader;
    static PropertySet properties;
    static Texture albedo;

    static Float3 center;
    static float distance;
    static float angle;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            Debug = false,
            SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = true,
            PreferStandardClipSpaceYDirection = true
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options);
    }


    public static void Load(GraphicsDevice newDevice)
    {
        device = newDevice;

        tracker = new(newDevice);
        buffer = device.ResourceFactory.CreateCommandBuffer();

        shader = ShaderDefLoader.Load(device, "Shaders/Unlit.shader");

        model = ModelAsset.Load(device, "Assets/Models/DamagedHelmet.glb", unwrapLightmapUVs: false);

        MaterialInfo material = model.Materials.Length > 0 ? model.Materials[0] : default;
        albedo = material.AlbedoTexture ?? model.GetDefaultWhite();

        properties = new();
        properties.SetTexture("AlbedoTexture", albedo, device.LinearSampler);
        properties.SetFloat4("BaseColor", new Float4(1, 1, 1, 1));

        center = model.Bounds.Center;
        distance = Float3.Length(model.Bounds.Extents) * 3.0f;
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        angle += (float)dt * 0.5f;

        float radius = Math.Max(distance, 0.001f);
        Float3 eye = center + new Float3(MathF.Sin(angle), 0.35f, MathF.Cos(angle)) * distance;

        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1.0f, radius * 0.02f, radius * 10.0f);
        Float4x4 view = Float4x4.CreateLookAt(eye, center, Float3.UnitY);
        Float4x4 mvp = projection * view;

        properties.SetMatrix("MatrixMVP", mvp);

        Frame frame = device.BeginFrame();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));

        buffer.SetShader(shader);
        buffer.SetVertexSource(model.Mesh);
        buffer.SetProperties(properties);
        buffer.DrawIndexed();

        buffer.End();

        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        tracker.End(dt);

        device.SwapBuffers();
    }


    public static void Close()
    {
        buffer.Dispose();
        shader.Dispose();
        model.Dispose();
        device.Dispose();
    }
}
