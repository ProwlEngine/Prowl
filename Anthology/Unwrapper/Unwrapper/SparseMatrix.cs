namespace Prowl.Unwrapper;

/// <summary>
/// Sparse matrix in compressed sparse column (CSC) layout.
/// <para>
/// Storage: three parallel arrays.
/// <c>Values</c> holds non-zero values walked column-by-column,
/// <c>RowIndices</c> gives the row of each value, and
/// <c>ColumnRanges</c> has length <c>ColumnCount + 1</c> with
/// <c>ColumnRanges[c]..ColumnRanges[c+1]</c> the span of entries for column <c>c</c>.
/// </para>
/// </summary>
internal sealed class SparseMatrix
{
    public int RowCount;
    public int ColumnCount;
    public int NonZeroCount;

    public double[] Values = System.Array.Empty<double>();
    public int[] RowIndices = System.Array.Empty<int>();
    public int[] ColumnRanges = new int[1];

    public SparseMatrix() { }

    public SparseMatrix(int rowCount, int columnCount, int nonZeroCount)
    {
        Reset(rowCount, columnCount, nonZeroCount);
    }

    /// <summary>Drop previous contents and resize to the given shape.</summary>
    public void Reset(int rowCount, int columnCount, int nonZeroCount)
    {
        RowCount = rowCount;
        ColumnCount = columnCount;
        NonZeroCount = nonZeroCount;

        Values = new double[nonZeroCount];
        RowIndices = new int[nonZeroCount];
        ColumnRanges = new int[columnCount + 1];
    }

    /// <summary>y := M * x. <paramref name="y"/> must already be sized to <see cref="RowCount"/>.</summary>
    public static void Multiply(double[] y, SparseMatrix m, double[] x)
    {
        System.Array.Clear(y, 0, m.RowCount);
        for (int col = 0; col < m.ColumnCount; ++col)
        {
            int start = m.ColumnRanges[col];
            int end = m.ColumnRanges[col + 1];
            double xv = x[col];
            for (int k = start; k < end; ++k)
                y[m.RowIndices[k]] += m.Values[k] * xv;
        }
    }

    /// <summary>Fill <paramref name="result"/> with the transpose of <paramref name="source"/>.</summary>
    public static void Transpose(SparseMatrix source, SparseMatrix result)
    {
        result.Reset(source.ColumnCount, source.RowCount, source.NonZeroCount);

        // First pass: how many entries land in each row of source (= each column of result)?
        int[] perColumnCount = new int[source.RowCount];
        for (int col = 0; col < source.ColumnCount; ++col)
        {
            int start = source.ColumnRanges[col];
            int end = source.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
                ++perColumnCount[source.RowIndices[k]];
        }

        result.ColumnRanges[0] = 0;
        for (int col = 0; col < result.ColumnCount; ++col)
            result.ColumnRanges[col + 1] = result.ColumnRanges[col] + perColumnCount[col];

        // Second pass: walk source and place each entry at the correct slot of the transpose.
        System.Array.Clear(perColumnCount, 0, perColumnCount.Length);
        for (int col = 0; col < source.ColumnCount; ++col)
        {
            int start = source.ColumnRanges[col];
            int end = source.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
            {
                int newCol = source.RowIndices[k];
                int slot = result.ColumnRanges[newCol] + perColumnCount[newCol];

                result.RowIndices[slot] = col;
                result.Values[slot] = source.Values[k];

                ++perColumnCount[newCol];
            }
        }
    }

