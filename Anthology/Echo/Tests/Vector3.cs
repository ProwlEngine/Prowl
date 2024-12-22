using System.Collections.Concurrent;

namespace Tests
{
    [Serializable]
    public struct Vector3 : IEquatable<Vector3>
    {
        public float X;

        public float Y;

        public float Z;

        [NonSerialized]
        private ConcurrentBag<Vector3> TestNonSerialized = new();

        public Vector3()
        {
        }

        public bool Equals(Vector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }
    }
}
