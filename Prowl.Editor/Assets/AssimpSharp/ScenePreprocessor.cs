using System;
using System.Linq;
using System.Numerics;

namespace AssimpSharp
{
    public static class ScenePreprocessor
    {
        private static AiScene scene;

        public static void ProcessScene(AiScene scene)
        {
            ScenePreprocessor.scene = scene;

            foreach (var mesh in scene.Meshes)
            {
                ProcessMesh(mesh);
            }

            foreach (var animation in scene.Animations)
            {
                ProcessAnimation(animation);
            }

            if (scene.NumMaterials == 0 && scene.NumMeshes > 0)
            {
                var material = new AiMaterial {
                    Color = new AiMaterial.MatColor { Diffuse = new Vector4(0.6f, 0.6f, 0.6f, 1f) },
                    Name = Constants.AI_DEFAULT_MATERIAL_NAME
                };
                scene.Materials.Add(material);
                Console.WriteLine($"ScenePreprocessor: Adding default material '{Constants.AI_DEFAULT_MATERIAL_NAME}'");

                foreach (var mesh in scene.Meshes)
                {
                    mesh.MaterialIndex = scene.NumMaterials;
                }

                scene.NumMaterials++;
            }
        }

        private static void ProcessMesh(AiMesh mesh)
        {
            foreach (var textureCoord in mesh.TextureCoordinateChannels)
            {
                for (int i = 0; i < textureCoord.Count; i++)
                {
                    if (textureCoord[i].Length == 0)
                    {
                        textureCoord[i] = new float[2];
                    }
                }
            }

            if (mesh.PrimitiveType == 0)
            {
                foreach (var face in mesh.Faces)
                {
                    mesh.PrimitiveType |= face.Count switch {
                        3 => AiPrimitiveType.Triangle,
                        2 => AiPrimitiveType.Line,
                        1 => AiPrimitiveType.Point,
                        _ => AiPrimitiveType.Polygon
                    };
                }
            }

            if (mesh.Tangents.Any() && mesh.Normals.Any() && !mesh.Bitangents.Any())
            {
                mesh.Bitangents = new System.Collections.Generic.List<Vector3>(mesh.NumVertices);
                for (int i = 0; i < mesh.NumVertices; i++)
                {
                    mesh.Bitangents.Add(Vector3.Cross(mesh.Normals[i], mesh.Tangents[i]));
                }
            }
        }

        private static void ProcessAnimation(AiAnimation animation)
        {
            double first = 1e10;
            double last = -1e10;

            foreach (var channel in animation.NodeAnimationChannels)
            {
                if (animation.DurationInTicks == -1.0)
                {
                    UpdateVecMinMaxTime(channel.PositionKeys, ref first, ref last);
                    UpdateVecMinMaxTime(channel.ScalingKeys, ref first, ref last);
                    UpdateQuatMinMaxTime(channel.RotationKeys, ref first, ref last);
                }

                if (channel.NumRotationKeys == 0 || channel.NumPositionKeys == 0 || channel.NumScalingKeys == 0)
                {
                    var node = scene.RootNode.FindNode(channel.NodeName);
                    if (node != null)
                    {
                        Matrix4x4.Decompose(node.Transform, out Vector3 scaling, out Quaternion rotation, out Vector3 position);

                        if (channel.NumRotationKeys == 0)
                        {
                            channel.NumRotationKeys = 1;
                            channel.RotationKeys = [ new AiQuatKey { Time = 0.0, Value = rotation } ];
                            Console.WriteLine("ScenePreprocessor: Dummy rotation track has been generated");
                        }

                        if (channel.NumScalingKeys == 0)
                        {
                            channel.NumScalingKeys = 1;
                            channel.ScalingKeys = [ new AiVectorKey { Time = 0.0, Value = scaling } ];
                            Console.WriteLine("ScenePreprocessor: Dummy scaling track has been generated");
                        }

                        if (channel.NumPositionKeys == 0)
                        {
                            channel.NumPositionKeys = 1;
                            channel.PositionKeys = [ new AiVectorKey { Time = 0.0, Value = position } ];
                            Console.WriteLine("ScenePreprocessor: Dummy position track has been generated");
                        }
                    }
                }
            }

            if (animation.DurationInTicks == -1.0)
            {
                Console.WriteLine("ScenePreprocessor: Setting animation duration");
                animation.DurationInTicks = last - Math.Min(first, 0.0);
            }
        }

        private static void UpdateVecMinMaxTime<T>(IEnumerable<T> keys, ref double first, ref double last) where T : AiVectorKey
        {
            foreach (var key in keys)
            {
                first = Math.Min(first, key.Time);
                last = Math.Max(last, key.Time);
            }
        }

        private static void UpdateQuatMinMaxTime<T>(IEnumerable<T> keys, ref double first, ref double last) where T : AiQuatKey
        {
            foreach (var key in keys)
            {
                first = Math.Min(first, key.Time);
                last = Math.Max(last, key.Time);
            }
        }
    }
}
