// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for the GUI phase of a frame, <see cref="Game.RunGui"/>.</summary>
public class GuiPhaseTests : RuntimeTestBase
{
    /// <summary>A host whose GUI runs in a world of its own and throws, as an editor panel can.</summary>
    private sealed class ThrowingGuiGame(SceneInstance guiWorld) : Game
    {
        protected override SceneInstance? GuiWorld => guiWorld;
        protected override bool PresentsWorlds => false;
        public override void OnGui(Paper paper) => throw new InvalidOperationException("A panel failed");
    }

    // The editor logs the exception and draws the next frame, so a world left ambient here outlives the frame.
    [Fact]
    public void RunGui_ThatThrows_PutsTheAmbientWorldBack()
    {
        SceneInstance edited = CreateScene(enable: true);

        Assert.Throws<InvalidOperationException>(() => new ThrowingGuiGame(edited).RunGui(null!));
        edited.Dispose();

        Assert.Same(SceneInstance.Main, SceneInstance.Active);
        Time.TimeScale = 0.5f; // as an input action callback that pauses the game does
    }
}
