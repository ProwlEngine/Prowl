using System.Numerics;
using System.Runtime.InteropServices;

namespace Prowl.Scribe
{
    public interface IFontRenderer
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Vertex
        {
            public Vector3 Position;
            public FontColor Color;
            public Vector2 TextureCoordinate;

            public Vertex(Vector3 position, FontColor color, Vector2 texCoord)
            {
                Position = position;
                Color = color;
                TextureCoordinate = texCoord;
            }
        }

        object CreateTexture(int width, int height);
        void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data);
        void DrawQuads(object texture, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<int> indices);
    }
}
