// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Cuts the <see cref="Ocean"/>'s surface away inside a shape at the GameObject, so a hull or a dry dock stays dry.
/// Only the surface is cut, not the underwater fog.
/// </summary>
[AddComponentMenu("Rendering/Ocean Clip")]
public sealed class OceanClip : Component
{
    public G.OceanClipShape Shape = G.OceanClipShape.Box;

    /// <summary>The shape's full size along the GameObject's axes in metres.</summary>
    public Float3 Size = new(2f, 2f, 2f);

    [System.NonSerialized, NotHeld] private G.OceanClipInput? _input;
    [System.NonSerialized, NotHeld] private Ocean? _ocean;

    public override void OnDisable()
    {
        if (_ocean.IsValid() && _input != null)
            _ocean!.RemoveClip(_input);
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
                _ocean!.RemoveClip(_input);
            _ocean = ocean;
            _input = new G.OceanClipInput();
            ocean!.AddClip(_input);
        }
        Float3 position = Transform.Position;
        _input.Shape = Shape;
        _input.Position = new Double3(position.X, position.Y, position.Z);
        _input.Rotation = Transform.Rotation;
        _input.Size = Size;
    }
}
