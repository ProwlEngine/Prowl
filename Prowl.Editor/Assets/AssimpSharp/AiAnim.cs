using System.Numerics;
using System.Collections.Generic;

namespace AssimpSharp
{
    public class AiVectorKey
    {
        public double Time { get; set; }
        public Vector3 Value { get; set; }
    }

    public class AiQuatKey
    {
        public double Time { get; set; }
        public Quaternion Value { get; set; }
    }

    public class AiMeshKey
    {
        public double Time { get; set; }
        public int Value { get; set; }
    }

    public class AiMeshMorphKey
    {
        public double Time { get; set; }
        public int[] Values { get; set; }
        public double[] Weights { get; set; }
        public int NumValuesAndWeights { get; set; }
    }

    public enum AiAnimBehaviour
    {
        Default = 0x0,
        Constant = 0x1,
        Linear = 0x2,
        Repeat = 0x3
    }

    public class AiNodeAnim
    {
        public string NodeName { get; set; } = "";
        public bool HasPositionKeys => NumPositionKeys > 0;
        public int NumPositionKeys { get; set; }
        public List<AiVectorKey> PositionKeys { get; set; } = new List<AiVectorKey>();
        public bool HasRotationKeys => NumRotationKeys > 0;
        public int NumRotationKeys { get; set; }
        public List<AiQuatKey> RotationKeys { get; set; } = new List<AiQuatKey>();
        public bool HasScalingKeys => NumScalingKeys > 0;
        public int NumScalingKeys { get; set; }
        public List<AiVectorKey> ScalingKeys { get; set; } = new List<AiVectorKey>();
        public AiAnimBehaviour PreState { get; set; } = AiAnimBehaviour.Default;
        public AiAnimBehaviour PostState { get; set; } = AiAnimBehaviour.Default;
    }

    public class AiMeshAnim
    {
        public string Name { get; set; } = "";
        public int NumKeys { get; set; }
        public List<AiMeshKey> Keys { get; set; } = new List<AiMeshKey>();
    }

    public class AiMeshMorphAnim
    {
        public string Name { get; set; } = "";
        public int NumKeys { get; set; }
        public AiMeshMorphKey[] Keys { get; set; }
    }

    public class AiAnimation
    {
        public string Name { get; set; } = "";
        public double DurationInTicks { get; set; } = -1.0;
        public double TicksPerSecond { get; set; }
        public bool HasNodeAnimations => NodeAnimationChannelCount > 0;
        public int NodeAnimationChannelCount { get; set; }
        public List<AiNodeAnim> NodeAnimationChannels { get; set; } = new List<AiNodeAnim>();
        public bool HasMeshAnimations => MeshAnimationChannelCount > 0;
        public int MeshAnimationChannelCount { get; set; }
        public List<List<AiMeshAnim>> MeshChannels { get; set; } = new List<List<AiMeshAnim>>();
        public bool HasMeshMorphAnimations => MeshMorphAnimationChannelCount > 0;
        public int MeshMorphAnimationChannelCount { get; set; }
        public List<AiMeshMorphAnim> MorphMeshChannels { get; set; } = new List<AiMeshMorphAnim>();
    }
}
