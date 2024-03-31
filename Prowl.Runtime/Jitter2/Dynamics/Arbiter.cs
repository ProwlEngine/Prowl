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
using System.Collections.Generic;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Dynamics;

/// <summary>
/// Holds a reference to all contacts (maximum 4) between two shapes.
/// </summary>
public class Arbiter
{
    internal static Stack<Arbiter> Pool = new();

    public RigidBody Body1 = null!;
    public RigidBody Body2 = null!;

    public JHandle<ContactData> Handle;
}

/// <summary>
/// Look-up key for stored <see cref="Arbiter"/>.
/// </summary>
public struct ArbiterKey : IEquatable<ArbiterKey>
{
    public ulong Key1;
    public ulong Key2;

    public ArbiterKey(ulong key1, ulong key2)
    {
        Key1 = key1;
        Key2 = key2;
    }

    public bool Equals(ArbiterKey other)
    {
        return Key1 == other.Key1 && Key2 == other.Key2;
    }

    public override bool Equals(object? obj)
    {
        return obj is ArbiterKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (int)Key1 + 2281 * (int)Key2;
    }
}