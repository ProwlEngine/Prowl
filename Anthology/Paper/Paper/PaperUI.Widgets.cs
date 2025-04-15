using System.Numerics;
/*
namespace Prowl.PaperUI
{
    public static partial class ImGui
    {
        public static void ScrollView(string id, ref Vector2 scrollPosition, bool horizontalScroll, bool verticalScroll, Action content)
        {
            //using (ImGui.LayoutBox("RootScrollView_" + id)
            //    .Width(ImGui.Stretch())
            //    .Height(ImGui.Stretch())
            //    .Style(BoxStyle.Solid(Color.Blue))
            //    .Enter())
            //{
            //    // Lets just start with Vertical scroll
            //    if (verticalScroll)
            //    {
            //        using (ImGui.LayoutBox("VScrollBar")
            //            .PositionType(Morphorm.PositionType.SelfDirected)
            //            .Left(ImGui.Percent(100, -20))
            //            .Width(20)
            //            .Height(ImGui.Percent(100, horizontalScroll ? -20 : 0))
            //            .Style(BoxStyle.Solid(Color.RebeccaPurple))
            //            .Enter())
            //        {
            //        }
            //    }
            //
            //    if (horizontalScroll)
            //    {
            //        using (ImGui.LayoutBox("HScrollBar")
            //            .PositionType(Morphorm.PositionType.SelfDirected)
            //            .Top(ImGui.Percent(100, -20))
            //            .Height(20)
            //            .Width(ImGui.Percent(100, verticalScroll ? -20 : 0))
            //            .Style(BoxStyle.Solid(Color.Yellow))
            //            .Enter())
            //        {
            //        }
            //    }
            //
            //    // Content View
            //    using (ImGui.LayoutBox("ContentView")
            //        .PositionType(Morphorm.PositionType.SelfDirected)
            //        .Height(ImGui.Percent(100, horizontalScroll ? -20 : 0))
            //        .Width(ImGui.Percent(100, verticalScroll ? -20 : 0))
            //        .Style(BoxStyle.Solid(Color.Green))
            //        .Clip()
            //        .Enter())
            //    {
            //        // Content
            //        using (ImGui.LayoutBox("Content")
            //            .PositionType(Morphorm.PositionType.SelfDirected)
            //            .Top(-scrollPosition.Y)
            //            .Left(scrollPosition.X)
            //            .Height(verticalScroll ? ImGui.Auto : ImGui.Stretch())
            //            .Width(horizontalScroll ? ImGui.Auto : ImGui.Stretch())
            //            .Style(BoxStyle.Solid(Color.Red))
            //            .Enter())
            //        {
            //            content.Invoke();
            //        }
            //
            //    }
            //}
        }

            //public static LayoutBuilder ScrollView(string stringID, ref Vector2 scrollPosition, bool horizontal = false, bool vertical = true)
            //{
            //// Create the outer container (viewport)
            //var scrollView = ImGui.LayoutBox(stringID)
            //    .Style(BoxStyle.Solid(Color.Transparent))
            //    .Clip() // Clip contents to viewport
            //    .OnScroll(delta => {
            //        // Handle scroll wheel input
            //        if (vertical)
            //            scrollPosition.Y -= delta * 20; // Adjust scroll speed as needed
            //        else if (horizontal)
            //            scrollPosition.X -= delta * 20;
            //    })
            //    .OnDrag(delta => {
            //        // Drag to scroll
            //        if (vertical)
            //            scrollPosition.Y -= delta.Y;
            //        if (horizontal)
            //            scrollPosition.X -= delta.X;
            //    });
            //
            //return scrollView;
            //}
        }
}

 */