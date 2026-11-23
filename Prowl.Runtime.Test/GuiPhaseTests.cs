// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for the GUI phase of a frame, <see cref="Game.RunGui"/>.</summary>
public class GuiPhaseTests : RuntimeTestBase
{
    public enum Phase { Begin, Gui, End }

    /// <summary>A host whose GUI runs in a world of its own and throws in one phase, as an editor panel can.</summary>
    private sealed class ThrowingGuiGame(Scene guiWorld, Phase throwIn) : Game
    {
        protected override Scene? GuiWorld => guiWorld;
        protected override bool PresentsWorlds => false;
        public override void BeginGui(Paper paper) => ThrowIf(Phase.Begin);
        public override void OnGui(Paper paper) => ThrowIf(Phase.Gui);
        public override void EndGui(Paper paper) => ThrowIf(Phase.End);

        private void ThrowIf(Phase phase)
        {
            if (phase == throwIn) throw new InvalidOperationException("A panel failed");
        }
    }

    // The editor logs the exception and draws the next frame, so a world left ambient here outlives the frame.
    [Theory]
    [InlineData(Phase.Begin)]
    [InlineData(Phase.Gui)]
    [InlineData(Phase.End)]
    public void RunGui_ThatThrows_PutsTheAmbientWorldBack(Phase throwIn)
    {
        Scene edited = CreateScene(enable: true);

        Assert.Throws<InvalidOperationException>(() => new ThrowingGuiGame(edited, throwIn).RunGui(null!));
        edited.Dispose();

        Assert.Same(Scene.Main, Scene.Active);
        Time.TimeScale = 0.5f; // as an input action callback that pauses the game does
    }

    [Fact]
    public void RunGui_ThatThrows_PutsBackAWorldThatWasAlreadyAmbient()
    {
        Scene prior = CreateScene(enable: true);
        Scene edited = CreateScene(enable: true);

        using (prior.Enter())
        {
            Assert.Throws<InvalidOperationException>(() => new ThrowingGuiGame(edited, Phase.Gui).RunGui(null!));
            Assert.Same(prior, Scene.Active);
        }
    }
}
