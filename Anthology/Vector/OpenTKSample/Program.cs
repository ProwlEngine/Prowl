// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace OpenTKSample;

class Program
{
    static void Main(string[] args)
    {
        var gameWindowSettings = GameWindowSettings.Default;
        var nativeWindowSettings = new NativeWindowSettings()
        {
            Size = new OpenTK.Mathematics.Vector2i(800, 600),
            Title = "OpenTK Demo with Prowl.Vector",
            Flags = ContextFlags.ForwardCompatible,
        };

        using var window = new GameWindow(gameWindowSettings, nativeWindowSettings);
        window.Run();
    }
}
