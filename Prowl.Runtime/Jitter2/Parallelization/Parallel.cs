/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Diagnostics;

namespace Jitter2.Parallelization;

/// <summary>
/// Contains methods and structures used for parallelization within the Jitter Physics engine.
/// </summary>
public static class Parallel
{
    /// <summary>
    /// Represents a batch defined by a start index, an end index, and a batch index.
    /// This struct is utilized in <see cref="ForBatch"/> to facilitate multi-threaded batch processing within a for-loop.
    /// </summary>
    public readonly struct Batch
    {
        public Batch(int start, int end, ushort index = 0)
        {
            Start = start;
            End = end;
            BatchIndex = index;
        }

        public readonly int Start;
        public readonly int End;
        public readonly ushort BatchIndex;
    }

    /// <summary>
    /// Given the number of elements, the number of divisions into parts and a part index, returns
    /// the lower and upper bound for that part.
    /// </summary>
    public static void GetBounds(int numElements, int numDivisions, int part, out int start, out int end)
    {
        // Example:
        // numElements = 14, numDivisions = 4, part = {0, 1, 2, 3}
        // (div = 3, mod = 2)
        //
        //   p |   0    1    2    3
        //   _______________________
        //   s |   0    4    8   11
        //   e |   4    8   11   14
        Debug.Assert(part < numDivisions);

        int div = numElements / numDivisions;
        int mod = numElements - div * numDivisions;

        // int mod = numElements % numDivisions;
        // Maybe a candidate for Math.DivRem
        start = div * part;
        end = start + div;

        if (part < mod)
        {
            start += part;
            end += part + 1;
        }
        else
        {
            start += mod;
            end += mod;
        }
    }

    private static readonly ThreadPool threadPool = ThreadPool.Instance;

    /// <summary>
    /// Helper function utilizing <see cref="ThreadPool"/> to execute tasks
    /// parallel in batches.
    /// </summary>
    /// <param name="lower">Inclusive lower bound.</param>
    /// <param name="upper">Exclusive upper bound.</param>
    /// <param name="numTasks">The number of batches which should be created.</param>
    /// <param name="action">The callback function.</param>
    /// <param name="execute">True if <see cref="ThreadPool.Execute"/> should be called after adding the tasks.</param>
    public static void ForBatch(int lower, int upper, int numTasks, Action<Batch> action, bool execute = true)
    {
        Debug.Assert(numTasks <= ushort.MaxValue);
        for (int i = 0; i < numTasks; i++)
        {
            GetBounds(upper - lower, numTasks, i, out int start, out int end);
            threadPool.AddTask(action, new Batch(start + lower, end + lower, (ushort)i));
        }

        if (execute) threadPool.Execute();
    }
}