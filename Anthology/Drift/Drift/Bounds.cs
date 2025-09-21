using System.Numerics;

namespace Prowl.Drift
{
    //-----------------------------------
    // Bounds
    //-----------------------------------
    public struct Bounds
    {
        public Vector2 Mins, Maxs;

        public Bounds(Vector2 min, Vector2 max) { Mins = min; Maxs = max; }

        public void Clear() 
        { 
            Mins = new Vector2(float.MaxValue, float.MaxValue);
            Maxs = new Vector2(float.MinValue, float.MinValue); 
        }

        public void AddPoint(Vector2 p)
        {
            Mins = new Vector2(MathF.Min(Mins.X, p.X), MathF.Min(Mins.Y, p.Y));
            Maxs = new Vector2(MathF.Max(Maxs.X, p.X), MathF.Max(Maxs.Y, p.Y));
        }

        public void AddBounds(Bounds b)
        {
            Mins = new Vector2(MathF.Min(Mins.X, b.Mins.X), MathF.Min(Mins.Y, b.Mins.Y));
            Maxs = new Vector2(MathF.Max(Maxs.X, b.Maxs.X), MathF.Max(Maxs.Y, b.Maxs.Y));
        }

        public bool Intersects(Bounds b) =>
            !(Maxs.X < b.Mins.X || Mins.X > b.Maxs.X || Maxs.Y < b.Mins.Y || Mins.Y > b.Maxs.Y);

        public bool ContainsPoint(Vector2 p) =>
            p.X >= Mins.X && p.X <= Maxs.X && p.Y >= Mins.Y && p.Y <= Maxs.Y;
    }
}
