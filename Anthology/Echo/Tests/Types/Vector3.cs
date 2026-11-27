// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace Tests.Types;

[Serializable]
public struct Vector3 : IEquatable<Vector3>
{
    public float X;

    public float Y;

    public float Z;

    [NonSerialized]
    private ConcurrentBag<Vector3> TestNonSerialized = new();

    public Vector3()
    {
    }

    public bool Equals(Vector3 other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }
}
