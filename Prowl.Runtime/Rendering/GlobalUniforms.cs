// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Graphite;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Structure matching the layout of global uniforms in ShaderVariables.glsl
/// Uses std140 layout for uniform buffer compatibility
/// Contains only per-frame data that is constant across all draw calls
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GlobalUniformsData
{
    public static int SizeInBytes => Marshal.SizeOf<GlobalUniformsData>();

    // Suppress IDE0130 warning about naming rule violation, as these are meant to match the naming of the Shader code
#pragma warning disable IDE1006 // Naming Styles

    // Camera matrices (each mat4 = 64 bytes)
    public Float4x4 prowl_MatV;               // 64 bytes
    public Float4x4 prowl_MatIV;              // 64 bytes
    public Float4x4 prowl_MatP;               // 64 bytes
    public Float4x4 prowl_MatVP;              // 64 bytes
    public Float4x4 prowl_PrevViewProj;       // 64 bytes
    public Float4x4 prowl_MatIP;              // 64 bytes (inverse projection)
    public Float4x4 prowl_MatIVP;             // 64 bytes (inverse view-projection)
    public Float4x4 prowl_MatVP_NonJittered;  // 64 bytes (current view-projection without TAA jitter)

    // Camera parameters
    public Float3 _WorldSpaceCameraPos;       // 12 bytes
    public float _padding0;                   // 4 bytes (padding)

    public Float4 _ProjectionParams;          // 16 bytes
    public Float4 _ScreenParams;              // 16 bytes
    public Float2 _CameraJitter;              // 8 bytes
    public Float2 _CameraPreviousJitter;      // 8 bytes

    // Time parameters
    public Float4 _Time;                      // 16 bytes
    public Float4 _SinTime;                   // 16 bytes
    public Float4 _CosTime;                   // 16 bytes
    public Float4 prowl_DeltaTime;            // 16 bytes

#pragma warning restore IDE1006 // Naming Styles
}

/// <summary>
/// Manages the global uniform buffer for efficient shader data upload
/// </summary>
public static class GlobalUniforms
{
    private static StreamingBuffer? s_uniformBuffer;
    private static GlobalUniformsData s_data;

    /// <summary>
    /// Initializes the global uniform buffer
    /// </summary>
    public static void Initialize()
    {
        if (s_uniformBuffer == null)
        {
            s_uniformBuffer = Graphics.Device.ResourceFactory.CreateStreamingBuffer(
                new BufferDescription((uint)GlobalUniformsData.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            s_uniformBuffer.Name = "GlobalUniforms (Frame)";
        }
    }

    /// <summary>
    /// Writes this frame's data into the current ring slot and re-registers it as the "Frame"
    /// global. This is a per-frame write regardless of whether the data changed, since the
    /// StreamingBuffer rotates to a different backing buffer every frame; skipping the write on
    /// an unchanged frame would leave that ring slot holding stale data from MaxFramesInFlight
    /// frames ago.
    /// </summary>
    public static void Upload()
    {
        Initialize();

        DeviceBuffer current = s_uniformBuffer!.Current;
        Graphics.Device.UpdateBuffer(current, 0u, new[] { s_data });

        // GlobalPropertySet.ClearGlobals() wipes all bindings once per camera, so the
        // "Frame" buffer binding must be re-registered every Upload rather than once in
        // Initialize, or it would disappear after the first camera renders.
        GlobalPropertySet.SetBuffer("Frame", current);
    }

    /// <summary>
    /// Gets the current frame's ring-slot buffer for binding to shaders. Does NOT lazily
    /// initialize: this is called by the executor on the render thread, and the non-atomic
    /// create-if-null in <see cref="Initialize"/> must only ever run on the main
    /// thread. <see cref="Upload"/> (called by the pipeline each frame before any
    /// draw) creates the buffer, so by submit order it is non-null here. Returns
    /// null only before the first Upload; PrepareDraw skips the bind in that case.
    /// </summary>
    public static DeviceBuffer? GetBuffer() => s_uniformBuffer?.Current;

    /// <summary>
    /// Cleans up the global uniform buffer resources
    /// </summary>
    public static void Dispose()
    {
        s_uniformBuffer?.Dispose();
        s_uniformBuffer = null;
    }

    // Camera matrix setters (per-frame data)
    public static void SetMatrixV(Float4x4 value)
    {
        s_data.prowl_MatV = (Float4x4)value;
    }

    public static void SetMatrixIV(Float4x4 value)
    {
        s_data.prowl_MatIV = (Float4x4)value;
    }

    public static void SetMatrixP(Float4x4 value)
    {
        s_data.prowl_MatP = (Float4x4)value;
    }

    public static void SetMatrixVP(Float4x4 value)
    {
        s_data.prowl_MatVP = (Float4x4)value;
    }

    public static void SetMatrixIP(Float4x4 value)
    {
        s_data.prowl_MatIP = (Float4x4)value;
    }

    public static void SetMatrixIVP(Float4x4 value)
    {
        s_data.prowl_MatIVP = (Float4x4)value;
    }

    public static void SetMatrixVPNonJittered(Float4x4 value)
    {
        s_data.prowl_MatVP_NonJittered = (Float4x4)value;
    }

    public static void SetPrevViewProj(Float4x4 value)
    {
        s_data.prowl_PrevViewProj = (Float4x4)value;
    }

    // Camera parameters
    public static void SetWorldSpaceCameraPos(Float3 value)
    {
        s_data._WorldSpaceCameraPos = (Float3)value;
    }

    public static void SetProjectionParams(Float4 value)
    {
        s_data._ProjectionParams = (Float4)value;
    }

    public static void SetScreenParams(Float4 value)
    {
        s_data._ScreenParams = (Float4)value;
    }

    public static void SetCameraJitter(Float2 value)
    {
        s_data._CameraJitter = (Float2)value;
    }

    public static void SetCameraPreviousJitter(Float2 value)
    {
        s_data._CameraPreviousJitter = (Float2)value;
    }

    // Time parameters
    public static void SetTime(Float4 value)
    {
        s_data._Time = (Float4)value;
    }

    public static void SetSinTime(Float4 value)
    {
        s_data._SinTime = (Float4)value;
    }

    public static void SetCosTime(Float4 value)
    {
        s_data._CosTime = (Float4)value;
    }

    public static void SetDeltaTime(Float4 value)
    {
        s_data.prowl_DeltaTime = (Float4)value;
    }

    /// <summary>
    /// Resets all data to defaults
    /// </summary>
    public static void Clear()
    {
        s_data = new GlobalUniformsData();
    }
}
