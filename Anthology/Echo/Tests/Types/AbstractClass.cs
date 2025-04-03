// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Tests.Types;

public abstract class AbstractClass
{
    public string Name = "Abstract";

    public Vector3 Position { get { return _position; } set { _position = value; } }

    [SerializeField]
    private Vector3 _position = new Vector3() { X = 1, Y = 2, Z = 3 };

}
