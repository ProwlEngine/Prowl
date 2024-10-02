using System;
using System.Collections.Generic;
using System.Linq;

namespace AssimpSharp
{
    public class LimitBoneWeightsProcess : BaseProcess
    {
        private const int AI_LMW_MAX_WEIGHTS = 16;

        private int mMaxWeights;
        private bool mRemoveEmptyBones;

        public LimitBoneWeightsProcess()
        {
            mMaxWeights = AI_LMW_MAX_WEIGHTS;
            mRemoveEmptyBones = true;
        }

        public override bool IsActive(int flags)
        {
            return ((AiPostProcessSteps)flags).HasFlag(AiPostProcessSteps.LimitBoneWeights);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("LimitBoneWeightsProcess begin");

            for (int m = 0; m < scene.NumMeshes; ++m)
            {
                ProcessMesh(scene.Meshes[m]);
            }

            Console.WriteLine("LimitBoneWeightsProcess end");
        }

        public override void SetupProperties(Importer importer)
        {
            mMaxWeights = importer.GetPropertyInteger(PropertyRepository.AI_CONFIG_PP_LBW_MAX_WEIGHTS, AI_LMW_MAX_WEIGHTS);
            mRemoveEmptyBones = importer.GetPropertyBool(PropertyRepository.AI_CONFIG_IMPORT_REMOVE_EMPTY_BONES, true);
        }

        private static int RemoveEmptyBones(AiMesh mesh)
        {
            int writeBone = 0;
            for (int readBone = 0; readBone < mesh.NumBones; ++readBone)
            {
                AiBone bone = mesh.Bones[readBone];
                if (bone.NumWeights > 0)
                {
                    mesh.Bones[writeBone++] = bone;
                }
            }
            return writeBone;
        }

        private void ProcessMesh(AiMesh mesh)
        {
            if (!mesh.HasBones)
                return;

            var vertexWeights = new List<List<Weight>>(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                vertexWeights.Add(new List<Weight>());
            }

            int maxVertexWeights = 0;

            for (int b = 0; b < mesh.NumBones; ++b)
            {
                AiBone bone = mesh.Bones[b];
                for (int w = 0; w < bone.NumWeights; ++w)
                {
                    AiVertexWeight vw = bone.Weights[w];

                    if (vertexWeights.Count <= vw.VertexId)
                        continue;

                    vertexWeights[vw.VertexId].Add(new Weight(b, vw.Weight));
                    maxVertexWeights = Math.Max(maxVertexWeights, vertexWeights[vw.VertexId].Count);
                }
            }

            if (maxVertexWeights <= mMaxWeights)
                return;

            int removed = 0;
            int oldBones = mesh.NumBones;

            for (int v = 0; v < vertexWeights.Count; ++v)
            {
                var vw = vertexWeights[v];
                if (vw.Count <= mMaxWeights)
                    continue;

                vw.Sort((a, b) => b.mWeight.CompareTo(a.mWeight));

                int m = vw.Count;
                vw.RemoveRange(mMaxWeights, vw.Count - mMaxWeights);
                removed += m - vw.Count;

                float sum = vw.Sum(w => w.mWeight);
                if (sum != 0.0f)
                {
                    float invSum = 1.0f / sum;
                    for (int i = 0; i < vw.Count; ++i)
                    {
                        vw[i] = new Weight(vw[i].mBone, vw[i].mWeight * invSum);
                    }
                }
            }

            foreach (var bone in mesh.Bones)
            {
                //bone.Weights.Clear();
                bone.Weights = new AiVertexWeight[mMaxWeights];
            }

            int weightIndex = 0;
            for (int a = 0; a < vertexWeights.Count; ++a)
            {
                var vw = vertexWeights[a];
                foreach (var weight in vw)
                {
                    AiBone bone = mesh.Bones[weight.mBone];
                    //bone.Weights.Add(new AiVertexWeight(a, weight.mWeight));
                    bone.Weights[weightIndex++] = new AiVertexWeight { VertexId = a, Weight = weight.mWeight };
                }
            }

            if (mRemoveEmptyBones)
            {
                mesh.NumBones = RemoveEmptyBones(mesh);
            }

            Console.WriteLine($"Removed {removed} weights. Input bones: {oldBones}. Output bones: {mesh.NumBones}");
        }

        private struct Weight
        {
            public int mBone;
            public float mWeight;

            public Weight(int bone, float weight)
            {
                mBone = bone;
                mWeight = weight;
            }
        }
    }
}
