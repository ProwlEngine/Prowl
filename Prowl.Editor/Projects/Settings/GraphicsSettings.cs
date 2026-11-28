using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Graphite.Rendering;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for how every camera draws unless it overrides them. </summary>
[ProjectSettings("Graphics", EditorIcons.Display, order: 24)]
public class GraphicsSettings : ProjectSettingsBase
{
    /// <summary> How cameras light opaque surfaces unless they override it. </summary>
    public RenderPath RenderingPath = RenderPath.Deferred;

    /// <summary> Which surfaces deferred cameras draw depth for before their GBuffer unless they override it. </summary>
    public EarlyDepth EarlyDepth = EarlyDepth.All;

    /// <summary> Whether cameras render into 16 bit float colour unless they override it. </summary>
    public bool HDR = true;

    /// <summary> The vsync a game starts with. </summary>
    public VSyncMode VSync = VSyncMode.Mailbox;

    /// <summary> The frame rate a game starts paced to, 0 for unlimited. </summary>
    public int TargetFrameRate;

    /// <summary> Whether cameras skip what is hidden behind what they saw last frame. </summary>
    public bool OcclusionCulling = true;

    /// <summary> The illuminance in lux where a local light's automatic range ends. </summary>
    public float LightCutoff = 0.25f;

    /// <summary> What cameras sort transparent renderers back to front by. </summary>
    public TransparencySortMode TransparencySortMode = TransparencySortMode.Default;

    /// <summary> The axis the custom axis mode sorts along, renderers further along it drawn first. </summary>
    public Float3 TransparencySortAxis = new(0, 1, 0);

    /// <summary> The precision of HDR colour on cameras that keep no alpha, and of the GBuffer's baked and emission targets. </summary>
    public ColorPrecision ColorPrecision = ColorPrecision.Compact;

    /// <summary> The depth precision of shadow maps. </summary>
    public DepthPrecision ShadowPrecision = DepthPrecision.Depth16;

    /// <summary> The precision of reflection probe and sky probe cubes. </summary>
    public ColorPrecision ReflectionPrecision = ColorPrecision.Full;

    /// <summary> How every scene's local light shadows are stored and sized. </summary>
    public ShadowSettings Shadows = new();

    /// <summary> Every scene's LOD rules. </summary>
    public LodSettings Lod = new();

    public override void Apply()
    {
        Runtime.Graphics.RenderingPath = RenderingPath;
        Runtime.Graphics.EarlyDepth = EarlyDepth;
        Runtime.Graphics.HDR = HDR;
        Runtime.Graphics.OcclusionCulling = OcclusionCulling;
        Runtime.Graphics.LightCutoff = LightCutoff;
        Runtime.Graphics.TransparencySort = new TransparencySort(TransparencySortMode, TransparencySortAxis);
        Runtime.Graphics.ColorPrecision = ColorPrecision;
        Runtime.Graphics.ShadowPrecision = ShadowPrecision;
        Runtime.Graphics.ReflectionPrecision = ReflectionPrecision;
        Runtime.Graphics.Shadows = Shadows;
        Runtime.Graphics.Lod = Lod;

        Application.GameVSync = VSync;
        Application.GameTargetFrameRate = TargetFrameRate;
        if (Application.IsPlaying)
        {
            Application.VSync = VSync;
            Application.TargetFrameRate = TargetFrameRate;
        }
    }

