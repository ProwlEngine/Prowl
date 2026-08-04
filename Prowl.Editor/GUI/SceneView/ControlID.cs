// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Stable identity for one interactive scene-view element across frames. Obtained from
/// <see cref="HandleContext.GetControlID(string)"/> so identity is scoped to a viewport.
/// </summary>
public readonly struct ControlID : IEquatable<ControlID>
{
    public readonly int Value;

    internal ControlID(int value) => Value = value;

    public static readonly ControlID None = new(0);

    public bool IsValid => Value != 0;

    public bool Equals(ControlID other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ControlID o && Equals(o);
    public override int GetHashCode() => Value;

    public static bool operator ==(ControlID a, ControlID b) => a.Value == b.Value;
    public static bool operator !=(ControlID a, ControlID b) => a.Value != b.Value;
}
