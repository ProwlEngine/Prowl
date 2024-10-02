using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp
{
    public static class AnimMeshCreator
    {
        public static AiAnimMesh AiCreateAnimMesh(AiMesh mesh)
        {
            var animesh = new AiAnimMesh();

            for (int i = 0; i < mesh.VertexColorChannels.Count; i++)
            {
                animesh.VertexColorChannels.Add(new List<Vector4>(mesh.VertexColorChannels[i]));
            }

            for (int a = 0; a < mesh.TextureCoordinateChannels.Count; a++)
            {
                animesh.TextureCoordinateChannels.Add(new List<float[]>());
                for (int b = 0; b < mesh.TextureCoordinateChannels[a].Count; b++)
                {
                    animesh.TextureCoordinateChannels[a].Add((float[])mesh.TextureCoordinateChannels[a][b].Clone());
                }
            }

            return animesh;
        }
    }
}