    public override void ResetToDefaults()
    {
        RenderingPath = RenderPath.Deferred;
        EarlyDepth = EarlyDepth.All;
        HDR = true;
        VSync = VSyncMode.Mailbox;
        TargetFrameRate = 0;
        OcclusionCulling = true;
        LightCutoff = 0.25f;
        ColorPrecision = ColorPrecision.Compact;
        ShadowPrecision = DepthPrecision.Depth16;
        ReflectionPrecision = ColorPrecision.Full;
        TransparencySortMode = TransparencySortMode.Default;
        TransparencySortAxis = new Float3(0, 1, 0);
        Shadows = new ShadowSettings();
        Lod = new LodSettings();
    }

    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "gfx_cam_hdr", $"{EditorIcons.Camera}  Cameras").Underline().Show();
        EditorGUI.SettingsEnumDropdown(paper, "gfx_path", "Rendering Path", RenderingPath, v => { RenderingPath = v; Changed(); },
            "How cameras light opaque surfaces unless they override it.\n\n" +
            "Deferred: surfaces are drawn into a GBuffer and each pixel is lit once. Screen space reflections, ambient occlusion and global illumination need it.\n\n" +
            "Forward: surfaces are lit as they draw. Cheaper with few lights and no screen effects.");
        if (RenderingPath == RenderPath.Deferred)
            EditorGUI.SettingsEnumDropdown(paper, "gfx_early", "Early Depth", EarlyDepth, v => { EarlyDepth = v; Changed(); },
                "Which surfaces deferred cameras draw depth for before the GBuffer pass.\n\n" +
                "All: every surface, so hidden pixels never run the GBuffer pass. Occlusion culling needs it.\n\n" +
                "Alpha Cut: only alpha cut surfaces, whose hidden layers would each run the GBuffer pass to be cut out.\n\n" +
                "None: no depth pass, the GBuffer pass draws every surface alone.");
        EditorGUI.SettingsToggle(paper, "gfx_hdr", "HDR", HDR, v => { HDR = v; Changed(); },
            tooltip: "On: cameras render into float colour that a Tonemapping volume maps to the display.\n\n" +
            "Off: cameras render into 8 bit sRGB, where light above 1 clips and bloom and exposure lose their range.");

        Origami.Header(paper, "gfx_fmt_hdr", $"{EditorIcons.Cubes}  Formats").Underline().Show();
        EditorGUI.SettingsEnumDropdown(paper, "gfx_color_precision", "HDR Color", ColorPrecision, v => { ColorPrecision = v; Changed(); },
            "The format of HDR colour on cameras that keep no alpha, and of the GBuffer's baked light and emission targets.\n\n" +
            "Compact: R11G11B10 float, 4 bytes a pixel. 6 bits of mantissa for red and green and 5 for blue, so very smooth dark gradients can band and colours shift slightly. No alpha.\n\n" +
            "Full: RGBA16 float, 8 bytes a pixel. 10 bits of mantissa a channel.\n\n" +
            "Cameras that keep alpha always use RGBA16 float. Changes apply next frame.");
        EditorGUI.SettingsEnumDropdown(paper, "gfx_shadow_precision", "Shadow Depth", ShadowPrecision, v => { ShadowPrecision = v; Changed(); },
            "The depth format of shadow maps: local light atlas pages, directional cascades and the voxel shadow.\n\n" +
            "Depth16: 16 bit unsigned normalized, half the memory and bandwidth. Enough for most scenes, but long shadow ranges need more bias and can show acne or detached shadows.\n\n" +
            "Depth32: 32 bit float, precise at any range, twice the memory.\n\n" +
            "Shadows draw again when this changes.");
        EditorGUI.SettingsEnumDropdown(paper, "gfx_reflection_precision", "Reflection Probes", ReflectionPrecision, v => { ReflectionPrecision = v; Changed(); },
            "The format of reflection probe and sky probe cubes.\n\n" +
            "Full: RGBA16 float, 8 bytes a texel.\n\n" +
            "Compact: R11G11B10 float, half the memory. Less precise blue, and smooth bright reflections can band slightly.\n\n" +
            "Realtime probes and the sky capture again when this changes. Baked probes keep the format they were baked in.");

        Origami.Header(paper, "gfx_pace_hdr", $"{EditorIcons.Gauge}  Frame Pacing").Underline().Show();
        EditorGUI.SettingsEnumDropdown(paper, "gfx_vsync", "VSync", VSync, v => { VSync = v; Changed(); },
            "How a built game, and play mode, waits for the display before showing a frame. Game code can change it with Application.VSync.\n\n" +
            "Mailbox: renders uncapped and shows the newest finished frame at each refresh, without tearing.\n\n" +
            "On: waits for each refresh. Never tears, a late frame waits for the next refresh.\n\n" +
            "Adaptive: waits for each refresh, but a late frame shows at once and may tear.\n\n" +
            "Off: shows each frame as soon as it is done. Lowest latency, and it can tear.");
        Int("gfx_fps", "Target Frame Rate", TargetFrameRate, v => TargetFrameRate = Math.Max(0, v),
            "The frame rate a built game, and play mode, starts paced to. 0 runs unlimited. A ceiling, so while VSync holds frames to the refresh the lower wins. Game code can change it with Application.TargetFrameRate.");

        Origami.Header(paper, "gfx_cull_hdr", $"{EditorIcons.EyeSlash}  Culling").Underline().Show();
        EditorGUI.SettingsToggle(paper, "gfx_occlusion", "Occlusion Culling", OcclusionCulling, v => { OcclusionCulling = v; Changed(); },
            tooltip: "Cameras skip renderables hidden behind what they saw last frame, tested on the GPU against a depth pyramid.\n\n" +
            "Pays off where walls, buildings or terrain hide much of the scene, and costs a little where little is hidden. Deferred cameras need Early Depth All.");

        Origami.Header(paper, "gfx_light_hdr", $"{EditorIcons.Lightbulb}  Lights").Underline().Show();
        Float("gfx_cutoff", "Light Cutoff (lux)", LightCutoff, v => LightCutoff = MathF.Max(0.001f, v),
            "The illuminance where a local light's automatic range ends. Higher cuts lights shorter, so fewer pixels and shadows pay for them. Lower lets dim light reach further. Lights that set their range by hand ignore it.");

        Origami.Header(paper, "gfx_shadow_hdr", $"{EditorIcons.Moon}  Local Light Shadows").Underline().Show();
        Int("gfx_shadow_page", "Page Size", Shadows.PageSize, v => Shadows.PageSize = (int)Math.Clamp(System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(v, 1024)), 1024, 16384),
            "The size of the two square atlas pages local lights draw their shadows into, one for live tiles and one for static copies. Rounded to a power of two.\n\n" +
            "Larger fits more and sharper shadows at the square of the size in memory: an 8192 page is 128 MB at Depth16, 256 MB at Depth32.");
        Float("gfx_shadow_texels", "Texels Per Pixel", Shadows.TexelsPerPixel, v => Shadows.TexelsPerPixel = MathF.Max(0.05f, v),
            "Shadow map texels per screen pixel a light covers, before rounding to a power of two. Higher is sharper and takes more of the atlas.");
        Float("gfx_shadow_distance", "Shadow Distance", Shadows.LocalDistance, v => Shadows.LocalDistance = MathF.Max(0f, v),
            "Local lights farther than this from every camera cast no shadow, fading out over the last tenth.");
        Float("gfx_shadow_radius", "Min Screen Radius", Shadows.MinScreenRadius, v => Shadows.MinScreenRadius = MathF.Max(0f, v),
            "Local lights smaller on screen than this radius in pixels cast no shadow, fading in up to twice it.");
        Float("gfx_shadow_budget", "Draw Budget (ms)", Shadows.DrawMilliseconds, v => Shadows.DrawMilliseconds = MathF.Max(0f, v),
            "Milliseconds a frame may spend preparing local light shadow draws, the most important lights first and always at least one. Lights left waiting keep their last shadow.");

        Origami.Header(paper, "gfx_lod_hdr", $"{EditorIcons.Shapes}  LOD").Underline().Show();
        EditorGUI.SettingsEnumDropdown(paper, "gfx_lod_transition", "Transition", Lod.Transition, v => { Lod.Transition = v; Changed(); },
            "How cameras move a renderable from one level of detail to the next.\n\n" +
            "Cross Fade: both levels draw for the fade time, dithered into each other. Needs the camera's depth prepass.\n\n" +
            "Pop: the new level replaces the old at once, the cheapest.\n\n" +
            "Shadows, probe captures and voxels always pop.");
        if (Lod.Transition == LodTransition.CrossFade)
            Float("gfx_lod_fade", "Fade Seconds", Lod.FadeSeconds, v => Lod.FadeSeconds = MathF.Max(0f, v),
                "How long a cross fade between two levels takes.");
        Float("gfx_lod_bias", "Bias", Lod.Bias, v => Lod.Bias = MathF.Max(0.01f, v),
            "Multiplies every renderable's screen size when picking its level. Above 1 keeps detail longer, below 1 drops to coarser levels sooner.");
        Float("gfx_lod_hysteresis", "Hysteresis", Lod.Hysteresis, v => Lod.Hysteresis = Math.Clamp(v, 0f, 0.9f),
            "The share of a level's screen size a renderable must fall under before moving coarser, so one sitting near a threshold does not flicker between levels.");

        Origami.Header(paper, "gfx_sort_hdr", $"{EditorIcons.LayerGroup}  Transparency Sort").Underline().Show();

        List<TransparencySortMode> modes = [.. Enum.GetValues<TransparencySortMode>()];
        EditorGUI.Row(paper, "gfx_sort_mode", "Mode", () =>
            Origami.Dropdown(paper, "gfx_sort_mode_dd", TransparencySortMode, v => { TransparencySortMode = v; Changed(); }, modes).Show(),
            tooltip: "What cameras sort transparent renderers back to front by unless they override it.\n\n" +
            "Default: distance from the camera, or depth along the view for orthographic cameras.\n\n" +
            "Orthographic: depth along the view for every camera.\n\n" +
            "Custom Axis: position along the axis below, furthest first. Suits 2D and isometric games.");

        if (TransparencySortMode == TransparencySortMode.CustomAxis)
        {
            EditorGUI.Row(paper, "gfx_sort_axis", "Axis", () =>
            {
                using (paper.Row("gfx_sort_axis_row").Gap(4).Enter())
                {
                    Axis("x", TransparencySortAxis.X, v => TransparencySortAxis.X = v);
                    Axis("y", TransparencySortAxis.Y, v => TransparencySortAxis.Y = v);
                    Axis("z", TransparencySortAxis.Z, v => TransparencySortAxis.Z = v);
                }
            }, tooltip: "The axis Custom Axis sorts along. Renderers further along it draw first.");
        }

        void Axis(string id, float value, Action<float> set)
            => Origami.NumericField<float>(paper, $"gfx_sort_axis_{id}", value, v => { set(v); Apply(); })
                .OnCommit(EditorRegistries.SaveSettings).Show();

        void Float(string id, string label, float value, Action<float> set, string tooltip)
            => EditorGUI.Row(paper, id, label, () =>
                Origami.NumericField<float>(paper, $"{id}_v", value, v => { set(v); Apply(); }).OnCommit(EditorRegistries.SaveSettings).Show(), tooltip: tooltip);

        void Int(string id, string label, int value, Action<int> set, string tooltip)
            => EditorGUI.Row(paper, id, label, () =>
                Origami.NumericField<int>(paper, $"{id}_v", value, v => { set(v); Apply(); }).OnCommit(EditorRegistries.SaveSettings).Show(), tooltip: tooltip);
    }
}
