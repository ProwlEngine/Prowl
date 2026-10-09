// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Vector4 = System.Numerics.Vector4;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A scene's reflection probes. Every probe is a layer of one prefiltered cubemap array, layer 0 holding the sky,
/// and a tree over the probe boxes lets each pixel find the probes around it, the same way it finds its lights.
/// Captures happen before a frame's cameras render, never inside one.
/// </summary>
internal sealed class ReflectionProbeSystem : IDisposable
{
    /// <summary>Edge length of every probe's cube. Changing it captures every probe again.</summary>
    public static int Resolution { get; set; } = 128;

    /// <summary>GGX samples per prefiltered texel.</summary>
    private const int PrefilterSamples = 64;

    // The smallest prefiltered mip is 4 texels across, below that the roughest reflections turn blocky
    private const int SmallestMip = 4;

    private const int TexelsPerProbe = 6;
    private const int SkyLayer = 0;
    private const double SkyMinInterval = 1.0;

    private sealed class Slot
    {
        public ReflectionProbe Probe = null!;
        public int Layer;
        public int UploadedBakedVersion = -1;
        public bool Captured;
        public readonly Float4[] Record = new Float4[TexelsPerProbe];
    }

    private readonly List<Slot> _slots = new();
    private readonly Dictionary<ReflectionProbe, Slot> _bySlot = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<int> _freeLayers = new();
    private int _nextLayer = SkyLayer + 1;

    private Cubemap? _array;
    private readonly ShaderDataTable _table = new("ProwlReflectionProbes", "_ReflectionProbeTex", GraphicsFeature.FragmentStorageBuffers);
    private readonly LightTreeBuilder _tree = new();
    private Vector4[] _spheres = [];
    private bool _tableDirty = true;
    private int _root = -1;
    private int _nodeBase;

    private bool _skyCaptured;
    private int _skyHash;
    private readonly Stopwatch _sinceSky = Stopwatch.StartNew();

    // A realtime capture spread over several frames, its faces landing in the shared scratch cube
    private Slot? _slicing;
    private int _slicedFaces;
    private int _nextRealtime;
    private long _updatedFrame = -1;

    public int Count => _slots.Count;

    public void Register(ReflectionProbe probe)
    {
        if (_bySlot.ContainsKey(probe)) return;
        var slot = new Slot { Probe = probe, Layer = _freeLayers.Count > 0 ? _freeLayers.Pop() : _nextLayer++ };
        _slots.Add(slot);
        _bySlot[probe] = slot;
        _tableDirty = true;
    }

