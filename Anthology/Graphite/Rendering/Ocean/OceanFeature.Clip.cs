using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public enum OceanClipShape
{
    /// <summary>Cuts the water inside a sphere of diameter <see cref="OceanClipInput.Size"/>'s x.</summary>
    Sphere,

    /// <summary>Cuts the water inside a box.</summary>
    Box,

    /// <summary>Brings back the water over a rectangle in x and z, under inputs before it.</summary>
    IncludeArea,

    /// <summary>Cuts the water over a rectangle in x and z.</summary>
    RemoveArea,
}

/// <summary>
/// A shape that cuts the ocean's surface away: water inside it is not drawn, so a hull
/// stays dry. Only the surface is cut, not the underwater fog. Inputs apply in order.
/// </summary>
public sealed class OceanClipInput
{
    public OceanClipShape Shape { get; set; } = OceanClipShape.Box;
    public Double3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>The shape's full size along its own axes in metres.</summary>
    public Float3 Size { get; set; } = new(1f, 1f, 1f);
}

public sealed partial class OceanFeature
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ClipPush
    {
        public Float4 Sizes, Depth, Shore, Tile, Centre, AxisX, AxisY, AxisZ;
    }

    private const float ClipInput = 0f, ClipHeight = 1f, ClipClear = 2f;

    private InternalCompute _clipProgram = null!;
    private readonly List<ClipPush> _clipDraws = new();
    private ClipPush _clipBase;
    private bool _clipCleared;

    /// <summary>The cut on the depth's slices, the surface cut where red is over a half, the water's height in green.</summary>
    public Texture Clip { get; private set; } = null!;

    /// <summary>The shapes cutting the surface. Host thread.</summary>
    public List<OceanClipInput> ClipInputs { get; } = new();

    /// <summary>
    /// The pass over the depth's slices that reads the finished waves, drawing <see cref="ClipInputs"/> and stepping the
    /// <see cref="Whitecaps"/>, for the ocean's world's frame features beside this one.
    /// </summary>
    public FrameFeature LocalPass { get; private set; } = null!;

    private void CreateClip(Renderer renderer)
    {
        _clipProgram = renderer.Effects.Compute("Internal/OceanClip.glsl");
        Clip = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, DepthSlices, PixelFormat.R16_G16_Float, TextureUsage.Storage | TextureUsage.Sampled));
        Clip.Name = "Ocean Clip";
        LocalPass = new LocalFeature(this, renderer.Resources.LinearRepeat, renderer.Resources.LinearClamp);
    }

    private void ExtractClip()
    {
        Double3 viewer = Viewer;
        double largest = CascadeSize(0);
        float x = (float)(viewer.X - Math.Floor(viewer.X / largest) * largest), z = (float)(viewer.Z - Math.Floor(viewer.Z / largest) * largest);
        _clipBase = new ClipPush
        {
            Sizes = new Float4(CascadeSize(0), CascadeSize(1), CascadeSize(2), CascadeSize(3)),
            Depth = DepthParams,
            Shore = new Float4(Math.Clamp(ShallowAttenuation, 0f, 1f), DepthSlices, ClipInput, Resolution),
            Tile = new Float4(x, z, 0f, 0f),
        };

        _clipDraws.Clear();
        foreach (OceanClipInput input in ClipInputs)
        {
            Quaternion rotation = input.Rotation;
            Float3 half = Maths.Abs(input.Size) * 0.5f;
            Float3 right = rotation * new Float3(1f, 0f, 0f), up = rotation * new Float3(0f, 1f, 0f), forward = rotation * new Float3(0f, 0f, 1f);
            Double3 at = input.Position;
            _clipDraws.Add(_clipBase with
            {
                Tile = _clipBase.Tile with { Z = (int)input.Shape },
                Centre = new Float4((float)(at.X - viewer.X), (float)(at.Y - SeaLevel), (float)(at.Z - viewer.Z), half.X),
                AxisX = new Float4(right.X, right.Y, right.Z, half.X),
                AxisY = new Float4(up.X, up.Y, up.Z, half.Y),
                AxisZ = new Float4(forward.X, forward.Y, forward.Z, half.Z),
            });
        }
    }

    // Finds the water's height under every texel, then draws each input over it, or only clears once when there are none
    private void RenderClip(CommandBuffer cmd)
    {
        if (_clipDraws.Count == 0 && _clipCleared)
            return;
        uint groups = (DepthResolution + 7) / 8;
        cmd.SetPushConstants(_clipBase with { Shore = _clipBase.Shore with { Z = _clipDraws.Count == 0 ? ClipClear : ClipHeight } });
        cmd.Dispatch(groups, groups, DepthSlices);
        cmd.ComputeBarrier();
        foreach (ClipPush draw in _clipDraws)
        {
            cmd.SetPushConstants(draw);
            cmd.Dispatch(groups, groups, DepthSlices);
            cmd.ComputeBarrier();
        }
        _clipCleared = _clipDraws.Count == 0;
    }

    // A pass of its own after the waves, so it reads their finished, mipmapped fields
    private sealed class LocalFeature(OceanFeature owner, Sampler waves, Sampler slices) : FrameFeature(FrameSlot.BeforeViews, order: 5)
    {
        private readonly PropertySet _properties = new();

        public override void Extract()
        {
            owner.ExtractClip();
            owner.ExtractWhitecaps();
        }

        public override void Setup(FrameBuilder builder)
        {
            builder.Read(owner.Displacement);
            builder.Read(owner.Slopes);
            builder.Read(owner.Depth);
            builder.Read(owner.Levels);
            builder.Read(owner.Ripples);
            builder.ExportToDraws(builder.Write(owner.Clip));
            builder.ExportToDraws(builder.Write(owner._whitecaps[0]));
            builder.Write(owner._whitecaps[1]);
        }

        public override void Render(FrameContext context)
        {
            CommandBuffer cmd = context.Command;
            cmd.SetComputeShader(owner._clipProgram.Program);
            _properties.Clear();
            _properties.SetTexture("Ocean_Displacement", owner.Displacement, waves);
            _properties.SetTexture("Ocean_Slopes", owner.Slopes, waves);
            _properties.SetTexture("Ocean_Depth", owner.Depth, slices);
            _properties.SetTexture("Ocean_Levels", owner.Levels, slices);
            _properties.SetTexture("Ocean_Clip", owner.Clip);
            _properties.SetTexture("Ocean_Ripples", owner.Ripples, slices);
            cmd.SetProperties(_properties);
            owner.RenderClip(cmd);

            cmd.SetComputeShader(owner._whitecapProgram.Program);
            owner.RenderWhitecaps(cmd, _properties, context.Time.DeltaTime * owner._timeScale);
        }
    }
}
