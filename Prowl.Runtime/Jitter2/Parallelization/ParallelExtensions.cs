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
using Jitter2.DataStructures;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Parallelization;

/// <summary>
/// Provides a ParallelForBatch extension for <see cref="UnmanagedActiveList{T}"/> and <see
/// cref="UnmanagedActiveList{T}"/>.
/// </summary>
public static class ParallelExtensions
{
    /// <summary>
    /// Loop in batches over the active elements of the <see cref="UnmanagedActiveList{T}"/>.
    /// </summary>
    /// <param name="taskThreshold">If the number of elements is less than this value, only
    /// one batch is generated.</param>
    /// <param name="execute">True if <see cref="ThreadPool.Execute"/> should be called.</param>
    /// <returns>The number of batches(/tasks) generated.</returns>
    public static int ParallelForBatch<T>(this UnmanagedActiveList<T> list, int taskThreshold,
        Action<Parallel.Batch> action, bool execute = true) where T : unmanaged
    {
        int numTasks = list.Active.Length / taskThreshold + 1;
        numTasks = Math.Min(numTasks, ThreadPool.Instance.ThreadCount);

        Parallel.ForBatch(0, list.Active.Length, numTasks, action, execute);

        return numTasks;
    }

    /// <summary>
    /// Loop in batches over the active elements of the <see cref="ActiveList{T}"/>.
    /// </summary>
    /// <param name="taskThreshold">If the number of elements is less than this value, only
    /// one batch is generated.</param>
    /// <param name="execute">True if <see cref="ThreadPool.Execute"/> should be called.</param>
    /// <returns>The number of batches(/tasks) generated.</returns>
    public static int ParallelForBatch<T>(this ActiveList<T> list, int taskThreshold,
        Action<Parallel.Batch> action, bool execute = true) where T : class, IListIndex
    {
        int numTasks = list.Active / taskThreshold + 1;
        numTasks = Math.Min(numTasks, ThreadPool.Instance.ThreadCount);

        Parallel.ForBatch(0, list.Active, numTasks, action, execute);

        return numTasks;
    }
}