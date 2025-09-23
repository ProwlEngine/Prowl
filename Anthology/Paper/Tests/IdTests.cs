// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;

public class IdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PushID_PreventsException_WhenDuplicateIdsAreAdded(ulong id)
    {
        var paper = new Paper(new Renderer(), 1, 1, new FontAtlasSettings());

        // This test explicitly provides the intID and lineID so that the ID stack can be tested independently
        paper.BeginFrame(0);
        {
            paper.PushID(id);
            {
                paper.Box("Element", 0, 0);

                paper.PushID(id);
                {
                    paper.Box("Element", 0, 0);
                }
                paper.PopID();
            }
            paper.PopID();
        }
        paper.EndFrame();
    }

    private class Renderer : ICanvasRenderer
    {
        public void Dispose() {}

        public object CreateTexture(uint width, uint height)
        {
            return new Vector2Int((int)width, (int)height);
        }

        public Vector2Int GetTextureSize(object texture)
        {
            return (Vector2Int)texture;
        }

        public void SetTextureData(object texture, IntRect bounds, byte[] data) {}

        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) {}
    }
}
