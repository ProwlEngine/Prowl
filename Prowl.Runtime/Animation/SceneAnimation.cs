// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>Every animator in a scene, advanced in one pass between the Update and LateUpdate phases.</summary>
public sealed class SceneAnimation
{
    private readonly List<Animator> _animators = new();
    private readonly List<Animator> _scratch = new();

    /// <summary>The enabled animators the scene is driving.</summary>
    public IReadOnlyList<Animator> Animators => _animators;

    internal void Register(Animator animator)
    {
        if (!_animators.Contains(animator)) _animators.Add(animator);
    }

    internal void Unregister(Animator animator) => _animators.Remove(animator);

    internal void Clear() => _animators.Clear();

    /// <summary>Advances every animator. One that throws is reported and skipped, not fatal to the frame.</summary>
    public void Update(float deltaTime)
    {
        if (!Application.ShouldRunGameplay || _animators.Count == 0) return;

        // A tick can enable or destroy another animator, so the pass runs over a copy.
        _scratch.Clear();
        _scratch.AddRange(_animators);

        for (int i = 0; i < _scratch.Count; i++)
        {
            Animator animator = _scratch[i];
            if (animator.IsNotValid() || !animator.EnabledInHierarchy) continue;

            try { animator.Tick(deltaTime); }
            catch (Exception ex)
            {
                Debug.LogError($"[Animator] '{animator.GameObject.Name}' threw while animating: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
