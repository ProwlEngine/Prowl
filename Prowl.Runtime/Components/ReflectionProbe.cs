// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;


using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GProbeMode = Prowl.Graphite.Rendering.ReflectionProbeMode;
using GProbeRefresh = Prowl.Graphite.Rendering.ReflectionProbeRefresh;
using GProbeSlicing = Prowl.Graphite.Rendering.ReflectionProbeTimeSlicing;
using ReflectionProbeMode = Prowl.Runtime.ReflectionProbeMode;
using ReflectionProbeTimeSlicing = Prowl.Runtime.ReflectionProbeTimeSlicing;

namespace Prowl.Runtime;

public enum ReflectionProbeMode
{
    /// <summary>Captured once by <see cref="ReflectionProbe.Bake"/> and saved with the scene.</summary>
    Baked,

    /// <summary>Captured while the game runs, as often as <see cref="ReflectionProbe.RefreshMode"/> says.</summary>
    Realtime,
}

public enum ReflectionProbeRefreshMode
{
    /// <summary>Once each time the probe is enabled.</summary>
    OnEnable,

    /// <summary>Again as soon as the last capture finishes.</summary>
    EveryFrame,

    /// <summary>Only when <see cref="ReflectionProbe.RenderProbe"/> is called.</summary>
    ViaScripting,
}

public enum ReflectionProbeTimeSlicing
{
    /// <summary>All six faces and the filtering in one frame.</summary>
    AllFacesAtOnce,

    /// <summary>One face a frame, filtered on the seventh, so a capture costs a sixth as much each frame.</summary>
    IndividualFaces,
}

/// <summary>
/// Captures its surroundings into a cubemap that surfaces inside its box reflect. Reflections are projected onto the
/// box, so a probe fitted to a room shows its walls in the right place from anywhere inside it. Where probes overlap,
/// the one with the higher <see cref="Importance"/> wins, then the smaller one, fading over <see cref="BlendDistance"/>.
/// Surfaces no probe covers reflect the sky.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Rendering/Reflection Probe")]
[ComponentIcon("")] // Dot circle
public class ReflectionProbe : Component, IRenderSync
{
    public ReflectionProbeMode Mode { get => mode; set { mode = value; Wake(); } }
    [SerializeField] private ReflectionProbeMode mode = ReflectionProbeMode.Baked;

