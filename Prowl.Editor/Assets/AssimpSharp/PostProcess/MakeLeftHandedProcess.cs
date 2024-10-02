using System;
using System.Numerics;

namespace AssimpSharp
{
    public class MakeLeftHandedProcess : BaseProcess
    {
        public override bool IsActive(int flags)
        {
            return ((AiPostProcessSteps)flags).HasFlag(AiPostProcessSteps.MakeLeftHanded);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("MakeLeftHandedProcess begin");

            ProcessNode(scene.RootNode, Matrix4x4.Identity);

            for (int a = 0; a < scene.NumMeshes; a++)
                ProcessMesh(scene.Meshes[a]);

            for (int a = 0; a < scene.NumMaterials; a++)
                ProcessMaterial(scene.Materials[a]);

            for (int a = 0; a < scene.NumAnimations; a++)
            {
                var anim = scene.Animations[a];
                for (int b = 0; b < anim.NodeAnimationChannelCount; b++)
                    ProcessNodeAnim(anim.NodeAnimationChannels[b]);
            }

            Console.WriteLine("MakeLeftHandedProcess finished");
        }

        private void ProcessNode(AiNode node, Matrix4x4 parentGlobalRotation)
        {
            node.Transform = MirrorMatrix(node.Transform);

            for (int a = 0; a < node.NumChildren; a++)
                ProcessNode(node.Children[a], Matrix4x4.Multiply(parentGlobalRotation, node.Transform));
        }

        private void ProcessMesh(AiMesh mesh)
        {
            for (int a = 0; a < mesh.NumVertices; a++)
            {
                mesh.Vertices[a] = new Vector3(mesh.Vertices[a].X, mesh.Vertices[a].Y, -mesh.Vertices[a].Z);

                if (mesh.HasNormals)
                    mesh.Normals[a] = new Vector3(mesh.Normals[a].X, mesh.Normals[a].Y, -mesh.Normals[a].Z);

                if (mesh.HasTangentsAndBitangents)
                {
                    mesh.Tangents[a] = new Vector3(mesh.Tangents[a].X, mesh.Tangents[a].Y, -mesh.Tangents[a].Z);
                    mesh.Bitangents[a] = -mesh.Bitangents[a];
                }
            }

            for (int a = 0; a < mesh.NumBones; a++)
                mesh.Bones[a].OffsetMatrix = MirrorMatrix(mesh.Bones[a].OffsetMatrix);
        }

        private void ProcessMaterial(AiMaterial material)
        {
            foreach (var texture in material.Textures)
            {
                if (texture.MapAxis.HasValue)
                {
                    var mapAxis = texture.MapAxis.Value;
                    texture.MapAxis = new Vector3(mapAxis.X, mapAxis.Y, -mapAxis.Z);
                }
            }
        }

        private void ProcessNodeAnim(AiNodeAnim nodeAnim)
        {
            for (int a = 0; a < nodeAnim.NumPositionKeys; a++)
            {
                var position = nodeAnim.PositionKeys[a].Value;
                nodeAnim.PositionKeys[a].Value = new Vector3(position.X, position.Y, -position.Z);
            }

            for (int a = 0; a < nodeAnim.NumRotationKeys; a++)
            {
                var rotation = nodeAnim.RotationKeys[a].Value;
                nodeAnim.RotationKeys[a].Value = new Quaternion(-rotation.X, -rotation.Y, rotation.Z, rotation.W);
            }
        }

        private Matrix4x4 MirrorMatrix(Matrix4x4 matrix)
        {
            matrix.M13 = -matrix.M13;
            matrix.M23 = -matrix.M23;
            matrix.M33 = -matrix.M33;
            matrix.M43 = -matrix.M43;

            matrix.M31 = -matrix.M31;
            matrix.M32 = -matrix.M32;
            matrix.M34 = -matrix.M34;

            return matrix;
        }
    }
}
