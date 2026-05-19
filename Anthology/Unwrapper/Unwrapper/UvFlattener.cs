using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Per-chart UV flattener. First runs angle-based flattening (LinABF) to find consistent
/// triangle angles, then a conformal least-squares solve (LSCM) to recover 2D positions.
/// </summary>
internal sealed class UvFlattener
{
    public const double DefaultTolerance = 1e-8;
    public const double DefaultIterationBudget = 25.0;

    private readonly MeshRegion _region;
    private readonly HalfEdgeMesh _workMesh = new();
    private int[] _vertexBackMap = System.Array.Empty<int>();

    // Angle system: one variable per triangle corner.
    private SparseMatrix _angleMatrix = new();
    private double[] _angleRhs = System.Array.Empty<double>();
    private int[] _cornerToVar = System.Array.Empty<int>();
    private double[] _initialAngle = System.Array.Empty<double>();
    private double[] _solvedAngle = System.Array.Empty<double>();

    // Conformal system: two variables per non-locked vertex.
    private SparseMatrix _conformalMatrix = new();
    private double[] _conformalRhs = System.Array.Empty<double>();
    private double[] _uv = System.Array.Empty<double>();

    // Solver + scratch matrices kept alive across calls so repeated solves (every merge trial,
    // every flatten) don't allocate fresh buffers.
    private readonly CgSolver _cg = new();
    private readonly SparseMatrix _transpose = new();
    private readonly SparseMatrix _normalEqs = new();
    private readonly SparseMatrix _jacobi = new();
    private readonly SparseMatrixAssembler _assembler = new(64);

    private Double3 _origin3D;

    public UvFlattener(MeshRegion region) => _region = region;

    /// <summary>
    /// Build a private working mesh from the region (so we don't perturb the parent),
    /// then recentre the geometry so the solver works in well-scaled coordinates.
    /// </summary>
    public void Setup()
    {
        var vertexList = new List<int>(3 * _region.Triangles.Length);
        var vertexLookup = new Dictionary<int, int>(3 * _region.Triangles.Length);
        _region.CollectVertices(vertexList, vertexLookup);
        _workMesh.BuildFromRegion(_region, vertexList, vertexLookup);

        _vertexBackMap = new int[_region.Mesh.Vertices.Count];
        for (int i = 0; i < _vertexBackMap.Length; ++i) _vertexBackMap[i] = -1;
        foreach (var kv in vertexLookup) _vertexBackMap[kv.Key] = kv.Value;

        int vCount = _workMesh.Vertices.Count;
        _origin3D = default;
        double invN = 1.0 / vCount;
        for (int v = 0; v < vCount; ++v) _origin3D += _workMesh.Vertices[v].Position * invN;
        for (int v = 0; v < vCount; ++v) _workMesh.Vertices[v].Position -= _origin3D;
    }

