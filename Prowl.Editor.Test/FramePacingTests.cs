// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Theming;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The editor and a game start from opposite defaults on purpose: the editor is a tool sharing a
/// machine with everything else, and a game is the thing the machine was turned on for. Only the
/// editor's half is reachable here, since a game's comes from creating a window.
/// </summary>
public class FramePacingTests
{
    [Fact]
    public void TheEditorStartsWithVSyncOnAndNoFrameLimit()
    {
        var settings = new EditorSettings();

        Assert.True(settings.VSync);
        Assert.Equal(0, settings.TargetFrameRate);
    }
}
