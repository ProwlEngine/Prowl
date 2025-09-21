namespace Physics2D
{
    public class Contact
    {
        public int Hash;

        // Contact point
        public Vec2 Position;

        // Contact normal (toward shape2)
        public Vec2 NormalTowardTwo;

        // Penetration depth (d < 0)
        public float Depth;

        // Accumulated impulses
        public float LambdaNAcc;
        public float LambdaTAcc;

        // Solver scratch values
        public Vec2 R1, R2;          // world-space offsets
        public Vec2 R1Local, R2Local; // local offsets
        public float Emn, Emt;
        public float Bounce;

        public Contact(Vec2 p, Vec2 n, float d, int hash)
        {
            Position = p;
            NormalTowardTwo = Vec2.Normalize(n);
            Depth = d;
            Hash = hash;
            LambdaNAcc = 0;
            LambdaTAcc = 0;
        }
    }
}