    public ReflectionProbeRefreshMode RefreshMode { get => refreshMode; set { refreshMode = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsRealtime))] private ReflectionProbeRefreshMode refreshMode = ReflectionProbeRefreshMode.OnEnable;

    public ReflectionProbeTimeSlicing TimeSlicing { get => timeSlicing; set { timeSlicing = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsRealtime))] private ReflectionProbeTimeSlicing timeSlicing = ReflectionProbeTimeSlicing.AllFacesAtOnce;

    /// <summary>Wins over overlapping probes with a lower value, whatever their size.</summary>
    public int Importance { get => importance; set { importance = value; Wake(); } }
    [SerializeField] private int importance = 1;

    public float Intensity { get => intensity; set { intensity = value; Wake(); } }
    [SerializeField] private float intensity = 1f;

    /// <summary>Size of the box the probe affects and projects reflections onto, turned with the object.</summary>
    public Float3 BoxSize { get => boxSize; set { boxSize = value; Wake(); } }
    [SerializeField, Header("Box")] private Float3 boxSize = new(10f, 10f, 10f);

    /// <summary>Where the box sits relative to the object, which is where the capture is taken from.</summary>
    public Float3 BoxOffset { get => boxOffset; set { boxOffset = value; Wake(); } }
    [SerializeField] private Float3 boxOffset = Float3.Zero;

    /// <summary>How far in from the box faces reflections fade in, so neighbouring probes blend.</summary>
    public float BlendDistance { get => blendDistance; set { blendDistance = value; Wake(); } }
    [SerializeField] private float blendDistance = 1f;

    /// <summary>Projects reflections onto the box rather than treating them as infinitely far away.</summary>
    public bool BoxProjection { get => boxProjection; set { boxProjection = value; Wake(); } }
    [SerializeField] private bool boxProjection = true;

    /// <summary>Edge length of a realtime capture's cube, a power of two.</summary>
    public int Resolution { get => resolution; set { resolution = value; Wake(); } }
    [SerializeField, Header("Capture")] private int resolution = 128;
    public float NearClip { get => nearClip; set { nearClip = value; Wake(); } }
    [SerializeField] private float nearClip = 0.1f;
    public float FarClip { get => farClip; set { farClip = value; Wake(); } }
    [SerializeField] private float farClip = 1000f;
    public LayerMask CullingMask { get => cullingMask; set { cullingMask = value; Wake(); } }
    [SerializeField] private LayerMask cullingMask = LayerMask.Everything;

    [SerializeField, HideInInspector] private BakedCubemap? _baked;

    private bool IsRealtime => Mode == ReflectionProbeMode.Realtime;

    /// <summary>Whether a baked capture is saved with the probe.</summary>
    public bool HasBakedData => _baked != null;

    internal BakedCubemap? Baked => _baked;

    [NonSerialized, NotHeld] private ReflectionProbeHandle _handle;
    [NonSerialized, NotHeld] private RenderWorld? _world;
    [NonSerialized, NotHeld] private Graphite.Texture? _bakedCube;
    [NonSerialized, NotHeld] private BakedCubemap? _bakedShown;
    [NonSerialized, NotHeld] private bool _captureRequested;
    [NonSerialized, NotHeld] private bool _bakeRequested;
    [NonSerialized, NotHeld] private ReflectionProbeDesc _last;

    /// <summary>Asks a realtime probe to capture again. It happens before a later frame renders.</summary>
    public void RenderProbe() { _captureRequested = true; Wake(); }

    /// <summary>Captures the probe in a coming frame and keeps the result with the scene.</summary>
    [Button("Bake")]
    public void Bake() { _bakeRequested = true; Wake(); }

    internal void SetBaked(BakedCubemap? baked)
    {
        _baked = baked;
        Wake();
    }

    // Syncs it again next frame, for a setting that changed
    private void Wake() => SceneRendering.Wake(this, GameObject);


    [Button("Clear Baked")]
    public void ClearBaked() => SetBaked(null);

    bool IRenderSync.Sync(RenderWorld world)
    {
        if (_world != null && _world != world) Remove();

        if (_bakedShown != _baked)
        {
            Graphics.DeferDispose(_bakedCube);
            _bakedCube = null;
            _bakedShown = _baked;
            if (_baked != null && Graphics.Renderer != null && _baked.Faces.Length > 0)
            {
                try { _bakedCube = Graphics.Renderer.CreateReflectionCubemap(new BakedReflectionProbe { Size = _baked.Size, Mips = _baked.Mips, Faces = _baked.Faces }); }
                catch (Exception e) { Debug.LogError($"Reflection probe '{Name}' could not show its bake: {e.Message}"); }
            }
        }

        Float3 position = CapturePosition;
        ReflectionProbeDesc desc = new()
        {
            Position = new Double3(position.X, position.Y, position.Z),
            Rotation = Transform.Rotation,
            BoxSize = BoxSize,
            BlendDistance = MathF.Max(BlendDistance, 0f),
            BoxProjection = BoxProjection,
            Importance = Importance,
            Intensity = Intensity,
            Mode = Mode == ReflectionProbeMode.Realtime ? GProbeMode.Realtime : GProbeMode.Baked,
            Refresh = RefreshMode == ReflectionProbeRefreshMode.EveryFrame ? GProbeRefresh.EveryFrame : GProbeRefresh.Once,
            TimeSlicing = TimeSlicing == ReflectionProbeTimeSlicing.IndividualFaces ? GProbeSlicing.IndividualFaces : GProbeSlicing.AllFacesAtOnce,
            NearClip = NearClip,
            FarClip = FarClip,
            CullingMask = (uint)CullingMask.Mask & ~Debug.GizmoLayerMask,
            Resolution = (uint)Math.Clamp((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(Resolution, 16)), 16, 2048),
            Cubemap = _bakedCube,
        };

        if (!_handle.IsValid || !world.IsAlive(_handle))
        {
            _handle = world.AddReflectionProbe(desc);
            _world = world;
            _last = desc;
        }
        else if (!Same(desc, _last))
        {
            world.SetReflectionProbe(_handle, desc);
            _last = desc;
        }

        if (_captureRequested)
        {
            _captureRequested = false;
            if (Mode == ReflectionProbeMode.Realtime) world.RenderReflectionProbe(_handle);
        }
        if (_bakeRequested)
        {
            _bakeRequested = false;
            world.BakeReflectionProbe(_handle, baked => SetBaked(new BakedCubemap { Size = baked.Size, Mips = baked.Mips, Faces = baked.Faces }));
        }
        return true;
    }

    private static bool Same(in ReflectionProbeDesc a, in ReflectionProbeDesc b)
        => a.Position.Equals(b.Position) && a.Rotation == b.Rotation && a.BoxSize == b.BoxSize && a.BlendDistance == b.BlendDistance
        && a.BoxProjection == b.BoxProjection && a.Importance == b.Importance && a.Intensity == b.Intensity && a.Mode == b.Mode
        && a.Refresh == b.Refresh && a.TimeSlicing == b.TimeSlicing && a.NearClip == b.NearClip && a.FarClip == b.FarClip
        && a.CullingMask == b.CullingMask && a.Resolution == b.Resolution && a.Cubemap == b.Cubemap;

    void IRenderSync.Detach(RenderWorld world) => Remove();

    private void Remove()
    {
        if (_world != null && _handle.IsValid && _world.IsAlive(_handle))
            _world.RemoveReflectionProbe(_handle);
        _handle = default;
        _world = null;
    }

    protected override void OnDispose()
    {
        Graphics.DeferDispose(_bakedCube);
        _bakedCube = null;
        _bakedShown = null;
        base.OnDispose();
    }

    /// <summary>The world position captures are taken from, the middle of the box.</summary>
    public Float3 CapturePosition => Transform.TransformPoint(BoxOffset);

    public override void DrawGizmos()
    {
        Texture2D icon = Texture2D.LoadDefault(DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, CapturePosition, 0.5f, new Color(0.6f, 0.8f, 1f, 1f));
    }

    public override void DrawGizmosSelected()
    {
        DrawBox(BoxSize * 0.5f, new Color(1f, 0.85f, 0.3f, 1f));
        float blend = MathF.Max(BlendDistance, 0f);
        if (blend > 0f)
            DrawBox(Maths.Max(BoxSize * 0.5f - new Float3(blend), Float3.Zero), new Color(0.4f, 0.7f, 1f, 1f));
        Debug.DrawWireSphere(CapturePosition, 0.25f, new Color(0.6f, 0.8f, 1f, 1f));
    }

    private void DrawBox(Float3 half, Color color)
    {
        Quaternion rotation = Transform.Rotation;
        Float3 center = CapturePosition;
        Span<Float3> c = stackalloc Float3[8];
        for (int i = 0; i < 8; i++)
            c[i] = center + rotation * new Float3((i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z);
        for (int i = 0; i < 8; i++)
            for (int axis = 1; axis < 8; axis <<= 1)
                if ((i & axis) == 0)
                    Debug.DrawLine(c[i], c[i | axis], color);
    }

    /// <summary>A prefiltered capture held as raw half float faces, every mip of every face in order.</summary>
    internal sealed class BakedCubemap : ISerializable
    {
        public int Size;
        public int Mips;
        public byte[][] Faces = [];

        public void Serialize(ref EchoObject compound, SerializationContext ctx)
        {
            compound.Add("Size", new(Size));
            compound.Add("Mips", new(Mips));
            EchoObject faces = EchoObject.NewList();
            foreach (byte[] face in Faces) faces.ListAdd(new(face));
            compound.Add("Faces", faces);
        }

        public void Deserialize(EchoObject value, SerializationContext ctx)
        {
            Size = value["Size"].IntValue;
            Mips = value["Mips"].IntValue;
            EchoObject faces = value["Faces"];
            var list = new List<byte[]>(faces.Count);
            for (int i = 0; i < faces.Count; i++) list.Add(faces[i].ByteArrayValue);
            Faces = [.. list];
        }
    }
}