    public void Unregister(ReflectionProbe probe)
    {
        if (!_bySlot.Remove(probe, out Slot? slot)) return;
        _slots.Remove(slot);
        _freeLayers.Push(slot.Layer);
        if (ReferenceEquals(_slicing, slot)) _slicing = null;
        _tableDirty = true;
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Takes requested bakes, keeps the sky and the realtime probes current and uploads baked captures. Once a frame.</summary>
    public void Update(Scene scene)
    {
        if (ReflectionProbeCapture.Capturing || _updatedFrame == Time.FrameCount) return;
        _updatedFrame = Time.FrameCount;

        EnsureArray();

        foreach (Slot slot in _slots.ToArray())
        {
            if (!slot.Probe.BakeRequested) continue;
            slot.Probe.BakeRequested = false;
            slot.Probe.SetBaked(ReflectionProbeCapture.Bake(scene, slot.Probe, Resolution, MipCount));
            if (ReferenceEquals(_slicing, slot)) _slicing = null;
        }

        foreach (Slot slot in _slots)
        {
            if (slot.Probe.Mode != ReflectionProbeMode.Baked) continue;
            if (slot.UploadedBakedVersion == slot.Probe.BakedVersion && slot.Captured) continue;
            slot.UploadedBakedVersion = slot.Probe.BakedVersion;
            slot.Captured = slot.Probe.Baked is { } baked && ReflectionProbeCapture.Upload(baked, _array!, slot.Layer);
        }

        UpdateSky(scene);
        UpdateRealtime(scene);
        RefreshTable();
    }

    private int MipCount => Math.Max(1, Cubemap.MipCountFor((uint)Resolution) - Cubemap.MipCountFor(SmallestMip) + 1);

    // Grows by doubling, a new array holds nothing so everything is captured or uploaded into it again
    private void EnsureArray()
    {
        int layersNeeded = Math.Max(_nextLayer, 2);
        if (_array.IsValid() && _array!.Size == (uint)Resolution && _array.Layers >= layersNeeded) return;

        int layers = _array.IsValid() && _array!.Size == (uint)Resolution ? _array.Layers : 2;
        while (layers < layersNeeded) layers *= 2;
        if (_array.IsValid()) _array!.Dispose();
        _array = new Cubemap((uint)Resolution, MipCount, layers);

        _skyCaptured = false;
        _slicing = null;
        foreach (Slot slot in _slots)
        {
            slot.Captured = false;
            slot.UploadedBakedVersion = -1;
            if (slot.Probe.Mode == ReflectionProbeMode.Realtime) slot.Probe.CaptureRequested = true;
        }
    }

    private void UpdateSky(Scene scene)
    {
        if (_slicing != null) return;
        int hash = SkyHash(scene);
        bool changed = hash != _skyHash;
        if (_skyCaptured && !(changed && _sinceSky.Elapsed.TotalSeconds >= SkyMinInterval)) return;

        _skyHash = hash;
        _sinceSky.Restart();
        ReflectionProbeCapture.CaptureSky(scene, Resolution);
        ReflectionProbeCapture.Prefilter(_array!, SkyLayer, MipCount);
        _skyCaptured = true;
    }

    // Everything the sky is drawn from, so a capture follows the sun and the sky settings
    private static int SkyHash(Scene scene)
    {
        Scene.SkyboxParams sky = scene.Skybox;
        var hash = new HashCode();
        hash.Add(sky.Mode);
        hash.Add(sky.SolidColor);
        hash.Add(sky.GradientTop);
        hash.Add(sky.GradientBottom);
        hash.Add(sky.GradientExponent);
        if (sky.CustomMaterial is { IsLoaded: true } material)
        {
            hash.Add(material.InstanceID);
            hash.Add(material.GetStateHash());
        }
        IRenderableLight? sun = DefaultRenderPipeline.GetOrCreateLightSystem(scene).Directional;
        if (sun != null) hash.Add(sun.GetLightDirection());
        return hash.ToHashCode();
    }

    private void UpdateRealtime(Scene scene)
    {
        if (_slicing == null)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[(_nextRealtime + i) % _slots.Count];
                if (!WantsCapture(slot)) continue;
                _nextRealtime = (_nextRealtime + i + 1) % _slots.Count;
                slot.Probe.CaptureRequested = false;
                _slicing = slot;
                _slicedFaces = 0;
                break;
            }
        }
        if (_slicing == null) return;

        ReflectionProbe probe = _slicing.Probe;
        int faces = probe.TimeSlicing == ReflectionProbeTimeSlicing.IndividualFaces ? 1 : 6;
        if (_slicedFaces < 6)
        {
            int last = Math.Min(6, _slicedFaces + faces);
            ReflectionProbeCapture.CaptureFaces(scene, probe, Resolution, _slicedFaces, last);
            _slicedFaces = last;
            if (faces == 1 || _slicedFaces < 6) return;
        }

