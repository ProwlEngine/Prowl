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
    public void PushID_PreventsException_WhenDuplicateIdsAreAdded(int id)
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

    [Fact]
    public void RootStorage_PersistsAcrossFrames()
    {
        var paper = new Paper(new Renderer(), 1, 1, new FontAtlasSettings());

        paper.BeginFrame(0);
        paper.SetRootStorage("count", 3);
        paper.EndFrame();

        paper.BeginFrame(0);
        Assert.Equal(3, paper.GetRootStorage<int>("count"));
        paper.EndFrame();
    }

    [Fact]
    public void AnimationHelpers_ProgressAtTheTopLevel()
    {
        var paper = new Paper(new Renderer(), 1, 1, new FontAtlasSettings());
        float value = 0;

        for (int frame = 0; frame < 7; frame++)
        {
            paper.BeginFrame(0.05f);
            value = paper.AnimateBool(frame >= 2, 0.5f);
            paper.EndFrame();
        }

        Assert.InRange(value, 0.3f, 0.7f);
    }

    private class Renderer : ICanvasRenderer
    {
        public void Dispose() { }

        public object CreateTexture(uint width, uint height)
        {
            return new Int2((int)width, (int)height);
        }

        public Int2 GetTextureSize(object texture)
        {
            return (Int2)texture;
        }

        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }

        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) { }
    }
}
