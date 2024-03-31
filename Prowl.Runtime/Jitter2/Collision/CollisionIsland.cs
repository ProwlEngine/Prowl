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

using System.Collections.Generic;
using Jitter2.DataStructures;
using Jitter2.Dynamics;

namespace Jitter2.Collision;

/// <summary>
/// Represents an island, which is a collection of bodies that are either directly or indirectly in contact with each other.
/// </summary>
public sealed class Island : IListIndex
{
    internal readonly HashSet<RigidBody> bodies = new();
    internal bool MarkedAsActive;
    internal bool NeedsUpdate;

    /// <summary>
    /// Gets a collection of all the bodies present in this island.
    /// </summary>
    public ReadOnlyHashSet<RigidBody> Bodies { get; private set; }

    int IListIndex.ListIndex { get; set; } = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Island"/> class.
    /// </summary>
    public Island()
    {
        Bodies = new ReadOnlyHashSet<RigidBody>(bodies);
    }

    /// <summary>
    /// Clears all the bodies from the lists within this island.
    /// </summary>
    internal void ClearLists()
    {
        bodies.Clear();
    }
}