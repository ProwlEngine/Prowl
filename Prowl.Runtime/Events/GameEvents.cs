// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Events;

[EventDomain]
public partial class GameEvents
{
    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnBeforeBeginUpdate = new();
    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnAfterBeginUpdate = new();

    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnBeforeFixedUpdate = new();
    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnAfterFixedUpdate = new();

    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnBeforeUpdate = new();
    private static readonly EventKey _OnUpdate = new();
    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnAfterUpdate = new();

    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnBeforeEndUpdate = new();
    [EventArgs(typeof(GameEventsArgs))]
    private static readonly EventKey _OnAfterEndUpdate = new();

    public readonly record struct GameEventsArgs(Scene? Scene);
}
