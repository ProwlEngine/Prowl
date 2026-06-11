// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Silk.NET.Windowing;

namespace Prowl.Runtime.Events;

[EventDomain]
public partial class SceneEvents
{
    private static readonly EventKey _FixedUpdate = new();
    private static readonly EventKey _Update = new();
    private static readonly EventKey _LateUpdate = new();
    private static readonly EventKey _PreUpdate = new();
    [EventArgs(typeof(OnRenderCollectArgs))]
    private static readonly EventKey _OnRenderCollect = new();
    private static readonly EventKey _DrawGizmos = new();
    [EventArgs(typeof(Paper))]
    private static readonly EventKey _OnGui = new();

    private static readonly EventKey _OnBeforeUpdates = new();
    private static readonly EventKey _OnFlush = new();

    public readonly record struct OnRenderCollectArgs(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights);
}
