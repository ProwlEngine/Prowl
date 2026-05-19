namespace Prowl.Unwrapper;

/// <summary>
/// Preconditioned conjugate-gradient solver for sparse, symmetric, positive-definite systems.
/// Drives both the angle-based flattening solve and the conformal-map least-squares solve.
/// </summary>
/// <remarks>
/// Instances cache the four N-sized scratch buffers (<c>r</c>, <c>p</c>, <c>z</c>, <c>g</c>)
/// and grow them on demand. Reusing one <see cref="CgSolver"/> across many solves with similar
/// sizes avoids the per-call allocation cost — significant when the merge loop runs hundreds
/// of small CG solves per region.
/// </remarks>
internal sealed class CgSolver
{
    private double[] _residual = System.Array.Empty<double>();
    private double[] _direction = System.Array.Empty<double>();
    private double[] _preconditioned = System.Array.Empty<double>();
    private double[] _scratch = System.Array.Empty<double>();

    private void EnsureCapacity(int n)
    {
        if (_residual.Length < n) _residual = new double[n];
        if (_direction.Length < n) _direction = new double[n];
        if (_preconditioned.Length < n) _preconditioned = new double[n];
        if (_scratch.Length < n) _scratch = new double[n];
    }

    /// <summary>
    /// Solve <c>A * x = b</c>. <paramref name="x"/> is the initial guess on entry and the
    /// solution on return; the number of iterations actually executed is returned.
    /// </summary>
    public int Solve(int n, SparseMatrix a, SparseMatrix? preconditioner, double[] b, double[] x, double epsilon, int maxIterations)
    {
        EnsureCapacity(n);

        double bNormSquared = VectorOps.InnerProduct(n, b, b);
        double stopAt = epsilon * epsilon * bNormSquared;

        // r0 = b - A * x0
        SparseMatrix.Multiply(_residual, a, x);
        VectorOps.AddScaled(n, -1.0, b, _residual);
        VectorOps.ScaleInPlace(n, -1.0, _residual);

        ApplyPreconditioner(n, _preconditioned, preconditioner, _residual);
        VectorOps.Assign(n, _preconditioned, _direction);

        int iter = 0;
        while (VectorOps.InnerProduct(n, _residual, _residual) > stopAt && iter < maxIterations)
        {
            double rDotZ = VectorOps.InnerProduct(n, _residual, _preconditioned);

            SparseMatrix.Multiply(_scratch, a, _direction);
            double pDotAp = VectorOps.InnerProduct(n, _direction, _scratch);
            if (pDotAp == 0.0) break;

            double alpha = rDotZ / pDotAp;
            VectorOps.AddScaled(n, alpha, _direction, x);

            VectorOps.ScaleInPlace(n, -alpha, _scratch);
            VectorOps.AddScaled(n, 1.0, _residual, _scratch);
            VectorOps.Assign(n, _scratch, _residual);

            ApplyPreconditioner(n, _preconditioned, preconditioner, _residual);

            double newRdotZ = VectorOps.InnerProduct(n, _residual, _preconditioned);
            if (rDotZ == 0.0) break;
            double beta = newRdotZ / rDotZ;

            VectorOps.ScaleInPlace(n, beta, _direction);
            VectorOps.AddScaled(n, 1.0, _preconditioned, _direction);

            ++iter;
        }

        return iter;
    }

    private static void ApplyPreconditioner(int n, double[] output, SparseMatrix? preconditioner, double[] input)
    {
        if (preconditioner is null)
            VectorOps.Assign(n, input, output);
        else
            SparseMatrix.Multiply(output, preconditioner, input);
    }
}
