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
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

namespace Jitter2.SoftBodies;

public class SoftBody
{
    public List<RigidBody> Vertices { get; } = new();
    public List<Constraint> Springs { get; } = new();
    public List<Shape> Shapes { get; } = new();

    protected World world;

    public bool IsActive => Vertices[0].IsActive;

    public SoftBody(World world)
    {
        this.world = world;
        world.PostStep += WorldOnPostStep;
    }

    public void Destroy()
    {
        world.PostStep -= WorldOnPostStep;

        foreach (var shape in Shapes)
        {
            world.Remove(shape);
        }

        foreach (var spring in Springs)
        {
            world.Remove(spring);
        }

        foreach (var point in Vertices)
        {
            world.Remove(point);
        }
    }

    private bool active = true;

    protected virtual void WorldOnPostStep(float dt)
    {
        if (IsActive == active) return;
        active = IsActive;

        foreach (var shape in Shapes)
        {
            if (active)
            {
                world.ActivateShape(shape);
            }
            else
            {
                world.DeactivateShape(shape);
            }
        }
    }
}