// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;

namespace Prowl.Runtime;

/// <summary>
/// Which layers collide with which, as a symmetric 32x32 bit matrix.
/// <para/>
/// Process-global rather than per <see cref="PhysicsWorld"/>, so every scene shares one matrix. That is
/// deliberate (the editor exposes it as a project setting), but it does mean two scenes cannot disagree.
/// </summary>
public static class CollisionMatrix
{
    public const int LayerCount = 32;

    // Copy-on-write. LayerFilter reads this from Jitter's broad-phase worker threads while the settings
    // UI writes it from the main thread; readers take the array reference once, so a multi-row update
    // like SetAllCollisions is never observed half applied.
    private static uint[] s_rows = CreateRows(true);

    /// <summary>
    /// Whether two layers collide. Out-of-range layers never collide, rather than throwing on a hot path
    /// that runs for every broad-phase pair.
    /// </summary>
    public static bool GetLayerCollision(int layer1, int layer2)
    {
        if (!InRange(layer1) || !InRange(layer2)) return false;

        return (Volatile.Read(ref s_rows)[layer1] & Bit(layer2)) != 0;
    }

    /// <summary>Sets whether two layers collide. Always applied symmetrically.</summary>
    public static void SetLayerCollision(int layer1Index, int layer2Index, bool shouldCollide)
    {
        if (!InRange(layer1Index) || !InRange(layer2Index)) return;

        uint[] rows = Snapshot();
        Apply(rows, layer1Index, layer2Index, shouldCollide);
        Apply(rows, layer2Index, layer1Index, shouldCollide);
        Publish(rows);
    }

    /// <summary>Sets whether a layer collides with every layer, itself included.</summary>
    public static void SetLayerCollisions(int layer, bool shouldCollide)
    {
        if (!InRange(layer)) return;

        uint[] rows = Snapshot();
        rows[layer] = shouldCollide ? uint.MaxValue : 0u;
        for (int other = 0; other < LayerCount; other++)
            Apply(rows, other, layer, shouldCollide);

        Publish(rows);
    }

    /// <summary>Sets every pair at once.</summary>
    public static void SetAllCollisions(bool shouldCollide) => Publish(CreateRows(shouldCollide));

    /// <summary>
    /// Replaces the whole matrix from packed rows, one bit per pair. Loading it a pair at a time would
    /// copy the matrix once per bit, and would let a reader catch it half applied.
    /// <para/>
    /// The result is forced symmetric: filtering asks about a pair in whatever order the broad phase
    /// produced it, so an asymmetric matrix would make collisions depend on that order.
    /// </summary>
    public static void SetRows(ReadOnlySpan<uint> rows)
    {
        uint[] next = CreateRows(false);

        int count = Math.Min(rows.Length, LayerCount);
        for (int i = 0; i < count; i++)
            next[i] = rows[i];

        Symmetrize(next);
        Publish(next);
    }

    /// <summary>The matrix as packed rows, one bit per pair.</summary>
    public static uint[] GetRows() => (uint[])Volatile.Read(ref s_rows).Clone();

    /// <summary>Restores the engine default, where every layer collides with every other.</summary>
    public static void Reset() => SetAllCollisions(true);

    /// <summary>Which layers the given one collides with, indexed by layer.</summary>
    public static bool[] GetLayerCollisions(int layer)
    {
        var result = new bool[LayerCount];
        if (!InRange(layer)) return result;

        uint row = Volatile.Read(ref s_rows)[layer];
        for (int other = 0; other < LayerCount; other++)
            result[other] = (row & Bit(other)) != 0;

        return result;
    }

    /// <summary>
    /// Forces the matrix symmetric by taking the union of the two triangles. Every setter here is
    /// already symmetric, so this only matters after a matrix was assembled elsewhere.
    /// </summary>
    public static void EnsureSymmetric()
    {
        uint[] rows = Snapshot();
        Symmetrize(rows);
        Publish(rows);
    }

    // Union of the two triangles, so a pair collides if either direction said so.
    private static void Symmetrize(uint[] rows)
    {
        for (int row = 0; row < LayerCount; row++)
            for (int col = row + 1; col < LayerCount; col++)
                if (((rows[row] & Bit(col)) != 0) || ((rows[col] & Bit(row)) != 0))
                {
                    rows[row] |= Bit(col);
                    rows[col] |= Bit(row);
                }
    }

    private static bool InRange(int layer) => (uint)layer < LayerCount;

    private static uint Bit(int layer) => 1u << layer;

    private static void Apply(uint[] rows, int row, int col, bool shouldCollide)
    {
        if (shouldCollide) rows[row] |= Bit(col);
        else rows[row] &= ~Bit(col);
    }

    private static uint[] CreateRows(bool shouldCollide)
    {
        var rows = new uint[LayerCount];
        if (shouldCollide) Array.Fill(rows, uint.MaxValue);
        return rows;
    }

    // Mutate a private copy, then swap it in, so no reader ever sees a partial update.
    private static uint[] Snapshot() => (uint[])Volatile.Read(ref s_rows).Clone();

    private static void Publish(uint[] rows) => Volatile.Write(ref s_rows, rows);
}
