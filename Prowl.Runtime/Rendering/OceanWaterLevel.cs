// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Holds the <see cref="Ocean"/>'s water at the GameObject's height over a rectangle centred on it, a lake or pond above
/// or below the sea.
/// </summary>
[AddComponentMenu("Rendering/Ocean Water Level")]
public sealed class OceanWaterLevel : Component
{
    /// <summary>The rectangle's size along x and z in metres.</summary>
    public Float2 Size = new(20f, 20f);

    [System.NonSerialized, NotHeld] private G.OceanLevelInput? _input;
    [System.NonSerialized, NotHeld] private Ocean? _ocean;

    public override void OnDisable()
    {
        if (_ocean.IsValid() && _input != null)
            _ocean!.RemoveLevel(_input);
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
                _ocean!.RemoveLevel(_input);
            _ocean = ocean;
            _input = new G.OceanLevelInput();
            ocean!.AddLevel(_input);
        }
        Float3 centre = Transform.Position;
        _input.Min = new Double2(centre.X - Size.X * 0.5, centre.Z - Size.Y * 0.5);
        _input.Size = Size;
        _input.Offset = centre.Y;
    }
}
