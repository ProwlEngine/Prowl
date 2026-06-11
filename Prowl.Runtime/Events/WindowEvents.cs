// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;

using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Runtime.Events;

[EventDomain]
public static partial class WindowEvents
{
    private static readonly EventKey _Load = new();
    [EventArgs(typeof(FloatArgs))]
    private static readonly EventKey _Update = new();
    [EventArgs(typeof(float))]
    private static readonly EventKey _Render = new();
    [EventArgs(typeof(FloatArgs))]
    private static readonly EventKey _PostRender = new();
    [EventArgs(typeof(BoolArgs))]
    private static readonly EventKey _FocusChanged = new();
    [EventArgs(typeof(Vector2D<int>))]
    private static readonly EventKey _Resize = new();
    [EventArgs(typeof(Vector2IntArgs))]
    private static readonly EventKey _FramebufferResize = new();
    private static readonly EventKey _Closing = new();

    [EventArgs(typeof(Vector2IntArgs))]
    private static readonly EventKey _Move = new();
    [EventArgs(typeof(WindowStateArgs))]
    private static readonly EventKey _StateChanged = new();
    [EventArgs(typeof(FileDropArgs))]
    private static readonly EventKey _FileDrop = new();


    public readonly record struct FloatArgs(float Value);
    public readonly record struct BoolArgs(bool Value);
    public readonly record struct Vector2IntArgs(Vector2D<int> Value);
    public readonly record struct WindowStateArgs(WindowState Value);
    public readonly record struct FileDropArgs(string[] Paths);
}
