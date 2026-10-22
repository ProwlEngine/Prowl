using System.Runtime.InteropServices;

using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal static class ModelGeometry
{
    public static MeshData FromModel(ModelAsset model)
    {
        uint[] indices = new uint[model.Indices.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            indices[i] = model.Indices[i];
            indices[i + 1] = model.Indices[i + 1];
            indices[i + 2] = model.Indices[i + 2];
        }

        return new MeshData
        {
            VertexCount = (uint)model.Positions.Length,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes<Float3>(model.Positions).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes<Float3>(model.Normals).ToArray() },
                new VertexStream { Semantic = VertexSemantic.UV0, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes<Float2>(model.UV0).ToArray() },
            ],
            Indices = indices,
        };
    }
}
