using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private const float HeroTileMinWidth = 170f;
    private const float FoldInset = 28f;

    private readonly record struct Stat(string Label, string Value, string Sub, Color? Color = null);

    private void HeroStrip(IReadOnlyList<Stat> stats, float? maxWidth = null, float valueScale = 1.6f)
    {
        float gap = Metrics.SpacingLarge;
        float width = maxWidth ?? SectionWidth(0);
        int perLine = Math.Clamp((int)((width + gap) / (HeroTileMinWidth + gap)), 1, stats.Count);
        float tileWidth = (width - gap * (perLine - 1)) / perLine;

        using (_paper.Row("sec", _sectionIndex++).Width(width).Height(UnitValue.Auto).WrapContent().Gap(gap).LineGap(gap).Enter())
            for (int i = 0; i < stats.Count; i++)
            {
                Stat stat = stats[i];
                using (Card("tile", i).Width(tileWidth).Height(UnitValue.Auto).Padding(Metrics.PaddingLarge).Gap(Metrics.SpacingSmall).Enter())
                {
                    _paper.Box("l").Height(UnitValue.Auto).Text(stat.Label, Style.Font).FontSize(Style.Body).TextColor(Style.InkSoft)
                        .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                    _paper.Box("v").Height(UnitValue.Auto).Text(stat.Value, Style.Strong).FontSize(Style.Title * valueScale).TextColor(stat.Color ?? Style.Ink)
                        .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
                    _paper.Box("s").Height(UnitValue.Auto).Text(stat.Sub, Style.Font).FontSize(Style.Small).TextColor(Style.InkDim)
                        .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                }
            }
    }

    private static Color? Versus(double value, double average)
        => average <= 0 ? null : value > average * 1.25 ? Style.Bad : value < average * 0.8 ? Style.Good : null;

    private void Collapsible(string title, Action body, bool open = false, float? width = null)
    {
        float outer = _contentWidth;
        using (_paper.Column("sec", _sectionIndex++).Width(width ?? SectionWidth(0)).Height(UnitValue.Auto).Enter())
        {
            _contentWidth = (width ?? outer) - FoldInset;
            Origami.Foldout(_paper, "fold", title).DefaultExpanded(open).Body(body);
            _contentWidth = outer;
        }
    }
}
