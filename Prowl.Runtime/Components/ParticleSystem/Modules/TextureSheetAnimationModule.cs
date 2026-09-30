// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum TextureSheetAnimation
{
    /// <summary>Plays every frame, left to right, top to bottom.</summary>
    WholeSheet,
    /// <summary>Plays the frames of one row.</summary>
    SingleRow
}

public enum TextureSheetTimeMode
{
    /// <summary>Frame follows <see cref="TextureSheetAnimationModule.FrameOverTime"/> across the particle's life.</summary>
    Lifetime,
    /// <summary>Frame follows the particle's speed across <see cref="TextureSheetAnimationModule.SpeedRange"/>.</summary>
    Speed,
    /// <summary>A fixed number of frames per second.</summary>
    FPS
}

/// <summary>
/// Plays a sprite sheet on each particle. Frame 0 is the top left tile. The frame is passed to the
/// shader, which also blends toward the next frame when <see cref="FrameBlending"/> is on.
/// </summary>
[Serializable]
public class TextureSheetAnimationModule : ParticleSystemModule
{
    public int TilesX = 1;
    public int TilesY = 1;

    public TextureSheetAnimation Animation = TextureSheetAnimation.WholeSheet;
    [ShowIf(nameof(IsSingleRow)), Tooltip("Each particle plays a random row.")]
    public bool RandomRow = true;
    [ShowIf(nameof(UsesRowIndex))]
    public int RowIndex = 0;

    public TextureSheetTimeMode TimeMode = TextureSheetTimeMode.Lifetime;
    [ShowIf(nameof(IsLifetime)), Tooltip("0 to 1 across the frames, over each cycle.")]
    public MinMaxCurve FrameOverTime = new(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
    [ShowIf(nameof(IsSpeed)), Tooltip("Speeds that map to the first and last frame.")]
    public Float2 SpeedRange = new(0f, 1f);
    [ShowIf(nameof(IsFps))]
    public float FPS = 30f;

    [Tooltip("Frame to start on.")]
    public MinMaxCurve StartFrame = new(0f);
    [Tooltip("How many times the animation plays over the particle's life.")]
    public int Cycles = 1;
    [Tooltip("Blends smoothly between frames.")]
    public bool FrameBlending = false;

    private bool IsSingleRow => Animation == TextureSheetAnimation.SingleRow;
    private bool UsesRowIndex => IsSingleRow && !RandomRow;
    private bool IsLifetime => TimeMode == TextureSheetTimeMode.Lifetime;
    private bool IsSpeed => TimeMode == TextureSheetTimeMode.Speed;
    private bool IsFps => TimeMode == TextureSheetTimeMode.FPS;

    private int Columns => Math.Max(1, TilesX);
    private int Rows => Math.Max(1, TilesY);

    /// <summary>Frames in one cycle: the whole sheet or a single row.</summary>
    internal int FramesPerCycle => IsSingleRow ? Columns : Columns * Rows;

    /// <summary>
    /// Tiles, frames per cycle and blending, for the shader. Blending is 1 to wrap into the first frame, or
    /// 2 when the animation plays once over the particle's life and must hold its last frame.
    /// </summary>
    internal Float4 ShaderParams => Enabled && Columns * Rows > 1
        ? new Float4(Columns, Rows, FramesPerCycle, !FrameBlending ? 0f : PlaysOnce ? 2f : 1f)
        : new Float4(1f, 1f, 1f, 0f);

    private bool PlaysOnce => TimeMode == TextureSheetTimeMode.Lifetime && Cycles <= 1;

    internal void Apply(ref Particle p, float age, float speed)
    {
        int frames = FramesPerCycle;
        if (Columns * Rows <= 1)
        {
            p.UVFrame = 0f;
            return;
        }

        int cycles = Math.Max(1, Cycles);
        float position = TimeMode switch
        {
            TextureSheetTimeMode.Speed => MathF.Min(ColorBySpeedModule.SpeedTime(SpeedRange, speed) * cycles * frames, cycles * frames - 0.0001f),
            TextureSheetTimeMode.FPS => p.Age * FPS,
            _ => MathF.Min(FrameOverTime.Evaluate(Cycle(age * cycles), p.Random(0x131)) * frames, frames - 0.0001f),
        };

        float frame = position + StartFrame.Evaluate(age, p.Random(0x132));
        frame -= MathF.Floor(frame / frames) * frames;
        frame = Maths.Clamp(frame, 0f, frames - 0.0001f);

        if (IsSingleRow)
        {
            int row = RandomRow ? (int)(p.Random(0x133) * Rows) : RowIndex;
            frame += Math.Clamp(row, 0, Rows - 1) * Columns;
        }

        p.UVFrame = frame;
    }

    // Wraps a cycle count into 0..1 but keeps the very end of each cycle at 1, not 0.
    private static float Cycle(float t)
    {
        if (t <= 0f) return 0f;
        float f = t - MathF.Floor(t);
        return f == 0f ? 1f : f;
    }
}
