// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Pushes ripples and a wake into the <see cref="Ocean"/> as the GameObject moves through the water, as a sphere.
/// Several make a hull's wake.
/// </summary>
[AddComponentMenu("Rendering/Ocean Wake")]
public sealed class OceanWake : Component
{
    /// <summary>The sphere's radius in metres.</summary>
    public float Radius = 1f;

    /// <summary>Scales how hard it pushes the water.</summary>
    public float Weight = 1f;

    [System.NonSerialized, NotHeld] private G.OceanInteractor? _interactor;
    [System.NonSerialized, NotHeld] private Ocean? _ocean;
    [System.NonSerialized, NotHeld] private Float3 _last;
    [System.NonSerialized, NotHeld] private bool _moved;

    public override void OnDisable()
    {
        if (_ocean.IsValid() && _interactor != null)
            _ocean!.RemoveInteractor(_interactor);
        (_interactor, _ocean, _moved) = (null, null, false);
    }

    public override void Update()
    {
        Ocean? ocean = Ocean.Active;
        if (ocean.IsNotValid())
            return;
        if (!ReferenceEquals(_ocean, ocean) || _interactor == null)
        {
            if (_ocean.IsValid() && _interactor != null)
                _ocean!.RemoveInteractor(_interactor);
            _ocean = ocean;
            _interactor = new G.OceanInteractor();
            ocean!.AddInteractor(_interactor);
        }

        Float3 position = Transform.Position;
        float dt = Time.DeltaTime;
        Float3 velocity = _moved && dt > 1e-5f ? (position - _last) / dt : Float3.Zero;
        (_last, _moved) = (position, true);
        _interactor.Position = new Double3(position.X, position.Y, position.Z);
        _interactor.Velocity = velocity;
        _interactor.Radius = Radius;
        _interactor.Weight = Weight;
    }
}
