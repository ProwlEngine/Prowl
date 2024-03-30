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

using System.Threading;

namespace Jitter2.Parallelization;

/// <summary>
/// An efficient reader-writer lock implementation optimized
/// for rare write events.
/// </summary>
public struct ReaderWriterLock
{
    private volatile int writer;
    private volatile int reader;

    /// <summary>
    /// Enters the critical read section.
    /// </summary>
    public void EnterReadLock()
    {
        while (true)
        {
            while (writer == 1) Thread.SpinWait(1);

            Interlocked.Increment(ref reader);
            if (writer == 0) break;
            Interlocked.Decrement(ref reader);
        }
    }

    /// <summary>
    /// Enters the critical write section.
    /// </summary>
    public void EnterWriteLock()
    {
        SpinWait sw = new();

        while (true)
        {
            if (Interlocked.CompareExchange(ref writer, 1, 0) == 0)
            {
                while (reader != 0) Thread.SpinWait(1);
                break;
            }

            sw.SpinOnce();
        }
    }

    /// <summary>
    /// Exist the read section.
    /// </summary>
    public void ExitReadLock()
    {
        Interlocked.Decrement(ref reader);
    }

    /// <summary>
    /// Exits the write section.
    /// </summary>
    public void ExitWriteLock()
    {
        writer = 0;
    }
}