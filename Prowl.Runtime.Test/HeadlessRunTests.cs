// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the headless run loop (<see cref="Game.RunHeadless"/>) - the standalone-server path that
/// drives gameplay/physics with no window, graphics or audio device.
/// </summary>
public class HeadlessRunTests
{
    /// <summary>A Game that loads a scene on Initialize and counts its update steps.</summary>
    private sealed class CountingHeadlessGame : Game
    {
        private readonly Scene _scene;
        public int UpdateCount;

        public CountingHeadlessGame(Scene scene) => _scene = scene;

        public override void Initialize() => Scene.Load(_scene);
        public override void OnUpdate(Scene? scene)
        {
            base.OnUpdate(scene);
            UpdateCount++;
        }
    }

    [Fact]
    public void RunHeadless_RunsRequestedFrames_ThenExits()
    {
        var time = new TimeData();
        Time.TimeStack.Push(time);
        try
        {
            var scene = new Scene();
            var game = new CountingHeadlessGame(scene);

            game.RunHeadless(new HeadlessRunOptions { MaxFrames = 10, TargetFps = 0 });

            Assert.Equal(10, game.UpdateCount);
            Assert.False(Application.IsHeadless); // reset on exit
            Assert.Null(Scene.Current);           // scene unloaded on exit
        }
        finally
        {
            Time.TimeStack.Clear();
            Application.IsPlaying = false;
        }
    }

    [Fact]
    public void RunHeadless_SetsHeadlessFlagDuringRun()
    {
        bool sawHeadless = false;

        var probe = new FlagProbeGame(() => sawHeadless = Application.IsHeadless);
        probe.RunHeadless(new HeadlessRunOptions { MaxFrames = 1, TargetFps = 0 });

        Assert.True(sawHeadless);
        Application.IsPlaying = false;
    }

    private sealed class FlagProbeGame : Game
    {
        private readonly Action _onUpdate;
        public FlagProbeGame(Action onUpdate) => _onUpdate = onUpdate;
        public override void OnUpdate(Scene? scene) => _onUpdate();
    }

    [Fact]
    public void RunHeadless_RequestQuit_StopsLoop()
    {
        var stopper = new SelfStoppingGame();
        stopper.RunHeadless(new HeadlessRunOptions { MaxFrames = 0, TargetFps = 0 });

        // It quit itself after 3 frames rather than running unbounded.
        Assert.Equal(3, stopper.Frames);
        Application.IsPlaying = false;
    }

    private sealed class SelfStoppingGame : Game
    {
        public int Frames;
        public override void OnUpdate(Scene? scene)
        {
            Frames++;
            if (Frames >= 3) RequestHeadlessQuit();
        }
    }
}
