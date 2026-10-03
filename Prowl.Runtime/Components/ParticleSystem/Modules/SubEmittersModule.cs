// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum SubEmitterType
{
    /// <summary>Emits continuously from every live particle, at the child's emission rates.</summary>
    Birth,
    /// <summary>Emits the child's bursts where a particle collides.</summary>
    Collision,
    /// <summary>Emits the child's bursts where a particle dies.</summary>
    Death,
    /// <summary>Emits the child's bursts only when <see cref="ParticleSystemComponent.TriggerSubEmitter(int)"/> is called.</summary>
    Manual
}

/// <summary>One child system and when it fires.</summary>
[Serializable]
public class SubEmitter
{
    [Tooltip("The system that emits. Its own emission is switched off while it is driven from here.")]
    public ParticleSystemComponent? System;
    public SubEmitterType Type = SubEmitterType.Death;
    [Range(0f, 1f)]
    public float Probability = 1f;

    [Header("Inherit")]
    public bool InheritColor = false;
    public bool InheritSize = false;
    public bool InheritRotation = false;
    [Tooltip("Scales child lifetimes by the share of life the parent particle has left.")]
    public bool InheritLifetime = false;
}

/// <summary>
/// Other particle systems that emit from this one's particles when they are born, collide or die.
/// Burst type sub emitters emit the total of the child's bursts, Birth emits at the child's rates.
/// </summary>
[Serializable]
public class SubEmittersModule : ParticleSystemModule
{
    public List<SubEmitter> Emitters = new();
}
