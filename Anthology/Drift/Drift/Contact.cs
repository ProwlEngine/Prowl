using System.Numerics;

namespace Prowl.Drift
{
    public class Contact
    {
        public int Hash;

        // Contact point
        public Vector2 Position;

        // Contact normal (toward shape2)
        public Vector2 NormalTowardTwo;

        // Penetration depth (d < 0)
        public float Depth;

        // Accumulated impulses
        public float LambdaNAcc = 0;
        public float LambdaTAcc = 0;

        // Solver scratch values
        public Vector2 R1, R2;          // world-space offsets
        public Vector2 R1Local, R2Local; // local offsets
        public float Emn, Emt;
        public float Bounce;

        public Contact(Vector2 p, Vector2 n, float d, int hash)
        {
            Position = p;
            NormalTowardTwo = Vector2.Normalize(n);
            Depth = d;
            Hash = hash;
        }
    }
}
