// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;

/// <summary>
/// Style values as they travel from a setter, through templates, through transitions, and out as
/// the value layout and rendering read. All of that now moves typed rather than boxed, so these
/// check the journey rather than any one step: what goes in is what comes out, a template carries
/// only what it declared, and an animation still lands where it used to.
/// </summary>
public class StyleValueTests
{
    [Fact]
    public void A_value_survives_the_round_trip_of_every_kind()
    {
        var paper = NewPaper();
        paper.BeginFrame(Step);
        var b = paper.Box("e")
            .Width(123)                     // UnitValue
            .BorderWidth(4.5f)              // float
            .BackgroundColor(Color.Red)     // Color
            .Rounded(1, 2, 3, 4)            // Float4
            .TabSize(7)                     // int
            .Translate(11, 22);             // two floats
        paper.EndFrame();

        var style = Style(paper, b);
        Assert.Equal(123f, style.GetUnit(GuiProp.Width).Px, 3);
        Assert.Equal(4.5f, style.GetBorderWidth(), 3);
        Assert.Equal(Color.Red, style.GetBackgroundColor());
        Assert.Equal(new Float4(1, 2, 3, 4), style.GetRounded());
        Assert.Equal(7, (int)style.GetValue(GuiProp.TabSize));
        Assert.Equal(11f, (float)style.GetValue(GuiProp.TranslateX), 3);
        Assert.Equal(22f, (float)style.GetValue(GuiProp.TranslateY), 3);
    }

    /// <summary>Anything the template did not declare is left alone on the element.</summary>
    [Fact]
    public void A_template_applies_what_it_declared_and_nothing_else()
    {
        var template = new StyleTemplate().BackgroundColor(Color.Blue).BorderWidth(3f);

        var paper = NewPaper();
        paper.BeginFrame(Step);
        var b = paper.Box("e").Width(200).Style(template);
        paper.EndFrame();

        var style = Style(paper, b);
        Assert.Equal(Color.Blue, style.GetBackgroundColor());
        Assert.Equal(3f, style.GetBorderWidth(), 3);
        Assert.Equal(200f, style.GetUnit(GuiProp.Width).Px, 3);   // declared on the element, untouched
    }

    [Fact]
    public void A_cloned_template_does_not_share_with_its_original()
    {
        var original = new StyleTemplate().BorderWidth(1f);
        var clone = original.Clone();
        clone.BorderWidth(9f);

        Assert.Equal(1f, AppliedBorderWidth(original), 3);
        Assert.Equal(9f, AppliedBorderWidth(clone), 3);
    }

    [Fact]
    public void One_template_applied_to_another_carries_its_values_over()
    {
        var source = new StyleTemplate().BorderWidth(5f).BackgroundColor(Color.Green);
        var destination = new StyleTemplate().BorderWidth(1f);
        source.ApplyTo(destination);

        var paper = NewPaper();
        paper.BeginFrame(Step);
        var b = paper.Box("e").Style(destination);
        paper.EndFrame();

        var style = Style(paper, b);
        Assert.Equal(5f, style.GetBorderWidth(), 3);
        Assert.Equal(Color.Green, style.GetBackgroundColor());
    }

    /// <summary>A transition does not animate out of nowhere: the first frame it sees snaps.</summary>
    [Fact]
    public void A_transition_snaps_on_the_frame_it_first_sees_a_property()
    {
        var paper = NewPaper();
        Assert.Equal(10f, RunBorder(paper, 10f), 3);
    }

    [Fact]
    public void A_transition_moves_a_float_towards_its_target_over_time()
    {
        var paper = NewPaper();
        RunBorder(paper, 0f);                      // establish the starting value

        float quarter = RunBorder(paper, 100f);    // one step of a four-step transition
        Assert.True(quarter > 0f && quarter < 100f, $"expected a value in flight, got {quarter}");

        float half = RunBorder(paper, 100f);
        Assert.True(half > quarter, $"expected {half} to be past {quarter}");

        RunBorder(paper, 100f);
        RunBorder(paper, 100f);
        Assert.Equal(100f, RunBorder(paper, 100f), 2);   // arrived, and stays
    }

    [Fact]
    public void A_transition_interpolates_a_colour_channel_by_channel()
    {
        var paper = NewPaper();
        RunColor(paper, new Color(0f, 0f, 0f, 1f));

        Color mid = RunColor(paper, new Color(1f, 1f, 1f, 1f));
        Assert.True(mid.R > 0f && mid.R < 1f, $"expected a colour in flight, got {mid.R}");
        Assert.Equal(mid.R, mid.G, 5);
        Assert.Equal(mid.G, mid.B, 5);
    }

    /// <summary>Retargeting mid-flight continues from where it had got to, not from the old start.</summary>
    [Fact]
    public void A_transition_retargeted_in_flight_carries_on_from_where_it_is()
    {
        var paper = NewPaper();
        RunBorder(paper, 0f);

        float inFlight = RunBorder(paper, 100f);
        float afterRetarget = RunBorder(paper, 0f);

        Assert.True(afterRetarget < inFlight, $"expected {afterRetarget} to head back below {inFlight}");
        Assert.True(afterRetarget > 0f, "it should not have snapped straight back to the start");
    }

    private const float Step = 0.25f;
    private const float Duration = 1f;

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    private static ElementStyle Style(Paper paper, ElementBuilder b)
        => paper.FindElementHandleByID(b._handle.Data.ID).Data._elementStyle;

    private static float RunBorder(Paper paper, float target)
    {
        paper.BeginFrame(Step);
        var b = paper.Box("anim").Transition(GuiProp.BorderWidth, Duration).BorderWidth(target);
        paper.EndFrame();
        return Style(paper, b).GetBorderWidth();
    }

    private static Color RunColor(Paper paper, Color target)
    {
        paper.BeginFrame(Step);
        var b = paper.Box("anim").Transition(GuiProp.BackgroundColor, Duration).BackgroundColor(target);
        paper.EndFrame();
        return Style(paper, b).GetBackgroundColor();
    }

    private static float AppliedBorderWidth(StyleTemplate template)
    {
        var paper = NewPaper();
        paper.BeginFrame(Step);
        var b = paper.Box("e").Style(template);
        paper.EndFrame();
        return Style(paper, b).GetBorderWidth();
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint width, uint height) => new Int2((int)width, (int)height);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) { }
    }
}