    /// <summary>
    /// Assemble the LinABF sparse system. Three constraint blocks: vertex consistency (interior
    /// star angles must sum to 2π), triangle consistency (per-face corners must sum to π), and
    /// wheel consistency (log-sine product around each interior vertex). All premultiplied by Da
    /// so values stay scaled.
    /// </summary>
    public void BuildAngleSystem()
    {
        int cornerCount = 3 * _workMesh.Triangles.Count;
        _cornerToVar = new int[cornerCount];
        for (int i = 0; i < cornerCount; ++i) _cornerToVar[i] = -1;
        _initialAngle = new double[cornerCount];
        _solvedAngle = new double[cornerCount];

        // Number every corner; the index gives this corner's variable slot.
        int nextVar = 0;
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            HalfEdge edge = _workMesh.Triangles[faceI].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                _cornerToVar[_workMesh.IndexOf(edge)] = nextVar++;
                edge = edge.Next!;
            }
        }

        int interiorVertexCount = 0;
        // Initial estimate: weight star corner angles around each interior vertex to sum to 2π.
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            Vertex v = _workMesh.Vertices[vertexI];
            double ratio = 1.0;
            if (!MeshOps.TouchesBorder(v))
            {
                ratio = (2.0 * System.Math.PI) / SumStarAngles(v);
                ++interiorVertexCount;
            }

            HalfEdge edge = v.IncidentEdge!;
            HalfEdge start = edge;
            do
            {
                if (!MeshOps.IsBorderEdge(edge))
                    _initialAngle[_cornerToVar[_workMesh.IndexOf(edge)]] = MeshOps.CornerAngleAt(edge) * ratio;
                edge = MeshOps.StepAroundVertex(edge);
            } while (edge != start);
        }

        // Total constraint count: 2 per interior vertex + 1 per face.
        int constraintCount = 2 * interiorVertexCount + _workMesh.Triangles.Count;
        _angleRhs = new double[constraintCount];

        var assembler = _assembler;
        assembler.Clear();
        int row = 0;

        // ---- Block 1: vertex consistency ----
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            Vertex v = _workMesh.Vertices[vertexI];
            if (MeshOps.TouchesBorder(v)) continue;

            _angleRhs[row] = 2.0 * System.Math.PI;
            HalfEdge edge = v.IncidentEdge!;
            HalfEdge start = edge;
            do
            {
                int varI = _cornerToVar[_workMesh.IndexOf(edge)];
                assembler.SetEntry(row, varI, 1.0 * _initialAngle[varI]);
                _angleRhs[row] -= _initialAngle[varI];
                edge = MeshOps.StepAroundVertex(edge);
            } while (edge != start);
            ++row;
        }

        // ---- Block 2: triangle consistency ----
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            _angleRhs[row] = System.Math.PI;
            HalfEdge edge = _workMesh.Triangles[faceI].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                int varI = _cornerToVar[_workMesh.IndexOf(edge)];
                assembler.SetEntry(row, varI, 1.0 * _initialAngle[varI]);
                _angleRhs[row] -= _initialAngle[varI];
                edge = edge.Next!;
            }
            ++row;
        }

        // ---- Block 3: wheel consistency ----
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            Vertex v = _workMesh.Vertices[vertexI];
            if (MeshOps.TouchesBorder(v)) continue;

            _angleRhs[row] = 0.0;
            HalfEdge edge = v.IncidentEdge!;
            HalfEdge start = edge;
            do
            {
                int nextVarI = _cornerToVar[_workMesh.IndexOf(edge.Next!)];
                int prevVarI = _cornerToVar[_workMesh.IndexOf(edge.Previous!)];

                double aNext = _initialAngle[nextVarI];
                double aPrev = _initialAngle[prevVarI];

                assembler.SetEntry(row, nextVarI, aNext * (System.Math.Cos(aNext) / System.Math.Sin(aNext)));
                assembler.SetEntry(row, prevVarI, aPrev * (-System.Math.Cos(aPrev) / System.Math.Sin(aPrev)));
                _angleRhs[row] += System.Math.Log(System.Math.Max(System.Math.Sin(aPrev), NumericHelpers.Tiny))
                                - System.Math.Log(System.Math.Max(System.Math.Sin(aNext), NumericHelpers.Tiny));
                edge = MeshOps.StepAroundVertex(edge);
            } while (edge != start);
            ++row;
        }

        assembler.Finalize(_angleMatrix, constraintCount, 3 * _workMesh.Triangles.Count);
    }

    /// <summary>Solve C C^T y = b via preconditioned CG, then recover alpha = alpha0 * (1 + C^T y).</summary>
    public void SolveAngleSystem(double tolerance, double iterationBudget)
    {
        SparseMatrix.Transpose(_angleMatrix, _transpose);
        SparseMatrix.MultiplyByTranspose(_angleMatrix, _transpose, _normalEqs);
        SparseMatrix.BuildJacobiInverse(_normalEqs, _jacobi);

        double[] dualVar = new double[_normalEqs.ColumnCount];
        _cg.Solve(_normalEqs.ColumnCount, _normalEqs, _jacobi, _angleRhs, dualVar, tolerance, (int)(iterationBudget * _normalEqs.ColumnCount));
        SparseMatrix.Multiply(_solvedAngle, _transpose, dualVar);

        int cornerCount = 3 * _workMesh.Triangles.Count;
        for (int a = 0; a < cornerCount; ++a)
            _solvedAngle[a] = _initialAngle[a] * (1.0 + _solvedAngle[a]);
    }

    public void EvaluateAngularDistortion(out DistortionStats stats)
        => DistortionMetrics.EvaluateAngularOnly(_workMesh, _solvedAngle, out stats);

    /// <summary>
    /// Run LSCM with the LinABF-computed angles. Picks an initial PCA-like projection, locks the
    /// two extremes along the dominant axis, and solves a least-squares conformal system.
    /// </summary>
    public void SolveConformalLayout(double tolerance, double iterationBudget)
    {
        _uv = new double[2 * _workMesh.Vertices.Count];
        _conformalRhs = new double[2 * _workMesh.Vertices.Count - 4];

        var rowBuffer = new ConformalRowBuffer();
        var assembler = _assembler;
        assembler.Clear();

        int[] lockedVertex = { -1, -1 };
        ComputeInitialProjection(_uv, lockedVertex, rowBuffer);

        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            HalfEdge edge = _workMesh.Triangles[faceI].FirstEdge!;
            int[] vertexIndex =
            {
                _workMesh.IndexOf(edge.Apex!),
                _workMesh.IndexOf(edge.Next!.Apex!),
                _workMesh.IndexOf(edge.Next!.Next!.Apex!),
            };
            double[] vertexAngle = { _solvedAngle[3 * faceI + 0], _solvedAngle[3 * faceI + 1], _solvedAngle[3 * faceI + 2] };
            double[] vertexSin = { System.Math.Sin(vertexAngle[0]), System.Math.Sin(vertexAngle[1]), System.Math.Sin(vertexAngle[2]) };

            // Rotate (right shift) so the vertex with the largest sine sits at slot 2 —
            // numerically friendlier for the coefficient construction below.
            if (vertexSin[0] > vertexSin[1] && vertexSin[0] > vertexSin[2])
            {
                RotateRight(vertexIndex); RotateRight(vertexAngle); RotateRight(vertexSin);
                RotateRight(vertexIndex); RotateRight(vertexAngle); RotateRight(vertexSin);
            }
            else if (vertexSin[1] > vertexSin[0] && vertexSin[1] > vertexSin[2])
            {
                RotateRight(vertexIndex); RotateRight(vertexAngle); RotateRight(vertexSin);
            }

            double sScale = System.Math.Sin(vertexAngle[1]) / System.Math.Sin(vertexAngle[2]);
            double cFx = System.Math.Cos(vertexAngle[0]) * sScale;
            double cFy = System.Math.Sin(vertexAngle[0]) * sScale;

            double weight = System.Math.Sqrt(_workMesh.FaceAttributes[faceI].Area);

            // Real-part equation (the "u" half of the conformal complex map).
            double[] uRow = { 1.0 - cFx, cFy, cFx, -cFy };
            PushVertexCoefficient(rowBuffer, vertexIndex[0], uRow, 0, _uv, 2 * vertexIndex[0]);
            PushVertexCoefficient(rowBuffer, vertexIndex[1], uRow, 2, _uv, 2 * vertexIndex[1]);
            PushScalarCoefficient(rowBuffer, 2 * vertexIndex[2] + 0, -1.0, _uv[2 * vertexIndex[2] + 0]);
            CommitRow(rowBuffer, assembler, weight);

            // Imaginary-part equation (the "v" half).
            double[] vRow = { -cFy, 1.0 - cFx, cFy, cFx };
            PushVertexCoefficient(rowBuffer, vertexIndex[0], vRow, 0, _uv, 2 * vertexIndex[0]);
            PushVertexCoefficient(rowBuffer, vertexIndex[1], vRow, 2, _uv, 2 * vertexIndex[1]);
            PushScalarCoefficient(rowBuffer, 2 * vertexIndex[2] + 1, -1.0, _uv[2 * vertexIndex[2] + 1]);
            CommitRow(rowBuffer, assembler, weight);
        }

        assembler.Finalize(_conformalMatrix, _conformalRhs.Length, _conformalRhs.Length);

        SparseMatrix.BuildJacobiInverse(_conformalMatrix, _jacobi);

        // Initial guess copied from the PCA projection, locked verts excluded.
        var guess = new List<double>(_conformalMatrix.ColumnCount);
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            if (vertexI != lockedVertex[0] && vertexI != lockedVertex[1])
            {
                guess.Add(_uv[2 * vertexI + 0]);
                guess.Add(_uv[2 * vertexI + 1]);
            }
        }
        double[] x = guess.ToArray();

        _cg.Solve(_conformalMatrix.ColumnCount, _conformalMatrix, _jacobi, _conformalRhs, x, tolerance, (int)(iterationBudget * _conformalMatrix.ColumnCount));

        // Splat solution back into _uv, skipping locked slots.
        int read = 0;
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            if (vertexI != lockedVertex[0] && vertexI != lockedVertex[1])
            {
                _uv[2 * vertexI + 0] = x[read++];
                _uv[2 * vertexI + 1] = x[read++];
            }
        }
    }

    /// <summary>Pull computed UVs out of the working mesh and into the supplied chart.</summary>
    public void ExtractUvs(UvChart chart)
    {
        for (int faceI = 0; faceI < _region.Triangles.Length; ++faceI)
        {
            int srcFace = _region.Triangles[faceI];
            HalfEdge e0 = _region.Mesh.Triangles[srcFace].FirstEdge!;
            HalfEdge e1 = e0.Next!;
            HalfEdge e2 = e1.Next!;

            int s0 = _vertexBackMap[_region.Mesh.IndexOf(e0.Apex!)];
            int s1 = _vertexBackMap[_region.Mesh.IndexOf(e1.Apex!)];
            int s2 = _vertexBackMap[_region.Mesh.IndexOf(e2.Apex!)];

            chart.UVs[3 * faceI + 0] = new Double2(_uv[2 * s0 + 0], _uv[2 * s0 + 1]);
            chart.UVs[3 * faceI + 1] = new Double2(_uv[2 * s1 + 0], _uv[2 * s1 + 1]);
            chart.UVs[3 * faceI + 2] = new Double2(_uv[2 * s2 + 0], _uv[2 * s2 + 1]);
        }

        chart.Origin3D = _origin3D;
        chart.RefreshUvArea();
    }

    public void ExtractAndFinaliseUvs(UvChart chart)
    {
        ExtractUvs(chart);
        chart.TightenAndOrient();
        RescaleToSurfaceArea(chart);
    }

    /// <summary>Project geometry along its two longest extents; lock the extreme U vertices.</summary>
    private void ComputeInitialProjection(double[] uv, int[] lockedVertex, ConformalRowBuffer rowBuffer)
    {
        Double3 vMin = new(1e32, 1e32, 1e32), vMax = new(-1e32, -1e32, -1e32);
        for (int v = 0; v < _workMesh.Vertices.Count; ++v)
        {
            Double3 p = _workMesh.Vertices[v].Position;
            if (p.X < vMin.X) vMin.X = p.X;
            if (p.Y < vMin.Y) vMin.Y = p.Y;
            if (p.Z < vMin.Z) vMin.Z = p.Z;
            if (p.X > vMax.X) vMax.X = p.X;
            if (p.Y > vMax.Y) vMax.Y = p.Y;
            if (p.Z > vMax.Z) vMax.Z = p.Z;
        }
        Double3 ext = vMax - vMin;
        Double3[] basis = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
        // 3-element bubble sort by extent.
        SwapByExtent(ref ext.X, ref ext.Y, ref basis[0], ref basis[1]);
        SwapByExtent(ref ext.Y, ref ext.Z, ref basis[1], ref basis[2]);
        SwapByExtent(ref ext.X, ref ext.Y, ref basis[0], ref basis[1]);

        Double3 axisU = basis[2];
        Double3 axisV = basis[1];

        // Ensure the basis is right-handed relative to the first face's winding;
        // LSCM handles mirrored initial guesses badly.
        {
            HalfEdge edge = _workMesh.Triangles[0].FirstEdge!;
            Double3 t0 = edge.Apex!.Position;
            Double3 t1 = edge.Next!.Apex!.Position;
            Double3 t2 = edge.Next!.Next!.Apex!.Position;
            Double3 axisW = Double3.Cross(axisU, axisV);
            Double3 triNormal = Double3.Cross(t1 - t0, t2 - t0);
            if (Double3.Dot(axisW, triNormal) < 0.0) axisU = -axisU;
        }

        double uMin = 1e32, uMax = -1e32;
        for (int vertexI = 0; vertexI < _workMesh.Vertices.Count; ++vertexI)
        {
            Double3 p = _workMesh.Vertices[vertexI].Position;
            double vu = Double3.Dot(p, axisU);
            double vv = Double3.Dot(p, axisV);
            uv[2 * vertexI + 0] = vu;
            uv[2 * vertexI + 1] = vv;
            if (vu < uMin) { lockedVertex[0] = vertexI; uMin = vu; }
            if (vu > uMax) { lockedVertex[1] = vertexI; uMax = vu; }
        }

        if (lockedVertex[0] > lockedVertex[1]) (lockedVertex[0], lockedVertex[1]) = (lockedVertex[1], lockedVertex[0]);

        rowBuffer.LockVertex0 = lockedVertex[0];
        rowBuffer.LockVertex1 = lockedVertex[1];
        rowBuffer.LockVar0 = 2 * lockedVertex[0];
        rowBuffer.LockVar1 = 2 * lockedVertex[0] + 1;
        rowBuffer.LockVar2 = 2 * lockedVertex[1];
        rowBuffer.LockVar3 = 2 * lockedVertex[1] + 1;
    }

    private static void SwapByExtent(ref double a, ref double b, ref Double3 b1, ref Double3 b2)
    {
        if (NumericHelpers.ApproxGreater(a, b, 1e-6))
        {
            (a, b) = (b, a);
            (b1, b2) = (b2, b1);
        }
    }

    /// <summary>
    /// Add a single (index, value) term to the row. Locked vars go into a separate bucket;
    /// otherwise the index is collapsed past any locked slots before being recorded.
    /// </summary>
    private static void PushScalarCoefficient(ConformalRowBuffer buf, int index, double value, double currentVarValue)
    {
        if (index == buf.LockVar0 || index == buf.LockVar1 || index == buf.LockVar2 || index == buf.LockVar3)
        {
            buf.Locked.Add((index, value));
            buf.LockedValues.Add((index, currentVarValue));
        }
        else
        {
            if (index > buf.LockVar3) index -= 4;
            else if (index > buf.LockVar2) index -= 3;
            else if (index > buf.LockVar1) index -= 2;
            else if (index > buf.LockVar0) index -= 1;
            buf.Free.Add((index, value));
        }
    }

    /// <summary>Two-component variant: pushes both (2v, 2v+1) at once.</summary>
    private static void PushVertexCoefficient(ConformalRowBuffer buf, int vertexIndex, double[] values, int valueOffset, double[] currentVars, int currentVarOffset)
    {
        if (vertexIndex == buf.LockVertex0 || vertexIndex == buf.LockVertex1)
        {
            buf.Locked.Add((2 * vertexIndex + 0, values[valueOffset + 0]));
            buf.Locked.Add((2 * vertexIndex + 1, values[valueOffset + 1]));
            buf.LockedValues.Add((2 * vertexIndex + 0, currentVars[currentVarOffset + 0]));
            buf.LockedValues.Add((2 * vertexIndex + 1, currentVars[currentVarOffset + 1]));
        }
        else
        {
            int idx = vertexIndex;
            if (idx > buf.LockVertex1) idx -= 2;
            else if (idx > buf.LockVertex0) idx -= 1;
            buf.Free.Add((2 * idx + 0, values[valueOffset + 0]));
            buf.Free.Add((2 * idx + 1, values[valueOffset + 1]));
        }
    }

    /// <summary>Materialise the row into normal-equation entries and update the RHS.</summary>
    private void CommitRow(ConformalRowBuffer buf, SparseMatrixAssembler assembler, double weight)
    {
        double w2 = weight * weight;
        for (int i = 0; i < buf.Free.Count; ++i)
        {
            for (int j = 0; j < buf.Free.Count; ++j)
            {
                assembler.AccumulateEntry(buf.Free[i].Index, buf.Free[j].Index,
                    buf.Free[i].Value * buf.Free[j].Value * w2);
            }
        }

        // Locked contributions move to the right-hand side.
        double s = 0.0;
        for (int i = 0; i < buf.Locked.Count; ++i)
            s += buf.Locked[i].Value * buf.LockedValues[i].Value;

        s *= w2;
        for (int i = 0; i < buf.Free.Count; ++i)
            _conformalRhs[buf.Free[i].Index] -= s * buf.Free[i].Value;

        buf.Free.Clear();
        buf.Locked.Clear();
        buf.LockedValues.Clear();
    }

    /// <summary>Scale the UV layout so its area roughly matches the 3D area it came from.</summary>
    private void RescaleToSurfaceArea(UvChart chart)
    {
        double scale = chart.UvArea < NumericHelpers.FloatTiny ? 1.0 : System.Math.Sqrt(chart.SurfaceArea / chart.UvArea);
        for (int f = 0; f < chart.Region!.Triangles.Length; ++f)
        {
            chart.UVs[3 * f + 0] *= scale;
            chart.UVs[3 * f + 1] *= scale;
            chart.UVs[3 * f + 2] *= scale;
        }
        chart.UvMin *= scale;
        chart.UvMax *= scale;
        chart.RefreshUvArea();
    }

    private static double SumStarAngles(Vertex v)
    {
        double sum = 0.0;
        HalfEdge edge = v.IncidentEdge!;
        HalfEdge start = edge;
        do
        {
            sum += MeshOps.CornerAngleAt(edge);
            edge = MeshOps.StepAroundVertex(edge);
        } while (edge != start);
        return sum;
    }

    // Right rotation: (a, b, c) -> (c, a, b). After up to two of these, the largest sine sits at slot 2.
    private static void RotateRight<T>(T[] a) { var tmp = a[2]; a[2] = a[1]; a[1] = a[0]; a[0] = tmp; }
}

/// <summary>Scratch storage for assembling one LSCM row at a time.</summary>
internal sealed class ConformalRowBuffer
{
    public List<(int Index, double Value)> Free = new(6);
    public List<(int Index, double Value)> Locked = new(6);
    public List<(int Index, double Value)> LockedValues = new(6);

    public int LockVertex0, LockVertex1;
    public int LockVar0, LockVar1, LockVar2, LockVar3;
}
