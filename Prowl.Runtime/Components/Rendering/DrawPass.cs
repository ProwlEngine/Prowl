// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

/// <summary>When a <see cref="DrawPass"/> draws in each camera's frame.</summary>
public enum DrawPassStage
{
    /// <summary>After the depth prepass, before the opaque surfaces draw over it.</summary>
    BeforeOpaques,

    /// <summary>After the opaque surfaces and the sky.</summary>
    AfterOpaques,

    /// <summary>After the transparent surfaces.</summary>
    AfterTransparents,
}

/// <summary>
/// Draws every visible renderer's shader pass tagged "LightMode" = <see cref="PassType"/> in every camera of the scene,
/// such as "Outline". Each pass draws with the render state its shader gives it, so outlines, silhouettes seen through
/// walls and similar effects need only the pass in their shaders.
/// </summary>
[AddComponentMenu("Rendering/Draw Pass")]
[ComponentIcon("")] // LayerGroup
public sealed class DrawPass : Component, IRenderSync
{
    /// <summary>The "LightMode" tag of the shader passes to draw.</summary>
    public string PassType = "Outline";

    public DrawPassStage Stage = DrawPassStage.AfterOpaques;

    /// <summary>Draw passes in the same stage draw lowest first.</summary>
    public int Order;

    [System.NonSerialized, NotHeld] private DrawPassFeature? _feature;
    [System.NonSerialized, NotHeld] private RenderWorld? _world;

    bool IRenderSync.Sync(RenderWorld world)
    {
        RenderSlot slot = Stage switch
        {
            DrawPassStage.BeforeOpaques => RenderSlot.AfterPrepass,
            DrawPassStage.AfterTransparents => RenderSlot.AfterTransparents,
            _ => RenderSlot.AfterOpaques,
        };
        if (_feature != null && (_world != world || _feature.Type.Name != PassType || _feature.Slot != slot || _feature.Order != Order))
            Release();
        if (_feature == null && !string.IsNullOrWhiteSpace(PassType))
        {
            _feature = new DrawPassFeature(new PassType(PassType), slot, Order);
            world.Features.Add(_feature);
            _world = world;
        }
        return false;
    }

    void IRenderSync.Detach(RenderWorld world) => Release();

    private void Release()
    {
        if (_feature == null) return;
        _world?.Features.Remove(_feature);
        _feature = null;
        _world = null;
    }
}
