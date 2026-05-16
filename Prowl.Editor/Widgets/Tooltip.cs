// Thin wrapper that delegates to Origami's TooltipSystem.

using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.Widgets;

public static class Tooltip
{
    public static void Draw(Paper paper) => TooltipSystem.Draw(paper);
}
