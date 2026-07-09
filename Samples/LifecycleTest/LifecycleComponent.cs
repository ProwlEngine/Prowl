// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace LifecycleTest;

/// <summary>
/// A test component that logs all lifecycle events.
/// Used to verify that OnEnable, OnDisable, OnDispose and other lifecycle methods are called correctly.
/// </summary>
public class LifecycleComponent : MonoBehaviour
{
    public string ComponentName { get; set; } = "LifecycleComponent";

    public override void OnAddedToScene()
    {
        Debug.Log($"[{ComponentName}] OnAddedToScene - GameObject: {GameObject.Name}, Scene Active: {Scene?.IsActive}");
    }

    public override void OnRemovedFromScene()
    {
        Debug.Log($"[{ComponentName}] OnRemovedFromScene - GameObject: {GameObject.Name}");
    }

    public override void OnEnable()
    {
        Debug.Log($"[{ComponentName}] OnEnable - GameObject: {GameObject.Name}, Enabled: {Enabled}, EnabledInHierarchy: {EnabledInHierarchy}");
    }

    public override void OnDisable()
    {
        Debug.Log($"[{ComponentName}] OnDisable - GameObject: {GameObject.Name}, Enabled: {Enabled}, EnabledInHierarchy: {EnabledInHierarchy}");
    }

    public override void Start()
    {
        Debug.Log($"[{ComponentName}] Start - GameObject: {GameObject.Name}");
    }

    public override void Update()
    {
        // Only log occasionally to avoid spam
        if (Time.FrameCount % 120 == 0)
        {
            Debug.Log($"[{ComponentName}] Update (every 120 frames) - GameObject: {GameObject.Name}");
        }
    }

    public override void OnDispose()
    {
        Debug.Log($"[{ComponentName}] OnDispose - GameObject: {GameObject.Name}");
    }
}
