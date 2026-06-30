using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Shared;

/// <summary>
/// A scroll view built from Paper primitives — no built-in scroll support needed.
/// Content is a SelfDirected column offset by scroll position, clipped to the view bounds.
///
/// Usage:
///   using (ScrollView.Begin(paper, "myScroll", width, height)) { /* content */ }
/// </summary>
public static class ScrollView
{
    private const float ScrollBarWidth = 6f;
    private const float MinThumbSize = 20f;
    private const float ScrollSpeed = 30f;

    public static IDisposable Begin(Paper paper, string id, float width, float height,
        float paddingLeft = 0, float paddingRight = 0, float paddingTop = 0, float paddingBottom = 0,
        float rowSpacing = 0)
    {
        ElementHandle outerHandle = default;

        var outerBuilder = paper.Box(id)
            .Size(width, height)
            .Clip()
            .OnScroll(e =>
            {
                float scroll = paper.GetElementStorage(outerHandle, "scrollY", 0f);
                float contentH = paper.GetElementStorage(outerHandle, "contentH", height);
                float maxScroll = MathF.Max(0, contentH - height);
                scroll -= (float)e.Delta * ScrollSpeed;
                scroll = MathF.Max(0, MathF.Min(maxScroll, scroll));
                paper.SetElementStorage(outerHandle, "scrollY", scroll);
            });

        var outerDisposable = outerBuilder.Enter();
        outerHandle = paper.CurrentParent;

        float scrollY = paper.GetElementStorage(outerHandle, "scrollY", 0f);

        var contentBuilder = paper.Column($"{id}_content")
            .PositionType(PositionType.SelfDirected)
            .Position(0, -scrollY)
            .Width(width - ScrollBarWidth)
            .Height(UnitValue.Auto)
            .ChildLeft(paddingLeft).ChildRight(paddingRight)
            .ChildTop(paddingTop).ChildBottom(paddingBottom)
            .RowBetween(rowSpacing);

        contentBuilder.OnPostLayout((contentHandle, contentRect) =>
        {
            float contentHeight = (float)contentRect.Size.Y;
            paper.SetElementStorage(outerHandle, "contentH", contentHeight);

            float maxScroll = MathF.Max(0, contentHeight - height);
            float curScroll = paper.GetElementStorage(outerHandle, "scrollY", 0f);
            if (curScroll > maxScroll)
                paper.SetElementStorage(outerHandle, "scrollY", maxScroll);
        });

        var contentDisposable = contentBuilder.Enter();

        return new ScrollViewScope(paper, id, outerHandle, outerDisposable, contentDisposable,
            width, height, scrollY);
    }

    private class ScrollViewScope : IDisposable
    {
        private readonly Paper _paper;
        private readonly string _id;
        private readonly ElementHandle _outerHandle;
        private readonly IDisposable _outerDisposable;
        private readonly IDisposable _contentDisposable;
        private readonly float _width;
        private readonly float _height;
        private readonly float _scrollY;

        public ScrollViewScope(Paper paper, string id, ElementHandle outerHandle,
            IDisposable outerDisposable, IDisposable contentDisposable,
            float width, float height, float scrollY)
        {
            _paper = paper;
            _id = id;
            _outerHandle = outerHandle;
            _outerDisposable = outerDisposable;
            _contentDisposable = contentDisposable;
            _width = width;
            _height = height;
            _scrollY = scrollY;
        }

        public void Dispose()
        {
            _contentDisposable.Dispose();

            float contentHeight = _paper.GetElementStorage(_outerHandle, "contentH", _height);

            if (contentHeight > _height)
            {
                float maxScroll = contentHeight - _height;
                float viewRatio = _height / contentHeight;
                float thumbH = MathF.Max(MinThumbSize, _height * viewRatio);
                float scrollRatio = maxScroll > 0 ? _scrollY / maxScroll : 0;
                float thumbY = scrollRatio * (_height - thumbH);
                float trackX = _width - ScrollBarWidth;

                _paper.Box($"{_id}_track")
                    .PositionType(PositionType.SelfDirected)
                    .Position(trackX, 0)
                    .Size(ScrollBarWidth, _height)
                    .BackgroundColor(System.Drawing.Color.FromArgb(20, 255, 255, 255))
                    .OnClick(e =>
                    {
                        float clickRatio = (float)e.NormalizedPosition.Y;
                        _paper.SetElementStorage(_outerHandle, "scrollY",
                            MathF.Max(0, MathF.Min(maxScroll, clickRatio * maxScroll)));
                    });

                _paper.Box($"{_id}_thumb")
                    .PositionType(PositionType.SelfDirected)
                    .Position(trackX, thumbY)
                    .Size(ScrollBarWidth, thumbH)
                    .BackgroundColor(System.Drawing.Color.FromArgb(60, 255, 255, 255))
                    .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(120, 255, 255, 255)).End()
                    .Rounded(ScrollBarWidth / 2)
                    .OnDragging(e =>
                    {
                        float trackSpace = _height - thumbH;
                        if (trackSpace > 0)
                        {
                            float scrollDelta = ((float)e.Delta.Y / trackSpace) * maxScroll;
                            float cur = _paper.GetElementStorage(_outerHandle, "scrollY", 0f);
                            _paper.SetElementStorage(_outerHandle, "scrollY",
                                MathF.Max(0, MathF.Min(maxScroll, cur + scrollDelta)));
                        }
                    });
            }

            _outerDisposable.Dispose();
        }
    }
}