        ReflectionProbeCapture.Prefilter(_array!, _slicing.Layer, MipCount);
        _slicing.Captured = true;
        _slicing = null;
    }

    private static bool WantsCapture(Slot slot)
    {
        ReflectionProbe probe = slot.Probe;
        if (probe.Mode != ReflectionProbeMode.Realtime) return false;
        return probe.CaptureRequested || !slot.Captured || probe.RefreshMode == ReflectionProbeRefreshMode.EveryFrame;
    }

    // ---------------------------------------------------------------- shader data

    //   +0..+2  rows of the world to box matrix, the box centred on the capture point
    //   +3      box half size, blend distance
    //   +4      array layer as int bits, mip count
    //   +5      intensity, box projection, priority
    private void RefreshTable()
    {
        int count = 0;
        foreach (Slot slot in _slots)
        {
            if (!slot.Captured) continue;
            ReflectionProbe probe = slot.Probe;
            Float3 half = Maths.Max(probe.BoxSize * 0.5f, new Float3(0.01f));
            Quaternion rotation = probe.Transform.Rotation;
            Float3 center = probe.CapturePosition;
            Float3 x = rotation * Float3.UnitX, y = rotation * Float3.UnitY, z = rotation * Float3.UnitZ;
            float volume = 8f * half.X * half.Y * half.Z;

            Span<Float4> record = stackalloc Float4[TexelsPerProbe];
            record[0] = new Float4(x, -Float3.Dot(x, center));
            record[1] = new Float4(y, -Float3.Dot(y, center));
            record[2] = new Float4(z, -Float3.Dot(z, center));
            record[3] = new Float4(half, MathF.Max(probe.BlendDistance, 0f));
            record[4] = new Float4(BitConverter.Int32BitsToSingle(slot.Layer), 0f, 0f, 0f);
            record[5] = new Float4(MathF.Max(probe.Intensity, 0f), probe.BoxProjection ? 1f : 0f, probe.Importance + 1f / (1f + volume), 0f);
            if (!record.SequenceEqual(slot.Record))
            {
                record.CopyTo(slot.Record);
                _tableDirty = true;
            }
            count++;
        }
        if (!_tableDirty) return;
        _tableDirty = false;

        if (count == 0)
        {
            _root = -1;
            return;
        }

        if (_spheres.Length < count) _spheres = new Vector4[count];
        var live = new List<Slot>(count);
        foreach (Slot slot in _slots)
        {
            if (!slot.Captured) continue;
            Float4 h = slot.Record[3];
            Float3 center = slot.Probe.CapturePosition;
            float radius = MathF.Sqrt(h.X * h.X + h.Y * h.Y + h.Z * h.Z);
            _spheres[live.Count] = new Vector4(center.X, center.Y, center.Z, radius);
            live.Add(slot);
        }

        _tree.Build(_spheres.AsSpan(0, count), 4, morton: false);
        _nodeBase = count * TexelsPerProbe;
        _table.EnsureCapacity(_nodeBase + _tree.NodeCount * LightTreeNode.Vec4Count);

        ReadOnlySpan<int> order = _tree.Order;
        for (int i = 0; i < count; i++)
            _table.Write(i * TexelsPerProbe, live[order[i]].Record);
        ForwardLightTrees.WriteNodes(_table, _tree.Nodes, _nodeBase, 0, 0);
        _root = 0;
    }

    /// <summary>Binds the probe array, the probe table and their uniforms for every shader.</summary>
    public void Bind(CommandBuffer cmd)
    {
        bool ready = _array.IsValid() && _skyCaptured;
        cmd.SetGlobalInt("_ReflectionProbesReady", ready ? 1 : 0);
        if (!ready) return;

        cmd.SetGlobalTextureCube("_ReflectionProbes", _array);
        cmd.SetGlobalFloat("_ReflectionProbeMips", _array!.MipLevels);
        cmd.SetGlobalInt("_ReflectionProbeRoot", _root);
        cmd.SetGlobalInt("_ReflectionProbeNodeBase", _nodeBase);
        if (_root >= 0) _table.Bind(cmd);
    }

    public void Dispose()
    {
        if (_array.IsValid()) _array!.Dispose();
        _array = null;
        _table.Dispose();
        _slots.Clear();
        _bySlot.Clear();
    }
}

/// <summary>Renders the scene into cube faces and prefilters them for reflections.</summary>
internal static class ReflectionProbeCapture
{
    /// <summary>True while a capture renders, so the render it starts does not start another.</summary>
    public static bool Capturing { get; private set; }

    private static GameObject? s_cameraObject;
    private static Camera? s_camera;
    private static RenderTexture? s_target;
    private static Cubemap? s_scratch;
    private static Material? s_prefilter;

