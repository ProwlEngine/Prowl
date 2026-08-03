// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Runtime;

namespace Prowl.Player;

/// <summary>
/// The whole of a built player's startup. The generated program is one line that calls this, so
/// everything here is ordinary compiled code with a debugger, tests and a compiler behind it.
/// </summary>
public static class PlayerEntryPoint
{
    /// <summary>
    /// Installs dependency resolution, then starts the game.
    /// </summary>
    /// <remarks>
    /// This method must not mention a type that pulls in a dependency living under <c>runtimes/</c>.
    /// Preparing a method loads every type in its body, and that happens before its first statement
    /// runs, so naming <see cref="DesktopPlayer"/> here would demand Paper before the resolver that
    /// finds Paper has been installed. Hence the split, and hence the NoInlining on the other half.
    /// </remarks>
    public static int Main(string[] args)
    {
        NativeLibraryResolver.Install();
        return Start(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Start(string[] args)
    {
        var manifest = PlayerManifest.Load(AppContext.BaseDirectory);
        using var game = new DesktopPlayer(manifest);

        if (IsHeadless(args))
            game.RunHeadless(ParseHeadless(args));
        else
            game.Run(manifest.ProductName, manifest.WindowWidth, manifest.WindowHeight);

        return 0;
    }

    private static bool IsHeadless(string[] args)
        => args.Contains("--headless") || args.Contains("-headless");

    private static HeadlessRunOptions ParseHeadless(string[] args)
    {
        var options = new HeadlessRunOptions();

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--frames" when long.TryParse(args[i + 1], out long frames):
                    options.MaxFrames = frames;
                    break;

                case "--seconds" when double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double seconds):
                    options.MaxSeconds = seconds;
                    break;

                case "--fps" when int.TryParse(args[i + 1], out int fps):
                    options.TargetFps = fps;
                    break;
            }
        }

        return options;
    }
}
