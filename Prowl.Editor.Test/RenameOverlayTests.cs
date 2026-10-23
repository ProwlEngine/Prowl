// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.Popups;

using Xunit;

namespace Prowl.Editor.Test;

public class RenameOverlayTests
{
    [Fact]
    public void ARenameNobodyDraws_CancelsAfterAFewFrames()
    {
        bool cancelled = false;
        RenameOverlay.Begin("a", "Name", _ => { }, () => cancelled = true);

        RenameOverlay.EndFrame();
        RenameOverlay.EndFrame();
        Assert.False(cancelled);
        Assert.True(RenameOverlay.IsRenaming("a"));

        RenameOverlay.EndFrame();
        Assert.True(cancelled);
        Assert.False(RenameOverlay.IsActive);
    }

    [Fact]
    public void StartingAnotherRename_CancelsTheFirst()
    {
        bool firstCancelled = false;
        RenameOverlay.Begin("a", "A", _ => { }, () => firstCancelled = true);
        RenameOverlay.Begin("b", "B", _ => { });

        Assert.True(firstCancelled);
        Assert.True(RenameOverlay.IsRenaming("b"));
        RenameOverlay.Cancel();
    }

    [Fact]
    public void CancelRunsTheCallbackOnce()
    {
        int cancels = 0;
        RenameOverlay.Begin("a", "A", _ => { }, () => cancels++);
        RenameOverlay.Cancel();
        RenameOverlay.Cancel();

        Assert.Equal(1, cancels);
    }
}
