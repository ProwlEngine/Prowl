// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

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
public class ReflectionProbe : MonoBehaviour
{
    public ReflectionProbeMode Mode = ReflectionProbeMode.Baked;

    [ShowIf(nameof(IsRealtime))]
    public ReflectionProbeRefreshMode RefreshMode = ReflectionProbeRefreshMode.OnEnable;

    [ShowIf(nameof(IsRealtime))]
    public ReflectionProbeTimeSlicing TimeSlicing = ReflectionProbeTimeSlicing.AllFacesAtOnce;

    /// <summary>Wins over overlapping probes with a lower value, whatever their size.</summary>
    public int Importance = 1;

    public float Intensity = 1f;

    /// <summary>Size of the box the probe affects and projects reflections onto, turned with the object.</summary>
    [Header("Box")]
    public Float3 BoxSize = new(10f, 10f, 10f);

    /// <summary>Where the box sits relative to the object, which is where the capture is taken from.</summary>
    public Float3 BoxOffset = Float3.Zero;

    /// <summary>How far in from the box faces reflections fade in, so neighbouring probes blend.</summary>
    public float BlendDistance = 1f;

    /// <summary>Projects reflections onto the box rather than treating them as infinitely far away.</summary>
    public bool BoxProjection = true;

    [Header("Capture")]
    public float NearClip = 0.1f;
    public float FarClip = 1000f;
    public LayerMask CullingMask = LayerMask.Everything;

    [SerializeField, HideInInspector] private BakedCubemap? _baked;

    private bool IsRealtime => Mode == ReflectionProbeMode.Realtime;

    /// <summary>Whether a baked capture is saved with the probe.</summary>
    public bool HasBakedData => _baked != null;

    internal BakedCubemap? Baked => _baked;

    /// <summary>Set by <see cref="RenderProbe"/>, cleared when the capture starts.</summary>
    internal bool CaptureRequested { get; set; }

    /// <summary>Set by <see cref="Bake"/>, cleared when the bake is taken.</summary>
    internal bool BakeRequested { get; set; }

    /// <summary>Bumped when the baked capture changes, so the probe system uploads it again.</summary>
    internal int BakedVersion { get; private set; }

    private ReflectionProbeSystem? _system;

    public override void OnEnable() => JoinScene(GameObject.Scene);

    public override void OnDisable() => LeaveScene(GameObject.Scene);

    internal override void JoinScene(Scene? scene)
    {
        _system = scene.IsValid() ? scene!.ReflectionProbes : null;
        _system?.Register(this);
        if (IsRealtime) CaptureRequested = true;
    }

    internal override void LeaveScene(Scene? scene)
    {
        _system?.Unregister(this);
        _system = null;
    }

    /// <summary>Asks a realtime probe to capture again. It happens before the next frame renders.</summary>
    public void RenderProbe() => CaptureRequested = true;

    /// <summary>Captures the probe before the next frame renders and keeps the result with the scene.</summary>
    [Button("Bake")]
    public void Bake() => BakeRequested = true;

    internal void SetBaked(BakedCubemap? baked)
    {
        _baked = baked;
        BakedVersion++;
    }

    [Button("Clear Baked")]
    public void ClearBaked() => SetBaked(null);

    /// <summary>The world position captures are taken from, the middle of the box.</summary>
    public Float3 CapturePosition => Transform.TransformPoint(BoxOffset);

    public override void DrawGizmos()
    {
        var icon = Texture2D.LoadDefault(DefaultTexture.IconLight);
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
            Faces = list.ToArray();
        }
    }
}