    // Forward, right and up of each GL cube face, as the face's texels lay out directions
    private static readonly (Float3 Forward, Float3 Right, Float3 Up)[] s_faces =
    [
        (Float3.UnitX, -Float3.UnitZ, -Float3.UnitY),
        (-Float3.UnitX, Float3.UnitZ, -Float3.UnitY),
        (Float3.UnitY, Float3.UnitX, Float3.UnitZ),
        (-Float3.UnitY, Float3.UnitX, -Float3.UnitZ),
        (Float3.UnitZ, Float3.UnitX, -Float3.UnitY),
        (-Float3.UnitZ, -Float3.UnitX, -Float3.UnitY),
    ];

    private static void EnsureResources(int resolution)
    {
        if (s_camera.IsNotValid())
        {
            s_cameraObject = new GameObject("Reflection Probe Capture") { HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos };
            s_camera = s_cameraObject.AddComponent<Camera>();
            s_camera.FieldOfView = 90f;
            s_camera.HDR = true;
        }
        if (s_target.IsNotValid() || s_target!.Width != resolution)
        {
            if (s_target.IsValid()) s_target!.Dispose();
            s_target = new RenderTexture(resolution, resolution, false, [TextureImageFormat.Short4]);
        }
        if (s_scratch.IsNotValid() || s_scratch!.Size != (uint)resolution)
        {
            if (s_scratch.IsValid()) s_scratch!.Dispose();
            s_scratch = new Cubemap((uint)resolution, mipChain: true);
        }
        if (s_prefilter.IsNotValid())
            s_prefilter = new Material(Shader.LoadDefault(DefaultShader.PrefilterCubemap));
    }

    /// <summary>Renders faces <paramref name="first"/> up to <paramref name="end"/> of the probe into the scratch cube.</summary>
    public static void CaptureFaces(Scene scene, ReflectionProbe probe, int resolution, int first, int end)
        => Render(scene, probe.CapturePosition, probe.NearClip, probe.FarClip, probe.CullingMask, resolution, first, end);

    /// <summary>Renders only the sky into all six faces of the scratch cube.</summary>
    public static void CaptureSky(Scene scene, int resolution)
        => Render(scene, Float3.Zero, 0.1f, 10f, LayerMask.Nothing, resolution, 0, 6);

    private static void Render(Scene scene, Float3 position, float near, float far, LayerMask mask, int resolution, int first, int end)
    {
        EnsureResources(resolution);
        Camera camera = s_camera!;
        camera.NearClipPlane = MathF.Max(near, 0.001f);
        camera.FarClipPlane = MathF.Max(far, camera.NearClipPlane + 0.01f);
        camera.CullingMask = mask;
        camera.ClearFlags = CameraClearFlags.Skybox;

        Capturing = true;
        scene.Add(s_cameraObject!);
        try
        {
            for (int face = first; face < end; face++)
            {
                (Float3 forward, Float3 right, Float3 up) = s_faces[face];
                camera.Transform.Position = position;
                camera.Transform.Rotation = Quaternion.LookRotation(forward, up);
                camera.UpdateRenderData(s_target);

                DefaultRenderPipeline.Default.Render(camera, new RenderingData { FallbackTarget = s_target, SkipUI = true });

                // The rendered image runs along the camera's own right and up, the face along its GL axes
                Float4x4 view = camera.ViewMatrix;
                bool flipX = Float3.Dot(new Float3(view.c0.X, view.c1.X, view.c2.X), right) < 0f;
                bool flipY = Float3.Dot(new Float3(view.c0.Y, view.c1.Y, view.c2.Y), up) < 0f;

                using CommandBuffer cmd = Graphics.GetCommandBuffer("ReflectionProbe.Face");
                cmd.SetRenderTargets(s_scratch!.GetFaceTarget(face, 0), s_target!.frameBuffer);
                cmd.BlitFramebuffer(flipX ? resolution : 0, flipY ? resolution : 0, flipX ? 0 : resolution, flipY ? 0 : resolution,
                                    0, 0, resolution, resolution, ClearFlags.Color, BlitFilter.Nearest);
                cmd.SetRenderTarget(null);
                Graphics.Submit(cmd);
            }
        }
        finally
        {
            scene.Remove(s_cameraObject!);
            Capturing = false;
        }
    }

