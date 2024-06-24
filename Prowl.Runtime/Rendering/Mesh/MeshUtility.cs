using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;


namespace Prowl.Runtime
{
    public enum MeshResource
    {
        Position,
        UV0,
        UV1,
        Normals,
        Tangents,
        Colors,
        BoneIndices,
        BoneWeights,
        Custom
    }

    public static class MeshUtility    
    {
        internal static VertexLayoutDescription GetLayoutForResource(MeshResource resource)
        {
            return resource switch 
            {
                MeshResource.Position =>    new VertexLayoutDescription(new VertexElementDescription("POSITION", VertexElementFormat.Float3, VertexElementSemantic.Position)),
                MeshResource.UV0 =>         new VertexLayoutDescription(new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, VertexElementSemantic.TextureCoordinate)),
                MeshResource.UV1 =>         new VertexLayoutDescription(new VertexElementDescription("TEXCOORD1", VertexElementFormat.Float2, VertexElementSemantic.TextureCoordinate)),
                MeshResource.Normals =>     new VertexLayoutDescription(new VertexElementDescription("NORMAL", VertexElementFormat.Float3, VertexElementSemantic.Normal)),
                MeshResource.Tangents =>    new VertexLayoutDescription(new VertexElementDescription("TANGENT", VertexElementFormat.Float3, VertexElementSemantic.Normal)),
                MeshResource.Colors =>      new VertexLayoutDescription(new VertexElementDescription("COLOR", VertexElementFormat.Float4, VertexElementSemantic.Color)),
                MeshResource.BoneIndices => new VertexLayoutDescription(new VertexElementDescription("BONEINDEX", VertexElementFormat.Float4, VertexElementSemantic.Position)),
                MeshResource.BoneWeights => new VertexLayoutDescription(new VertexElementDescription("BONEWEIGHT", VertexElementFormat.Float4, VertexElementSemantic.Color)),
                MeshResource.Custom =>      throw new Exception("Custom mesh resource types must be created manually."),
            };
        }

        public static void UploadMeshResources(CommandList commandList, Mesh mesh, ShaderPass pass, KeywordState? keywords = null)
        {
            mesh.Upload();

            commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            
            var vertexInputs = pass.GetVariant(keywords).vertexInputs;

            for (uint i = 0; i < vertexInputs.Count; i++)
            {
                MeshResource vertexResource = vertexInputs[(int)i].Item1;

                switch (vertexResource)
                {
                    case MeshResource.Position:     commandList.SetVertexBuffer(i, mesh.VertexBuffer, 0);                           break;
                    case MeshResource.UV0:          commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.UVStart);          break;
                    case MeshResource.UV1:          commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.UV2Start);         break;
                    case MeshResource.Normals:      commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.NormalsStart);     break;
                    case MeshResource.Tangents:     commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.TangentsStart);    break;
                    case MeshResource.Colors:       commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.ColorsStart);      break;
                    case MeshResource.BoneIndices:  commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.BoneIndexStart);   break;
                    case MeshResource.BoneWeights:  commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.BoneWeightStart);  break;
                }
            }
        }
    }
}