// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Scribe;

namespace Tests;

class TestFontRenderer : IFontRenderer
{
    public object CreateTexture(int width, int height) => new byte[width * height];
    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }
    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices) { }
}
