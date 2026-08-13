// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Covers the cursor lock state machine. The clamp itself needs a live window, so these only exercise
/// the mode transitions and the bounds it would read. Input's cursor state is process-global, so every
/// test restores it.
/// </summary>
public class CursorLockTests
{
    /// <summary>Reports fixed bounds so the default context never has to reach for a real window.</summary>
    private sealed class FakeContext : CursorLockContext
    {
        public IntRect Bounds = new(Int2.Zero, new Int2(100, 50));

        public override IntRect GetConfineBounds() => Bounds;
    }

    [Fact]
    public void LockCursor_Locks_And_Hides()
    {
        try
        {
            Input.LockCursor();

            Assert.Equal(CursorLockMode.Locked, Input.CursorLockState);
            Assert.True(Input.CursorLocked);
            Assert.False(Input.CursorVisible);
        }
        finally { Input.UnlockCursor(); }

        Assert.Equal(CursorLockMode.None, Input.CursorLockState);
        Assert.False(Input.CursorLocked);
        Assert.True(Input.CursorVisible);
    }

    [Fact]
    public void Confined_Is_Not_Locked_So_Real_Positions_Are_Reported()
    {
        try
        {
            Input.CursorLockState = CursorLockMode.Confined;
            Input.CursorVisible = false;

            Assert.Equal(CursorLockMode.Confined, Input.CursorLockState);
            // CursorLocked is what swaps MousePosition for the lock center, defeating a custom cursor
            Assert.False(Input.CursorLocked);
            Assert.False(Input.CursorVisible);
        }
        finally { Input.UnlockCursor(); }
    }

    [Fact]
    public void Confining_Announces_Itself_So_The_Escape_Prompt_Shows()
    {
        int locked = 0;
        void OnLocked() => locked++;

        Input.OnCursorLocked += OnLocked;
        try
        {
            Input.CursorLockState = CursorLockMode.Confined;
            Assert.Equal(1, locked);

            Input.CursorLockState = CursorLockMode.None;
            Assert.Equal(1, locked);
        }
        finally
        {
            Input.OnCursorLocked -= OnLocked;
            Input.UnlockCursor();
        }
    }

    [Fact]
    public void Context_That_Disallows_Locking_Rejects_Every_Constraining_Mode()
    {
        int failures = 0;
        void OnFailed() => failures++;

        Input.OnCursorLockFailed += OnFailed;
        Input.PushLockContext(new FakeContext { AllowLock = false });
        try
        {
            Input.CursorLockState = CursorLockMode.Confined;
            Assert.Equal(CursorLockMode.None, Input.CursorLockState);

            Input.LockCursor();
            Assert.Equal(CursorLockMode.None, Input.CursorLockState);
            Assert.True(Input.CursorVisible);

            Assert.Equal(2, failures);
        }
        finally
        {
            Input.OnCursorLockFailed -= OnFailed;
            Input.PopLockContext();
            Input.UnlockCursor();
        }
    }

    [Fact]
    public void PopLockContext_Releases_A_Confined_Cursor()
    {
        Input.PushLockContext(new FakeContext());
        try
        {
            Input.CursorLockState = CursorLockMode.Confined;
            Input.CursorVisible = false;
            Assert.Equal(CursorLockMode.Confined, Input.CursorLockState);

            Input.PopLockContext();

            Assert.Equal(CursorLockMode.None, Input.CursorLockState);
            Assert.True(Input.CursorVisible); // UnlockCursor deliberately overrides visibility
        }
        finally { Input.UnlockCursor(); }
    }

    [Fact]
    public void Setting_LockState_Directly_Leaves_Visibility_Alone()
    {
        try
        {
            Input.CursorVisible = false;

            Input.CursorLockState = CursorLockMode.Confined;
            Assert.False(Input.CursorVisible);

            Input.CursorLockState = CursorLockMode.None;
            Assert.False(Input.CursorVisible);
        }
        finally
        {
            Input.UnlockCursor();
            Input.CursorVisible = true;
        }
    }

    [Fact]
    public void ConfineBounds_And_Center_Come_From_The_Topmost_Context()
    {
        var outer = new FakeContext { Bounds = new IntRect(Int2.Zero, new Int2(100, 50)) };
        var inner = new FakeContext { Bounds = new IntRect(new Int2(10, 20), new Int2(60, 40)) };

        Input.PushLockContext(outer);
        try
        {
            Assert.Equal(outer.Bounds, Input.CursorConfineBounds);

            Input.PushLockContext(inner);
            try
            {
                Assert.Equal(inner.Bounds, Input.CursorConfineBounds);
                Assert.Equal(inner.Bounds.Center, Input.CursorLockCenter);
            }
            finally { Input.PopLockContext(); }

            Assert.Equal(outer.Bounds, Input.CursorConfineBounds);
        }
        finally { Input.PopLockContext(); }
    }
}
