using System.Runtime.CompilerServices;

using Prowl.Vector;

using static SoftwareRasterizer.Rasterizer.GraphicsDevice;

namespace SoftwareRasterizer.Rasterizer.Engines;

internal class TriangleRasterizer(GraphicsDevice device) : RasterizerBase(device)
{
    private const int QUAD_SIZE = 2;
    private static readonly FragmentContext s_emptyContext = new FragmentContext();
    private static readonly ThreadLocal<Float4[]> s_varyingCache = new(() => new Float4[32]);

    public override void Rasterize(RasterTriangle triangle)
    {
        Float3 v0 = triangle.Vertices[0].ScreenPosition;
        Float3 v1 = triangle.Vertices[1].ScreenPosition;
        Float3 v2 = triangle.Vertices[2].ScreenPosition;

        if (Device.CullMode != CullMode.None && IsBackFace(v0, v1, v2))
        {
            return;
        }

        // Pre-calculate edge equation constants
        float area = EdgeFunction(ref v0, ref v1, ref v2);
        if (area < 1e-6 && area > -1e-6)
            return;

        // Calculate bounds same as before
        int minX = (int)Maths.Max(Maths.Floor(Maths.Min(v0.X, Maths.Min(v1.X, v2.X))), 0);
        int maxX = (int)Maths.Min(Maths.Ceiling(Maths.Max(v0.X, Maths.Max(v1.X, v2.X))), Device.CurrentFramebuffer.Width - 1);
        int minY = (int)Maths.Max(Maths.Floor(Maths.Min(v0.Y, Maths.Min(v1.Y, v2.Y))), 0);
        int maxY = (int)Maths.Min(Maths.Ceiling(Maths.Max(v0.Y, Maths.Max(v1.Y, v2.Y))), Device.CurrentFramebuffer.Height - 1);

        // Early exit if triangle is completely outside screen
        if (minX > maxX || minY > maxY) return;

        // Pre-calculate edge equation coefficients
        float invArea = 1.0f / area;
        var e0 = new EdgeCoefficients(v1, v2);  // edge v1->v2
        var e1 = new EdgeCoefficients(v2, v0);  // edge v2->v0
        var e2 = new EdgeCoefficients(v0, v1);  // edge v0->v1
        EdgeData edgeData = new(e0, e1, e2, invArea);

        if (Device.EnableDerivatives)
        {
            // Align to quad boundaries
            minX &= ~(QUAD_SIZE - 1);
            minY &= ~(QUAD_SIZE - 1);
            maxX = (maxX + QUAD_SIZE - 1) & ~(QUAD_SIZE - 1);
            maxY = (maxY + QUAD_SIZE - 1) & ~(QUAD_SIZE - 1);
            
            for (int quadY = minY; quadY < maxY; quadY += QUAD_SIZE)
            {
                for (int quadX = minX; quadX < maxX; quadX += QUAD_SIZE)
                {
                    ProcessQuad(quadX, quadY, triangle, edgeData);
                }
            }
        }
        else
        {
            var varyingCache = s_varyingCache.Value;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ProcessPixel(x, y, ref triangle, ref e0, ref e1, ref e2, invArea, varyingCache);
                }
            }
        }

    }

    public readonly struct EdgeCoefficients
    {
        public readonly float A;     // dy (step x)
        public readonly float B;     // -dx (step y)
        public readonly float C;     // C = -(A*x1 + B*y1)
        public readonly float InitX; // Initial X value for incremental stepping
        public readonly float InitY; // Initial Y value for incremental stepping

        public EdgeCoefficients(Float3 v1, Float3 v2)
        {
            A = v2.Y - v1.Y;
            B = v1.X - v2.X;
            C = -(A * v1.X + B * v1.Y);
            InitX = A;
            InitY = B;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Evaluate(float x, float y) => A * x + B * y + C;
    }

    private readonly struct EdgeData(EdgeCoefficients e0, EdgeCoefficients e1, EdgeCoefficients e2, float invArea)
    {
        public readonly EdgeCoefficients E0 = e0;
        public readonly EdgeCoefficients E1 = e1;
        public readonly EdgeCoefficients E2 = e2;
        public readonly float InvArea = invArea;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessPixel(int x, int y, ref RasterTriangle triangle,
        ref EdgeCoefficients e0, ref EdgeCoefficients e1, ref EdgeCoefficients e2,
        float invArea, Float4[] varyingCache)
    {
        // Use pixel center
        float px = x + 0.5f;
        float py = y + 0.5f;

        // Calculate edge values
        float w0 = e0.Evaluate(px, py) * invArea;
        float w1 = e1.Evaluate(px, py) * invArea;
        float w2 = e2.Evaluate(px, py) * invArea;

        // Check if pixel is inside triangle
        if (w0 < 0 || w1 < 0 || w2 < 0)
            return;

        float depth = InterpolateFloat(
            triangle.Vertices[0].ScreenPosition.Z,
            triangle.Vertices[1].ScreenPosition.Z,
            triangle.Vertices[2].ScreenPosition.Z,
            w0, w1, w2);

        lock (Device.CurrentFramebuffer.GetPixelLockUnsafe(x, y))
        {
            // Skip if failed depth test (unless shader can write depth)
            if (Device.DoDepthTest && !Device.CurrentShader.CanWriteDepth)
            {
                if (depth >= Device.CurrentFramebuffer.GetDepth(x, y))
                    return;
            }

            InterpolateVaryings(ref triangle, w0, w1, w2, varyingCache);

            // Execute fragment shader - no quad context needed
            Shader.FragmentOutput fragmentOutput = Device.CurrentShader.FragmentShader(varyingCache, s_emptyContext);

            // Write output
            Device.CurrentFramebuffer.SetPixelUnsafe(x, y, fragmentOutput.GlFragColor);
            Device.CurrentFramebuffer.SetDepthUnsafe(x, y, fragmentOutput.GlFragDepth ?? depth);
        }
    }

    private void ProcessQuad(int quadX, int quadY, RasterTriangle triangle, EdgeData edgeData)
    {
        var quadFragments = new QuadFragment?[QUAD_SIZE, QUAD_SIZE];
        bool hasValidFragments = false;
        var varyingCache = s_varyingCache.Value;

        // Calculate edge values at quad corner (pixel center)
        float baseX = quadX + 0.5f;
        float baseY = quadY + 0.5f;

        float e0_base = edgeData.E0.Evaluate(baseX, baseY);
        float e1_base = edgeData.E1.Evaluate(baseX, baseY);
        float e2_base = edgeData.E2.Evaluate(baseX, baseY);

        // Pre-calculate step increments (these are the A and B coefficients)
        float e0_stepX = edgeData.E0.A;
        float e0_stepY = edgeData.E0.B;
        float e1_stepX = edgeData.E1.A;
        float e1_stepY = edgeData.E1.B;
        float e2_stepX = edgeData.E2.A;
        float e2_stepY = edgeData.E2.B;

        // First pass: barycentric calculation using incremental updates
        for (int dy = 0; dy < QUAD_SIZE; dy++)
        {
            // Calculate edge values for start of this row
            float e0_row = e0_base + dy * e0_stepY;
            float e1_row = e1_base + dy * e1_stepY;
            float e2_row = e2_base + dy * e2_stepY;

            for (int dx = 0; dx < QUAD_SIZE; dx++)
            {
                // Incrementally calculate edge values for this pixel
                float e0 = e0_row + dx * e0_stepX;
                float e1 = e1_row + dx * e1_stepX;
                float e2 = e2_row + dx * e2_stepX;

                // Convert to barycentric coordinates
                float w0 = e0 * edgeData.InvArea;
                float w1 = e1 * edgeData.InvArea;
                float w2 = e2 * edgeData.InvArea;

                // Check if pixel is inside triangle
                if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                {
                    float depth = InterpolateFloat(
                        triangle.Vertices[0].ScreenPosition.Z,
                        triangle.Vertices[1].ScreenPosition.Z,
                        triangle.Vertices[2].ScreenPosition.Z,
                        w0, w1, w2);

                    InterpolateVaryings(ref triangle, w0, w1, w2, varyingCache);

                    quadFragments[dy, dx] = new QuadFragment
                    {
                        Depth = depth,
                        Barycentric = new Float3(w0, w1, w2),
                        Varyings = varyingCache
                    };
                    hasValidFragments = true;
                }
            }
        }

        // Skip if no valid fragments in quad
        if (!hasValidFragments)
            return;

        // Second pass: Process fragments with neighbor information
        for (int dy = 0; dy < QUAD_SIZE; dy++)
        {
            for (int dx = 0; dx < QUAD_SIZE; dx++)
            {
                QuadFragment? fragment = quadFragments[dy, dx];
                if (fragment.HasValue == false)
                    continue;

                int x = quadX + dx;
                int y = quadY + dy;

                lock (Device.CurrentFramebuffer.GetPixelLockUnsafe(x, y))
                {
                    // Skip if failed depth test (unless shader can write depth)
                    if (Device.DoDepthTest && !Device.CurrentShader.CanWriteDepth)
                    {
                        if (fragment.Value.Depth >= Device.CurrentFramebuffer.GetDepth(x, y))
                            continue;
                    }

                    // Create fragment context with neighbor access
                    FragmentContext fragmentContext = new FragmentContext
                    {
                        CurrentFragment = fragment.Value,
                        QuadFragments = quadFragments,
                        QuadX = dx,
                        QuadY = dy,
                    };

                    // Execute fragment shader with quad context
                    Shader.FragmentOutput fragmentOutput = Device.CurrentShader.FragmentShader(fragment.Value.Varyings, fragmentContext);

                    // Write output
                    Device.CurrentFramebuffer.SetPixelUnsafe(
                        x, y,
                        fragmentOutput.GlFragColor);

                    Device.CurrentFramebuffer.SetDepthUnsafe(x, y, fragmentOutput.GlFragDepth ?? fragment.Value.Depth);
                }
            }
        }
    }

    private void InterpolateVaryings(ref RasterTriangle triangle, float barycentricX, float barycentricY, float barycentricZ, Float4[] varyingCache)
    {
        var v0Varyings = triangle.Vertices[0].Varyings;
        var v1Varyings = triangle.Vertices[1].Varyings;
        var v2Varyings = triangle.Vertices[2].Varyings;

        int numVaryings = v0Varyings.Length;
        for (int i = 0; i < numVaryings; i++)
        {
            ref var v0 = ref v0Varyings[i];
            ref var v1 = ref v1Varyings[i];
            ref var v2 = ref v2Varyings[i];

            varyingCache[i] = new Float4(
                v0.X * barycentricX + v1.X * barycentricY + v2.X * barycentricZ,
                v0.Y * barycentricX + v1.Y * barycentricY + v2.Y * barycentricZ,
                v0.Z * barycentricX + v1.Z * barycentricY + v2.Z * barycentricZ,
                v0.W * barycentricX + v1.W * barycentricY + v2.W * barycentricZ);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBackFace(Float3 v0, Float3 v1, Float3 v2)
    {
        float signedArea = (v1.X - v0.X) * (v2.Y - v0.Y) - (v1.Y - v0.Y) * (v2.X - v0.X);
        //float signedArea = v0.X * v1.Y - v0.Y * v1.X +
        //                   v1.X * v2.Y - v1.Y * v2.X +
        //                   v2.X * v0.Y - v2.Y * v0.X;
        return Device.CullMode == CullMode.Back ? signedArea >= 0 : signedArea <= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterpolateFloat(float a, float b, float c, float barycentricX, float barycentricY, float barycentricZ)
    {
        return a * barycentricX + b * barycentricY + c * barycentricZ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EdgeFunction(ref Float3 a, ref Float3 b, ref Float3 c)
    {
        return (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
    }
}

public struct QuadFragment
{
    public float Depth { get; set; }
    public Float3 Barycentric { get; set; }
    public Float4[] Varyings { get; set; }
}

public struct FragmentContext
{
    public QuadFragment CurrentFragment { get; set; }
    public QuadFragment?[,] QuadFragments { get; set; }
    public int QuadX { get; set; }
    public int QuadY { get; set; }

    public QuadFragment? GetNeighbor(int offsetX, int offsetY)
    {
        int newX = QuadX + offsetX;
        int newY = QuadY + offsetY;

        // Loop
        if (newX < 0) newX += 2;
        if (newY < 0) newY += 2;
        if (newX >= 2) newX -= 2;
        if (newY >= 2) newY -= 2;

        return QuadFragments[newY, newX];
    }

    public (float dFdx, float dFdy) CalculateDerivatives()
    {
        float current = CurrentFragment.Depth;

        // Normal non-stippled mode - use direct neighbors if available
        float right = GetNeighbor(1, 0)?.Depth ?? current;
        float bottom = GetNeighbor(0, 1)?.Depth ?? current;

        return (right - current, bottom - current);
    }
}
