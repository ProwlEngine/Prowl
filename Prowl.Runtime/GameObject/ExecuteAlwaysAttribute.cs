// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// When applied to a MonoBehaviour, its gameplay methods also run outside play mode, such as in the editor: Start,
/// Update, LateUpdate, FixedUpdate, OnEnable, OnDisable, and the collision, trigger and character callbacks.
/// OnAddedToScene, OnRemovedFromScene, OnRenderCollect, DrawGizmos, DrawGizmosSelected and OnGui always run.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public class ExecuteAlwaysAttribute : Attribute { }