    /// <summary>Filters the scratch cube into every roughness mip of <paramref name="layer"/> of <paramref name="destination"/>.</summary>
    public static void Prefilter(Cubemap destination, int layer, int mips)
    {
        Graphics.GenerateMipmap(s_scratch!.Handle);
        Mesh quad = Mesh.GetFullscreenQuad();

        using CommandBuffer cmd = Graphics.GetCommandBuffer("ReflectionProbe.Prefilter");
        for (int mip = 0; mip < mips; mip++)
        {
            uint size = destination.MipSize(mip);
            for (int face = 0; face < 6; face++)
            {
                cmd.SetRenderTarget(destination.GetFaceTarget(face, mip, false, destination.Layers > 0 ? layer : 0));
                cmd.SetViewport(0, 0, size, size);
                var props = new PropertyState();
                props.SetTextureCube("_Source", s_scratch);
                props.SetInt("_Face", face);
                props.SetFloat("_Roughness", mips > 1 ? mip / (float)(mips - 1) : 0f);
                props.SetFloat("_SourceSize", s_scratch.Size);
                props.SetInt("_SampleCount", PrefilterSamplesFor(mip));
                cmd.DrawMesh(quad, s_prefilter!, 0, Float4x4.Identity, props);
            }
        }
        cmd.SetRenderTarget(null);
        Graphics.Submit(cmd);
    }

    private static int PrefilterSamplesFor(int mip) => mip == 0 ? 1 : 64;

    /// <summary>Captures and prefilters a probe into a standalone cube and reads it back to keep with the scene.</summary>
    public static ReflectionProbe.BakedCubemap? Bake(Scene scene, ReflectionProbe probe, int resolution, int mips)
    {
        CaptureFaces(scene, probe, resolution, 0, 6);
        using var target = new Cubemap((uint)resolution, mipChain: true);
        Prefilter(target, 0, mips);

        var faces = new byte[6 * mips][];
        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < mips; mip++)
            {
                byte[] data = new byte[target.FaceByteSize(mip)];
                target.GetFaceData(face, data, mip);
                faces[face * mips + mip] = data;
            }
        return new ReflectionProbe.BakedCubemap { Size = resolution, Mips = mips, Faces = faces };
    }

    /// <summary>Copies a baked capture into one layer of the probe array, scaled when it was baked at another size.</summary>
    public static bool Upload(ReflectionProbe.BakedCubemap baked, Cubemap array, int layer)
    {
        if (baked.Size <= 0 || baked.Mips <= 0 || baked.Faces.Length < 6 * baked.Mips) return false;

        using var source = new Cubemap((uint)baked.Size, mipChain: true);
        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < baked.Mips; mip++)
                source.SetFaceData(face, new Memory<byte>(baked.Faces[face * baked.Mips + mip]), mip);

        using CommandBuffer cmd = Graphics.GetCommandBuffer("ReflectionProbe.Upload");
        int mips = array.MipLevels;
        for (int mip = 0; mip < mips; mip++)
        {
            int sourceMip = mips > 1 ? (int)MathF.Round(mip * (baked.Mips - 1) / (float)(mips - 1)) : 0;
            int from = (int)source.MipSize(sourceMip), to = (int)array.MipSize(mip);
            for (int face = 0; face < 6; face++)
            {
                cmd.SetRenderTargets(array.GetFaceTarget(face, mip, false, layer), source.GetFaceTarget(face, sourceMip));
                cmd.BlitFramebuffer(0, 0, from, from, 0, 0, to, to, ClearFlags.Color, BlitFilter.Linear);
            }
        }
        cmd.SetRenderTarget(null);
        Graphics.Submit(cmd);
        return true;
    }
}
