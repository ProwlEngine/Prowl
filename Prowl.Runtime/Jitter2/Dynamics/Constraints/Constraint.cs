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
using System.Runtime.InteropServices;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Dynamics.Constraints;

[StructLayout(LayoutKind.Sequential, Size = ConstraintSize)]
public unsafe struct SmallConstraintData
{
    public const int ConstraintSize = 128;

    internal int _internal;
    public delegate*<ref SmallConstraintData, float, void> Iterate;
    public delegate*<ref SmallConstraintData, float, void> PrepareForIteration;

    public JHandle<RigidBodyData> Body1;
    public JHandle<RigidBodyData> Body2;
}

[StructLayout(LayoutKind.Sequential, Size = ConstraintSize)]
public unsafe struct ConstraintData
{
    public const int ConstraintSize = 256;

    internal int _internal;
    public delegate*<ref ConstraintData, float, void> Iterate;
    public delegate*<ref ConstraintData, float, void> PrepareForIteration;

    public JHandle<RigidBodyData> Body1;
    public JHandle<RigidBodyData> Body2;
}

/// <summary>
/// The base class for constraints.
/// </summary>
public abstract class Constraint : IDebugDrawable
{
    public RigidBody Body1 { private set; get; } = null!;
    public RigidBody Body2 { private set; get; } = null!;

    public virtual bool IsSmallConstraint { get; } = false;

    /// <summary>
    /// A handle for accessing the raw constraint data.
    /// </summary>
    public JHandle<ConstraintData> Handle { internal set; get; }

    public JHandle<SmallConstraintData> SmallHandle => JHandle<ConstraintData>.AsHandle<SmallConstraintData>(Handle);

    /// <summary>
    /// This method must be overridden. It initializes the function pointers for
    /// <see cref="ConstraintData.Iterate"/> and <see cref="ConstraintData.PrepareForIteration"/>.
    /// </summary>
    protected virtual void Create()
    {
    }

    internal Constraint()
    {
    }

    protected unsafe delegate*<ref ConstraintData, float, void> iterate = null;
    protected unsafe delegate*<ref ConstraintData, float, void> prepareForIteration = null;

    /// <summary>
    /// Enables or disables this constraint temporarily. For a complete removal of the constraint,
    /// use <see cref="World.Remove(Constraint)"/>.
    /// </summary>
    public unsafe bool IsEnabled
    {
        get => Handle.Data.Iterate != null;
        set
        {
            Handle.Data.Iterate = value ? iterate : null;
            Handle.Data.PrepareForIteration = value ? prepareForIteration : null;
        }
    }


    internal void Create(JHandle<SmallConstraintData> handle, RigidBody body1, RigidBody body2)
    {
        var cd = JHandle<SmallConstraintData>.AsHandle<ConstraintData>(handle);
        Create(cd, body1, body2);
    }

    internal void Create(JHandle<ConstraintData> handle, RigidBody body1, RigidBody body2)
    {
        Body1 = body1;
        Body2 = body2;
        Handle = handle;

        handle.Data.Body1 = body1.handle;
        handle.Data.Body2 = body2.handle;

        Create();

        IsEnabled = true;
    }

    public virtual void DebugDraw(IDebugDrawer drawer)
    {
        throw new NotImplementedException();
    }
}