    /// <summary>
    /// Compute M * M^T into <paramref name="result"/>. Done in two passes: first the
    /// structure (so we know how much to allocate), then the numeric values.
    /// </summary>
    public static void MultiplyByTranspose(SparseMatrix m, SparseMatrix mT, SparseMatrix result)
    {
        // Discover the sparsity pattern by scanning column j of mT once per j and
        // unioning the rows of m at the indicated columns. `seen[row] = j` is the
        // "have we already noted this row in column j?" marker.
        int[] seen = new int[m.RowCount];
        for (int i = 0; i < seen.Length; ++i) seen[i] = -1;

        int nnz = 0;
        for (int col = 0; col < mT.ColumnCount; ++col)
        {
            int start = mT.ColumnRanges[col];
            int end = mT.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
            {
                int rowI = mT.RowIndices[k];
                int aStart = m.ColumnRanges[rowI];
                int aEnd = m.ColumnRanges[rowI + 1];
                for (int q = aStart; q < aEnd; ++q)
                {
                    int target = m.RowIndices[q];
                    if (seen[target] != col)
                    {
                        seen[target] = col;
                        ++nnz;
                    }
                }
            }
        }

        result.Reset(m.RowCount, m.RowCount, nnz);

        for (int i = 0; i < seen.Length; ++i) seen[i] = -1;
        nnz = 0;
        for (int col = 0; col < mT.ColumnCount; ++col)
        {
            result.ColumnRanges[col] = nnz;

            int start = mT.ColumnRanges[col];
            int end = mT.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
            {
                int rowI = mT.RowIndices[k];
                int aStart = m.ColumnRanges[rowI];
                int aEnd = m.ColumnRanges[rowI + 1];
                for (int q = aStart; q < aEnd; ++q)
                {
                    int target = m.RowIndices[q];
                    if (seen[target] != col)
                    {
                        seen[target] = col;
                        result.RowIndices[nnz] = target;
                        ++nnz;
                    }
                }
            }
        }
        result.ColumnRanges[mT.ColumnCount] = nnz;

        // Row indices within each column should be sorted for deterministic access later.
        for (int col = 0; col < mT.ColumnCount; ++col)
        {
            int from = result.ColumnRanges[col];
            int len = result.ColumnRanges[col + 1] - from;
            System.Array.Sort(result.RowIndices, from, len);
        }

        // Numeric pass: for each column j of the result, accumulate into a dense scratch
        // buffer and read it back out at the indices we already laid out above.
        double[] scratch = new double[m.RowCount];
        for (int col = 0; col < mT.ColumnCount; ++col)
        {
            int start = mT.ColumnRanges[col];
            int end = mT.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
            {
                int rowI = mT.RowIndices[k];
                int aStart = m.ColumnRanges[rowI];
                int aEnd = m.ColumnRanges[rowI + 1];
                for (int q = aStart; q < aEnd; ++q)
                    scratch[m.RowIndices[q]] += mT.Values[k] * m.Values[q];
            }

            int rStart = result.ColumnRanges[col];
            int rEnd = result.ColumnRanges[col + 1];
            for (int k = rStart; k < rEnd; ++k)
            {
                int idx = result.RowIndices[k];
                result.Values[k] = scratch[idx];
                scratch[idx] = 0.0;
            }
        }
    }

    /// <summary>Build the inverse Jacobi (diagonal) preconditioner for a square matrix.</summary>
    public static void BuildJacobiInverse(SparseMatrix m, SparseMatrix result)
    {
        if (m.RowCount != m.ColumnCount)
            throw new System.InvalidOperationException("Jacobi preconditioner needs a square matrix.");

        result.Reset(m.RowCount, m.RowCount, m.RowCount);

        result.ColumnRanges[0] = 0;
        for (int col = 0; col < result.ColumnCount; ++col)
        {
            result.ColumnRanges[col + 1] = result.ColumnRanges[col] + 1;
            result.RowIndices[col] = col;
            result.Values[col] = 0.0;
        }

        for (int col = 0; col < m.ColumnCount; ++col)
        {
            int start = m.ColumnRanges[col];
            int end = m.ColumnRanges[col + 1];
            for (int k = start; k < end; ++k)
            {
                if (m.RowIndices[k] == col)
                    result.Values[col] = 1.0 / m.Values[k];
            }
        }
    }
}
