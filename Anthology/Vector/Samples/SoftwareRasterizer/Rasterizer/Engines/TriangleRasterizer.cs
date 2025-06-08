using System.Runtime.CompilerServices;

using Prowl.Vector;

using static SoftwareRasterizer.Rasterizer.GraphicsDevice;

namespace SoftwareRasterizer.Rasterizer.Engines;

internal class TriangleRasterizer(GraphicsDevice device) : RasterizerBase(device)
{
    private const int QUAD_SIZE = 2;
    private static readonly Float4[] s_varyingCache = new Float4[32];

    public override void Rasterize(RasterTriangle triangle)
    {
        Float3 v0 = triangle.Vertices[0].ScreenPosition;
        Float3 v1 = triangle.Vertices[1].ScreenPosition;
        Float3 v2 = triangle.Vertices[2].ScreenPosition;

        if (Device.CullMode != CullMode.None && IsBackFace(v0, v1, v2))
        {
            return;
        }

        // Pre-calculate edge equation constants only once
        float area = EdgeFunction(ref v0, ref v1, ref v2);
        if (area < 1e-6 && area > -1e-6)
            return;

        // Calculate bounds same as before
        int minX = (int)Maths.Max(Maths.Floor(Maths.Min(v0.X, Maths.Min(v1.X, v2.X))), 0);
        int maxX = (int)Maths.Min(Maths.Ceiling(Maths.Max(v0.X, Maths.Max(v1.X, v2.X))), Device.CurrentFramebuffer.Width - 1);
        int minY = (int)Maths.Max(Maths.Floor(Maths.Min(v0.Y, Maths.Min(v1.Y, v2.Y))), 0);
        int maxY = (int)Maths.Min(Maths.Ceiling(Maths.Max(v0.Y, Maths.Max(v1.Y, v2.Y))), Device.CurrentFramebuffer.Height - 1);

        // Align to quad boundaries
        minX &= ~(QUAD_SIZE - 1);
        minY &= ~(QUAD_SIZE - 1);
        maxX = (maxX + QUAD_SIZE - 1) & ~(QUAD_SIZE - 1);
        maxY = (maxY + QUAD_SIZE - 1) & ~(QUAD_SIZE - 1);

        int count = Maths.CeilToInt((double)(maxY - minY) / QUAD_SIZE);
        if (count > 0)
        {
            // Pre-calculate edge equation coefficients
            float invArea = 1.0f / area;
            var e0 = new EdgeCoefficients(v1, v2);  // edge v1->v2
            var e1 = new EdgeCoefficients(v2, v0);  // edge v2->v0
            var e2 = new EdgeCoefficients(v0, v1);  // edge v0->v1
            EdgeData edgeData = new(e0, e1, e2, invArea);

            for (int i = 0; i < count; i++) {
                int quadY = minY + (i * QUAD_SIZE);
                for (int quadX = minX; quadX < maxX; quadX += QUAD_SIZE)
                {
                    ProcessQuadOptimized(quadX, quadY, triangle, edgeData);
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
    private static Float3 CalculateBarycentric(float x, float y, ref EdgeData edges)
    {
        float w0 = edges.E0.Evaluate(x, y) * edges.InvArea;
        float w1 = edges.E1.Evaluate(x, y) * edges.InvArea;
        float w2 = edges.E2.Evaluate(x, y) * edges.InvArea;
        return new Float3(w0, w1, w2);
    }

    private void ProcessQuadOptimized(int quadX, int quadY, RasterTriangle triangle, EdgeData edgeData)
    {
        var quadFragments = new QuadFragment?[QUAD_SIZE, QUAD_SIZE];
        bool hasValidFragments = false;

        // First pass barycentric calculation
        for (int dy = 0; dy < QUAD_SIZE; dy++)
        {
            for (int dx = 0; dx < QUAD_SIZE; dx++)
            {
                int x = quadX + dx;
                int y = quadY + dy;

                Float3 barycentric = CalculateBarycentric(
                    x + 0.5f, y + 0.5f,  // Center of pixel
                    ref edgeData);

                if (barycentric.X >= 0 && barycentric.Y >= 0 && barycentric.Z >= 0)
                {
                    float depth = InterpolateFloat(
                        triangle.Vertices[0].ScreenPosition.Z,
                        triangle.Vertices[1].ScreenPosition.Z,
                        triangle.Vertices[2].ScreenPosition.Z,
                        ref barycentric);

                    InterpolateVaryings(ref triangle, ref barycentric);

                    quadFragments[dy, dx] = new QuadFragment
                    {
                        Depth = depth,
                        Barycentric = barycentric,
                        Varyings = s_varyingCache
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

    private void InterpolateVaryings(ref RasterTriangle triangle, ref Float3 barycentric)
    {
        RasterVertex v0 = triangle.Vertices[0];
        RasterVertex v1 = triangle.Vertices[1];
        RasterVertex v2 = triangle.Vertices[2];

        int numVaryings = v0.Varyings.Length;
        for (int i = 0; i < numVaryings; i++)
        {
            s_varyingCache[i] = InterpolateVector3(ref v0.Varyings[i], ref v1.Varyings[i], ref v2.Varyings[i], ref barycentric);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBackFace(Float3 v0, Float3 v1, Float3 v2)
    {
        float signedArea = v0.X * v1.Y - v0.Y * v1.X +
                           v1.X * v2.Y - v1.Y * v2.X +
                           v2.X * v0.Y - v2.Y * v0.X;

        return Device.CullMode == CullMode.Back ? signedArea >= 0 : signedArea <= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Float4 InterpolateVector3(ref Float4 a, ref Float4 b, ref Float4 c, ref Float3 barycentric)
    {
        return new Float4(
            InterpolateFloat(a.X, b.X, c.X, ref barycentric),
            InterpolateFloat(a.Y, b.Y, c.Y, ref barycentric),
            InterpolateFloat(a.Z, b.Z, c.Z, ref barycentric),
            InterpolateFloat(a.W, b.W, c.W, ref barycentric)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterpolateFloat(float a, float b, float c, ref Float3 barycentric)
    {
        return a * barycentric.X + b * barycentric.Y + c * barycentric.Z;
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
