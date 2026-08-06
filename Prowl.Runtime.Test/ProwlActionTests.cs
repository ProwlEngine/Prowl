// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="ProwlAction"/> / <see cref="ProwlCall"/>: what a configured call invokes, and
/// what it reports when the target is gone or the target itself throws. The reporting matters as much
/// as the dispatch here - these calls are wired up in the inspector, so the log is all an author has.
/// </summary>
public class ProwlActionTests : RuntimeTestBase
{
    private sealed class CallTarget : MonoBehaviour
    {
        public int Calls;
        public int LastInt;

        public void Ping() => Calls++;
        public void PingInt(int value) { Calls++; LastInt = value; }
        public void Boom() => throw new InvalidOperationException("the target's own failure");
    }

    private CallTarget MakeTarget()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Target");
        var comp = go.AddComponent<CallTarget>();
        scene.Add(go);
        return comp;
    }

    private static ProwlAction ActionFor(EngineObject? target, string member,
                                         ProwlActionArgType argType = ProwlActionArgType.None, int intArg = 0)
    {
        var action = new ProwlAction();
        action.Calls.Add(new ProwlCall { Target = target, Member = member, ArgType = argType, IntArg = intArg });
        return action;
    }

    /// <summary>Records everything logged while it is alive.</summary>
    private sealed class LogCapture : IDisposable
    {
        private readonly List<string> _messages = [];

        public LogCapture() => Debug.OnLog += Record;

        private void Record(string message, DebugStackTrace? trace, LogSeverity severity) => _messages.Add(message);

        public bool Logged(string text) => _messages.Exists(m => m.Contains(text, StringComparison.Ordinal));

        public void Dispose() => Debug.OnLog -= Record;
    }

    [Fact]
    public void Invoke_CallsTheTargetMethod()
    {
        var target = MakeTarget();

        ActionFor(target, nameof(CallTarget.Ping)).Invoke();

        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void Invoke_PassesTheConfiguredArgument()
    {
        var target = MakeTarget();

        ActionFor(target, nameof(CallTarget.PingInt), ProwlActionArgType.Int, intArg: 42).Invoke();

        Assert.Equal(1, target.Calls);
        Assert.Equal(42, target.LastInt);
    }

    // EngineObject's == is reference equality, so a destroyed target is not null and reflection would
    // happily call into it. Anything the method touches on the way (GameObject, Scene) is already gone.
    [Fact]
    public void Invoke_OnADestroyedTarget_DoesNotCallIt()
    {
        var target = MakeTarget();
        var action = ActionFor(target, nameof(CallTarget.Ping));

        target.GameObject.Dispose();

        using var log = new LogCapture();
        action.Invoke();

        Assert.Equal(0, target.Calls);
        Assert.True(log.Logged("null or destroyed"), "A call that silently did nothing is indistinguishable from a mis-wired one.");
    }

    [Fact]
    public void Invoke_OnANullTarget_IsANoOp()
    {
        var action = ActionFor(null, nameof(CallTarget.Ping));

        action.Invoke(); // must not throw
    }

    // Reflection wraps whatever the target threw in a TargetInvocationException whose message and
    // stack are about reflection ("Exception has been thrown by the target of an invocation"), which
    // says nothing about the actual fault. The report has to name what really went wrong.
    [Fact]
    public void Invoke_WhenTheTargetThrows_ReportsTheTargetsOwnException()
    {
        var target = MakeTarget();
        using var log = new LogCapture();

        ActionFor(target, nameof(CallTarget.Boom)).Invoke();

        Assert.True(log.Logged("the target's own failure"), "The target's message must reach the log.");
        Assert.True(log.Logged(nameof(InvalidOperationException)), "So must its type.");
        Assert.True(log.Logged("Target.Boom"), "And which call it was.");
        Assert.False(log.Logged("target of an invocation"), "The reflection wrapper is noise.");
    }

    // The DontDestroyOnLoad trap. A saved reference to an object outside the scene's own graph is
    // written by value rather than as a link, so loading rebuilds it as a copy with no GameObject.
    // The call then fails somewhere inside the author's own method, with nothing pointing at the wiring.
    [Fact]
    public void Invoke_OnADetachedTarget_SaysTheTargetIsDetached()
    {
        var target = MakeTarget();
        var detached = (CallTarget)Prowl.Echo.Serializer.Deserialize<MonoBehaviour>(
            Prowl.Echo.Serializer.Serialize(typeof(MonoBehaviour), target))!;

        Assert.True(detached.GameObject.IsNotValid(), "Precondition: an out-of-graph reference loads back detached.");

        using var log = new LogCapture();
        ActionFor(detached, nameof(CallTarget.Boom)).Invoke();

        Assert.True(log.Logged("detached"), "The wiring, not just the throw site, is what the author has to fix.");
    }

    [Fact]
    public void Invoke_WhenACallThrows_TheRestStillRun()
    {
        var target = MakeTarget();
        var action = ActionFor(target, nameof(CallTarget.Boom));
        action.Calls.Add(new ProwlCall { Target = target, Member = nameof(CallTarget.Ping) });

        using var log = new LogCapture();
        action.Invoke();

        Assert.Equal(1, target.Calls);
    }
}
