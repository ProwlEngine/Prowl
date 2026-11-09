// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A current over a rectangle centred on the GameObject, running along its forward, which carries the
/// <see cref="Ocean"/>'s waves, foam and ripples downstream.
/// </summary>
[AddComponentMenu("Rendering/Ocean Flow")]
public sealed class OceanFlow : Component
{
    /// <summary>The rectangle's size along x and z in metres.</summary>
    public Float2 Size = new(40f, 40f);

    /// <summary>The current's speed in m/s.</summary>
    public float Speed = 1f;

    /// <summary>How far in from the rectangle's edges the current fades in, in metres.</summary>
    public float Feather = 10f;

    [System.NonSerialized, NotHeld] private G.OceanFlowInput? _input;
    [System.NonSerialized, NotHeld] private Ocean? _ocean;

    public override void OnDisable()
    {
        if (_ocean.IsValid() && _input != null)
            _ocean!.RemoveFlow(_input);
        (_input, _ocean) = (null, null);
    }

    public override void Update()
    {
        Ocean? ocean = Ocean.Active;
        if (ocean.IsNotValid())
            return;
        if (!ReferenceEquals(_ocean, ocean) || _input == null)
        {
            if (_ocean.IsValid() && _input != null)
                _ocean!.RemoveFlow(_input);
            _ocean = ocean;
            _input = new G.OceanFlowInput();
            ocean!.AddFlow(_input);
        }
        Float3 centre = Transform.Position;
        Float3 forward = Transform.Forward;
        Float2 along = new(forward.X, forward.Z);
        float length = Float2.Length(along);
        _input.Min = new Double2(centre.X - Size.X * 0.5, centre.Z - Size.Y * 0.5);
        _input.Size = Size;
        _input.Feather = Feather;
        _input.Velocity = length > 1e-4f ? along / length * Speed : Float2.Zero;
    }
}
