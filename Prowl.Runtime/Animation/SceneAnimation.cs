// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>Every animator in a scene, advanced in one pass between the Update and LateUpdate phases.</summary>
public sealed class SceneAnimation
{
    private readonly List<Animator> _animators = [];
    private readonly List<Animator> _scratch = [];

    /// <summary>The enabled animators the scene is driving.</summary>
    public IReadOnlyList<Animator> Animators => _animators;

    internal void Register(Animator animator)
    {
        if (!_animators.Contains(animator)) _animators.Add(animator);
    }

    internal void Unregister(Animator animator) => _animators.Remove(animator);

    internal void Clear() => _animators.Clear();

    /// <summary>Advances every Normal and UnscaledTime animator. One that throws is reported and skipped, not fatal to the frame.</summary>
    public void Update(float deltaTime)
    {
        if (_animators.Count == 0) return;

        // A tick can enable or destroy another animator, so the pass runs over a copy.
        _scratch.Clear();
        _scratch.AddRange(_animators);

        float unscaledDt = (float)Time.UnscaledDeltaTime;
        for (int i = 0; i < _scratch.Count; i++)
        {
            Animator animator = _scratch[i];
            if (animator.IsNotValid() || !animator.EnabledInHierarchy) continue;
            if (animator.UpdateMode == AnimatorUpdateMode.AnimatePhysics) continue;
            if (ShouldCull(animator)) continue;

            float dt = animator.UpdateMode == AnimatorUpdateMode.UnscaledTime ? unscaledDt : deltaTime;
            try { animator.Tick(dt); }
            catch (Exception ex)
            {
                Debug.LogException($"[Animator] '{animator.GameObject.Name}' threw while animating", ex);
            }
        }
    }

    /// <summary>Advances every <see cref="AnimatorUpdateMode.AnimatePhysics"/> animator in FixedUpdate before physics steps.</summary>
    public void FixedUpdate(float fixedDeltaTime)
    {
        if (_animators.Count == 0) return;

        _scratch.Clear();
        _scratch.AddRange(_animators);

        for (int i = 0; i < _scratch.Count; i++)
        {
            Animator animator = _scratch[i];
            if (animator.IsNotValid() || !animator.EnabledInHierarchy) continue;
            if (animator.UpdateMode != AnimatorUpdateMode.AnimatePhysics) continue;
            if (ShouldCull(animator)) continue;

            try { animator.Tick(fixedDeltaTime); }
            catch (Exception ex)
            {
                Debug.LogException($"[Animator] '{animator.GameObject.Name}' threw while animating in FixedUpdate", ex);
            }
        }
    }

    private static bool ShouldCull(Animator animator)
    {
        if (animator.CullingMode != AnimatorCullingMode.CullCompletely) return false;
        GameObject go = animator.GameObject;
        if (go.IsNotValid()) return true;
        // Skip only if renderers exist in the hierarchy and every one of them is disabled
        bool foundRenderer = false;
        if (HasActiveRenderer(go, ref foundRenderer)) return false;
        return foundRenderer;
    }

    private static bool HasActiveRenderer(GameObject go, ref bool foundRenderer)
    {
        for (int i = 0; i < go._components.Count; i++)
        {
            Component c = go._components[i];
            if (c.IsNotValid()) continue;
            if (c is Rendering.IRenderSync)
            {
                foundRenderer = true;
                if (c.EnabledInHierarchy) return true;
            }
        }
        for (int i = 0; i < go.Children.Count; i++)
            if (HasActiveRenderer(go.Children[i], ref foundRenderer)) return true;
        return false;
    }
}
