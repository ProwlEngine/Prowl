// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample;

public class PlaneDemo : IDemo
{
    public string Name => "Plane";

    public void Draw(Float3 position, float timeInSeconds)
    {
        Float3 planeNormal = Float3.Normalize(new Float3(0, 1, 0));
        Gizmo.DrawPlane(position, planeNormal, new Float3(2, 0, 2), new Float4(0, 1, 1, 1), 8);
    }

    public Float3 GetBounds() => new Float3(4.0f, 1.0f, 4.0f);
}


