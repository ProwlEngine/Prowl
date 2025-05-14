namespace Prowl.Vector
{
    public static class Float4Extensions
    {
        public static Byte4 ToColor4b(this Float4 vector)
        {
            return new Byte4(
                (byte)(vector.X * 255),
                (byte)(vector.Y * 255),
                (byte)(vector.Z * 255),
                (byte)(vector.W * 255)
            );
        }

        public static Float4 ToColor4f(this Byte4 vector)
        {
            return new Float4(
                vector.X / 255f,
                vector.Y / 255f,
                vector.Z / 255f,
                vector.W / 255f
            );
        }

    }
}
