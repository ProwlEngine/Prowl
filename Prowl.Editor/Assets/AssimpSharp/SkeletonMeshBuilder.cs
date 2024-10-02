using System;
using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp
{
    public class SkeletonMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Face> faces = new List<Face>();
        private readonly List<AiBone> bones = new List<AiBone>();
        private readonly bool knobsOnly;

        public SkeletonMeshBuilder(AiScene scene, AiNode root = null, bool knobsOnly = false)
        {
            this.knobsOnly = knobsOnly;

            if (scene.NumMeshes == 0 && scene.RootNode != null)
            {
                root = root ?? scene.RootNode;
                CreateGeometry(root);
                scene.NumMeshes = 1;
                scene.Meshes = new List<AiMesh> { CreateMesh() };
                root.NumMeshes = 1;
                root.Meshes = new int[1];
                if (scene.NumMaterials == 0)
                {
                    scene.NumMaterials = 1;
                    scene.Materials = new List<AiMaterial> { CreateMaterial() };
                }
            }
        }

        private void CreateGeometry(AiNode node)
        {
            int vertexStartIndex = vertices.Count;

            if (node.NumChildren > 0 && !knobsOnly)
            {
                for (int a = 0; a < node.NumChildren; a++)
                {
                    Matrix4x4 childTransform = node.Children[a].Transform;
                    Vector3 childpos = new Vector3(childTransform.M14, childTransform.M24, childTransform.M34);
                    float distanceToChild = childpos.Length();
                    if (distanceToChild < 0.0001f) continue;

                    Vector3 up = Vector3.Normalize(childpos);
                    Vector3 orth = new Vector3(1f, 0f, 0f);
                    if (Math.Abs(Vector3.Dot(orth, up)) > 0.99) orth = new Vector3(0f, 1f, 0f);

                    Vector3 front = Vector3.Normalize(Vector3.Cross(up, orth));
                    Vector3 side = Vector3.Normalize(Vector3.Cross(front, up));

                    int localVertexStart = vertices.Count;
                    vertices.Add(-front * distanceToChild * 0.1f);
                    vertices.Add(childpos);
                    vertices.Add(-side * distanceToChild * 0.1f);
                    vertices.Add(-side * distanceToChild * 0.1f);
                    vertices.Add(childpos);
                    vertices.Add(front * distanceToChild * 0.1f);
                    vertices.Add(front * distanceToChild * 0.1f);
                    vertices.Add(childpos);
                    vertices.Add(side * distanceToChild * 0.1f);
                    vertices.Add(side * distanceToChild * 0.1f);
                    vertices.Add(childpos);
                    vertices.Add(-front * distanceToChild * 0.1f);

                    faces.Add(new Face(localVertexStart + 0, localVertexStart + 1, localVertexStart + 2));
                    faces.Add(new Face(localVertexStart + 3, localVertexStart + 4, localVertexStart + 5));
                    faces.Add(new Face(localVertexStart + 6, localVertexStart + 7, localVertexStart + 8));
                    faces.Add(new Face(localVertexStart + 9, localVertexStart + 10, localVertexStart + 11));
                }
            }
            else
            {
                Vector3 ownpos = new Vector3(node.Transform.M14, node.Transform.M24, node.Transform.M34);
                float sizeEstimate = ownpos.Length() * 0.18f;

                vertices.Add(new Vector3(-sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, 0f, -sizeEstimate));
                vertices.Add(new Vector3(0f, sizeEstimate, 0f));
                vertices.Add(new Vector3(sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, 0f, -sizeEstimate));
                vertices.Add(new Vector3(sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, -sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, 0f, -sizeEstimate));
                vertices.Add(new Vector3(0f, -sizeEstimate, 0f));
                vertices.Add(new Vector3(-sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, 0f, -sizeEstimate));

                vertices.Add(new Vector3(-sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, 0f, sizeEstimate));
                vertices.Add(new Vector3(0f, sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, 0f, sizeEstimate));
                vertices.Add(new Vector3(sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(sizeEstimate, 0f, 0f));
                vertices.Add(new Vector3(0f, 0f, sizeEstimate));
                vertices.Add(new Vector3(0f, -sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, -sizeEstimate, 0f));
                vertices.Add(new Vector3(0f, 0f, sizeEstimate));
                vertices.Add(new Vector3(-sizeEstimate, 0f, 0f));

                faces.Add(new Face(vertexStartIndex + 0, vertexStartIndex + 1, vertexStartIndex + 2));
                faces.Add(new Face(vertexStartIndex + 3, vertexStartIndex + 4, vertexStartIndex + 5));
                faces.Add(new Face(vertexStartIndex + 6, vertexStartIndex + 7, vertexStartIndex + 8));
                faces.Add(new Face(vertexStartIndex + 9, vertexStartIndex + 10, vertexStartIndex + 11));
                faces.Add(new Face(vertexStartIndex + 12, vertexStartIndex + 13, vertexStartIndex + 14));
                faces.Add(new Face(vertexStartIndex + 15, vertexStartIndex + 16, vertexStartIndex + 17));
                faces.Add(new Face(vertexStartIndex + 18, vertexStartIndex + 19, vertexStartIndex + 20));
                faces.Add(new Face(vertexStartIndex + 21, vertexStartIndex + 22, vertexStartIndex + 23));
            }

            int numVertices = vertices.Count - vertexStartIndex;
            if (numVertices > 0)
            {
                Matrix4x4.Invert(node.Transform, out var offsetMat);

                var bone = new AiBone {
                    Name = node.Name,
                    OffsetMatrix = offsetMat
                };

                var parent = node.Parent;
                while (parent != null)
                {
                    Matrix4x4.Invert(parent.Transform, out var boneOffsetMat);
                    bone.OffsetMatrix = Matrix4x4.Multiply(boneOffsetMat, bone.OffsetMatrix);
                    parent = parent.Parent;
                }

                bone.NumWeights = numVertices;
                bone.Weights = new AiVertexWeight[numVertices];
                for (int i = 0; i < numVertices; i++)
                {
                    bone.Weights[i] = new AiVertexWeight { VertexId = vertexStartIndex + i, Weight = 1f };
                }

                Matrix4x4.Invert(bone.OffsetMatrix, out var boneToMeshTransform);
                for (int a = vertexStartIndex; a < vertices.Count; a++)
                {
                    vertices[a] = Vector3.Transform(vertices[a], boneToMeshTransform);
                }

                bones.Add(bone);
            }

            foreach (var child in node.Children)
            {
                CreateGeometry(child);
            }
        }

        private AiMesh CreateMesh()
        {
            var mesh = new AiMesh {
                NumVertices = vertices.Count,
                Vertices = new List<Vector3>(vertices),
                Normals = new List<Vector3>(new Vector3[vertices.Count]),
                NumFaces = faces.Count,
                Faces = new List<AiFace>()
            };

            foreach (var face in faces)
            {
                mesh.Faces.Add(new AiFace() { face.Indices[0], face.Indices[1], face.Indices[2] });

                Vector3 nor = Vector3.Cross(
                    vertices[face.Indices[2]] - vertices[face.Indices[0]],
                    vertices[face.Indices[1]] - vertices[face.Indices[0]]
                );

                if (nor.Length() < 1e-5)
                {
                    nor = new Vector3(1f, 0f, 0f);
                }

                for (int n = 0; n < 3; n++)
                {
                    mesh.Normals[face.Indices[n]] = nor;
                }
            }

            mesh.NumBones = bones.Count;
            mesh.Bones = bones;
            mesh.MaterialIndex = 0;

            return mesh;
        }

        private AiMaterial CreateMaterial()
        {
            return new AiMaterial {
                Name = "SkeletonMaterial",
                IsTwoSided = true
            };
        }

        private class Face
        {
            public int[] Indices { get; }

            public Face()
            {
                Indices = new int[3];
            }

            public Face(int p0, int p1, int p2)
            {
                Indices = new int[] { p0, p1, p2 };
            }
        }
    }
}