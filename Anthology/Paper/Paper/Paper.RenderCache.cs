// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI;

/// <summary>What an element with <see cref="Paper.CacheRender(ref ElementHandle, long)"/> drew inside itself last time.</summary>
internal sealed class RenderSnapshot
{
    public readonly CanvasSnapshot Snapshot = new();
    public long Key;
    public float Width, Height;
}

public partial class Paper
{
    // True while an element's contents are being recorded. Culling is skipped meanwhile, since a
    // replay after the element moves has to hold children that were outside the clip when recorded.
    private bool _recordingSnapshot;

    /// <summary>
    /// Records everything drawn inside the element, meaning its text, its draw callbacks and all of its
    /// children, and replays that on later frames instead of drawing it again, for as long as
    /// <paramref name="key"/> and the element's size stay the same. The element's own background,
    /// border and clip are still drawn every frame.
    /// <para>
    /// Moving the element, for example by scrolling, replays the recording at the new position without
    /// redrawing. Change the key whenever anything drawn inside changes, including hover and selection
    /// looks. Children are not culled while recording, so this suits contents that are mostly visible
    /// or that cull themselves. Contents holding a higher layer, such as a popup, are never cached.
    /// </para>
    /// </summary>
    public void CacheRender(ref ElementHandle handle, long key) => handle.Data._renderCacheKey = key;

    // Replays the element's recorded contents, or starts recording them. Returns true when replayed,
    // in which case the caller skips drawing the contents.
    private bool ReplayOrRecord(ref ElementData data, Rect rect, out bool recording)
    {
        recording = false;
        if (!data._renderCacheKey.HasValue || data._cullHasLayerBreakout)
            return false;

        long key = data._renderCacheKey.Value;
        var cached = data._elementStyle.RenderSnapshot ??= new RenderSnapshot();
        if (cached.Key == key && cached.Width == rect.Size.X && cached.Height == rect.Size.Y
            && _canvas.DrawSnapshot(cached.Snapshot, rect.Min.X, rect.Min.Y))
            return true;

        // Inside another element's recording this one is simply drawn, since recordings do not nest.
        if (_canvas.IsCapturing)
            return false;

        cached.Key = key;
        cached.Width = rect.Size.X;
        cached.Height = rect.Size.Y;
        _canvas.BeginSnapshot(cached.Snapshot, rect.Min.X, rect.Min.Y);
        _recordingSnapshot = true;
        recording = true;
        return false;
    }

    private void EndRecording()
    {
        _canvas.EndSnapshot();
        _recordingSnapshot = false;
    }
}
