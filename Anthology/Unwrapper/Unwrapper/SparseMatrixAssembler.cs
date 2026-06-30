namespace Prowl.Unwrapper;

/// <summary>
/// Coordinate-format accumulator used while building the angle and conformal systems.
/// Callers <see cref="SetEntry"/> or <see cref="AccumulateEntry"/> by (row, col);
/// <see cref="Finalize"/> writes the resulting CSC matrix into a <see cref="SparseMatrix"/>.
/// </summary>
internal sealed class SparseMatrixAssembler
{
    // Keyed on a packed (col, row); column-major makes the per-column CSC layout phase cheap.
    private readonly LongDoubleMap _entries;

    public SparseMatrixAssembler(int expectedCount)
    {
        _entries = new LongDoubleMap(expectedCount);
    }

    public void Clear() => _entries.Clear();

    public void SetEntry(int row, int col, double value)
    {
        _entries.Set(PackKey(row, col), value);
    }

    public void AccumulateEntry(int row, int col, double value)
    {
        _entries.Add(PackKey(row, col), value);
    }

    /// <summary>Emit the accumulated entries as a CSC matrix with the given shape.</summary>
    public void Finalize(SparseMatrix result, int rowCount, int columnCount)
    {
        result.Reset(rowCount, columnCount, _entries.Count);

        // Tally entries per column to lay out ColumnRanges.
        int[] perColumnCount = new int[columnCount];
        var it = _entries.GetEnumerator();
        while (it.MoveNext())
        {
            UnpackKey(it.Current.Key, out _, out int col);
            ++perColumnCount[col];
        }

        result.ColumnRanges[0] = 0;
        for (int col = 0; col < columnCount; ++col)
            result.ColumnRanges[col + 1] = result.ColumnRanges[col] + perColumnCount[col];

        // Drop row indices into place, ignoring order until the sort below.
        System.Array.Clear(perColumnCount, 0, perColumnCount.Length);
        it = _entries.GetEnumerator();
        while (it.MoveNext())
        {
            UnpackKey(it.Current.Key, out int row, out int col);
            int slot = result.ColumnRanges[col] + perColumnCount[col];
            result.RowIndices[slot] = row;
            ++perColumnCount[col];
        }

        // Sort by row within each column, then look up values to fill in.
        for (int col = 0; col < columnCount; ++col)
        {
            int from = result.ColumnRanges[col];
            int len = result.ColumnRanges[col + 1] - from;
            System.Array.Sort(result.RowIndices, from, len);

            for (int k = from; k < from + len; ++k)
                result.Values[k] = _entries.GetOrThrow(PackKey(result.RowIndices[k], col));
        }
    }

    private static long PackKey(int row, int col) => ((long)col << 32) | (uint)row;

    private static void UnpackKey(long key, out int row, out int col)
    {
        row = (int)(key & 0xFFFFFFFFL);
        col = (int)(key >> 32);
    }
}
