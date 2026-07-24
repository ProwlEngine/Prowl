// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>An in-memory clipboard so the round-trip is testable headlessly.</summary>
public sealed class FakeClipboardInput : IInputHandler
{
    private string _clipboard = "";
    public string Clipboard { get => _clipboard; set => _clipboard = value ?? ""; }

    public bool IsAnyKeyDown => false;
    public Float2 MouseDelta => Float2.Zero;
    public Int2 MousePosition { get => Int2.Zero; set { } }
    public float MouseWheelDelta => 0f;
    public Int2 PrevMousePosition => Int2.Zero;
    public string InputString => string.Empty;

    public event Action<KeyCode, bool> OnKeyEvent { add { } remove { } }
    public event Action<MouseButton, float, float, bool, bool> OnMouseEvent { add { } remove { } }

    public char? GetPressedChar() => null;
    public bool GetKey(KeyCode key) => false;
    public bool GetKeyDown(KeyCode key) => false;
    public bool GetKeyUp(KeyCode key) => false;
    public bool GetMouseButton(int button) => false;
    public bool GetMouseButtonDown(int button) => false;
    public bool GetMouseButtonUp(int button) => false;
    public void SetCursorVisible(bool visible, int miceIndex = 0) { }
    public void SetCursorShape(PaperCursor shape, int miceIndex = 0) { }

    public int GetGamepadCount() => 0;
    public bool IsGamepadConnected(int gamepadIndex) => false;
    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) => false;
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => false;
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => false;
    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) => Float2.Zero;
    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) => 0f;
    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) { }
}

public sealed class ClipComp : MonoBehaviour
{
    public int Value;
    public string Label = "";
    public Float3 Offset;
}

public sealed class OtherClipComp : MonoBehaviour
{
    public int Value;
}

/// <summary>A component holding scene-object references, the case Echo would otherwise deep-clone.</summary>
public sealed class ClipRefComp : MonoBehaviour
{
    public GameObject? TargetGO;
    public ClipComp? TargetComp;
    public Transform? TargetTransform;
    public int Value;
}

/// <summary>
/// Tests for <see cref="ComponentClipboard"/>: value round-trips, identity preservation on
/// paste-values, undo/redo, and - the part most likely to regress - scene-object reference fields,
/// which must resolve by identifier rather than being deep-cloned into orphans.
/// </summary>
public class ComponentClipboardTests : EditorTestHarness, IDisposable
{
    private readonly FakeClipboardInput _input = new();

    public ComponentClipboardTests()
    {
        Input.PushHandler(_input);
        Undo.Clear();
    }

    void IDisposable.Dispose()
    {
        Input.PopHandler();
        base.Dispose();
    }

    private static Scene MakeScene(out GameObject a, out GameObject b)
    {
        var scene = new Scene();
        a = new GameObject("A");
        b = new GameObject("B");
        scene.Add(a);
        scene.Add(b);
        Scene.Load(scene);
        return scene;
    }

    // ---- Basic round-trip ----

    [Fact]
    public void Copy_PasteAsNew_CopiesFieldValues()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 42;
        src.Label = "hello";
        src.Offset = new Float3(1, 2, 3);

        ComponentClipboard.Copy(src);
        var pasted = ComponentClipboard.PasteAsNew(b) as ClipComp;

        Assert.NotNull(pasted);
        Assert.Equal(42, pasted!.Value);
        Assert.Equal("hello", pasted.Label);
        Assert.Equal(new Float3(1, 2, 3), pasted.Offset);
        Assert.Same(b, pasted.GameObject);
    }

    [Fact]
    public void PasteAsNew_GetsFreshIdentifier()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();

        ComponentClipboard.Copy(src);
        var pasted = ComponentClipboard.PasteAsNew(b);

        Assert.NotNull(pasted);
        Assert.NotEqual(src.Identifier, pasted!.Identifier);
    }

    [Fact]
    public void PasteAsNew_OntoSameGameObject_AddsSecondInstance()
    {
        MakeScene(out var a, out _);
        var src = a.AddComponent<ClipComp>();
        src.Value = 7;

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteAsNew(a);

        var all = a.GetComponents<ClipComp>().ToList();
        Assert.Equal(2, all.Count);
        Assert.All(all, c => Assert.Equal(7, c.Value));
    }

    [Fact]
    public void PasteAsNew_OntoSameGameObject_RealEngineComponent()
    {
        // Stacking several colliders on one object is the motivating case for same-object paste.
        MakeScene(out var a, out _);
        var src = a.AddComponent<BoxCollider>();
        src.Size = new Float3(2, 3, 4);
        src.Center = new Float3(1, 0, 0);

        ComponentClipboard.Copy(src);
        var pasted = ComponentClipboard.PasteAsNew(a) as BoxCollider;

        Assert.NotNull(pasted);
        Assert.Equal(new Float3(2, 3, 4), pasted!.Size);
        Assert.Equal(new Float3(1, 0, 0), pasted.Center);
        Assert.Equal(2, a.GetComponents<BoxCollider>().Count());
        Assert.NotEqual(src.Identifier, pasted.Identifier);
    }

    [Fact]
    public void CopiedComponent_IsUnmodifiedByCopy()
    {
        MakeScene(out var a, out _);
        var target = a.AddComponent<ClipComp>();
        var src = a.AddComponent<ClipRefComp>();
        src.TargetGO = a;
        src.TargetComp = target;
        src.TargetTransform = a.Transform;

        ComponentClipboard.Copy(src);

        // Copy temporarily nulls reference fields to keep them out of the payload; they must be back.
        Assert.Same(a, src.TargetGO);
        Assert.Same(target, src.TargetComp);
        Assert.Same(a.Transform, src.TargetTransform);
    }

    // ---- Type gating ----

    [Fact]
    public void CanPasteValues_OnlyForMatchingType()
    {
        MakeScene(out var a, out _);
        ComponentClipboard.Copy(a.AddComponent<ClipComp>());

        Assert.True(ComponentClipboard.CanPasteValues(typeof(ClipComp)));
        Assert.False(ComponentClipboard.CanPasteValues(typeof(OtherClipComp)));
        Assert.True(ComponentClipboard.CanPasteAsNew());
    }

    [Fact]
    public void PasteValues_WrongType_IsRejected()
    {
        MakeScene(out var a, out var b);
        ComponentClipboard.Copy(a.AddComponent<ClipComp>());
        var other = b.AddComponent<OtherClipComp>();
        other.Value = 99;

        Assert.False(ComponentClipboard.PasteValues(other));
        Assert.Equal(99, other.Value);
    }

    [Fact]
    public void EmptyClipboard_PasteIsNoOp()
    {
        MakeScene(out _, out var b);
        Assert.False(ComponentClipboard.CanPasteAsNew());
        Assert.Null(ComponentClipboard.PasteAsNew(b));
    }

    [Fact]
    public void ForeignClipboardText_IsIgnored()
    {
        MakeScene(out _, out var b);
        Input.Clipboard = "just some text a user copied";

        Assert.False(ComponentClipboard.CanPasteAsNew());
        Assert.Null(ComponentClipboard.PasteAsNew(b));
    }

    // ---- Paste values preserves identity ----

    [Fact]
    public void PasteValues_OverwritesDataButKeepsIdentity()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 5;
        src.Label = "src";

        var dst = b.AddComponent<ClipComp>();
        dst.Value = 100;
        dst.Label = "dst";
        var dstId = dst.Identifier;

        ComponentClipboard.Copy(src);
        Assert.True(ComponentClipboard.PasteValues(dst));

        Assert.Equal(5, dst.Value);
        Assert.Equal("src", dst.Label);
        Assert.Equal(dstId, dst.Identifier);   // identity untouched
        Assert.Same(b, dst.GameObject);        // still on its own GameObject
    }

    [Fact]
    public void PasteValues_CopiesEnabledState()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Enabled = false;

        var dst = b.AddComponent<ClipComp>();
        Assert.True(dst.Enabled);

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteValues(dst);

        Assert.False(dst.Enabled);
        Assert.False(dst.EnabledInHierarchy); // derived flag kept in sync, not left stale
    }

    [Fact]
    public void PasteValues_DoesNotChangeSiblingIndex()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 3;

        b.AddComponent<OtherClipComp>();
        var dst = b.AddComponent<ClipComp>();
        b.AddComponent<OtherClipComp>();

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteValues(dst);

        var comps = b.GetComponents<MonoBehaviour>().ToList();
        Assert.Equal(1, comps.IndexOf(dst));
        Assert.Equal(3, dst.Value);
    }

    // ---- Scene references ----

    [Fact]
    public void SceneReferences_ResolveToOriginalObjects_NotClones()
    {
        var scene = MakeScene(out var a, out var b);
        var target = a.AddComponent<ClipComp>();
        var src = a.AddComponent<ClipRefComp>();
        src.TargetGO = a;
        src.TargetComp = target;
        src.TargetTransform = a.Transform;

        int rootsBefore = scene.RootObjects.Count();

        ComponentClipboard.Copy(src);
        var pasted = ComponentClipboard.PasteAsNew(b) as ClipRefComp;

        Assert.NotNull(pasted);
        Assert.Same(a, pasted!.TargetGO);
        Assert.Same(target, pasted.TargetComp);
        Assert.Same(a.Transform, pasted.TargetTransform);

        // The real point: no orphan clone of A was dragged into the scene by the paste.
        Assert.Equal(rootsBefore, scene.RootObjects.Count());
    }

    [Fact]
    public void SceneReferences_PastedIntoDifferentScene_AreNull()
    {
        MakeScene(out var a, out _);
        var src = a.AddComponent<ClipRefComp>();
        src.TargetGO = a;
        src.TargetComp = a.AddComponent<ClipComp>();
        src.TargetTransform = a.Transform;
        src.Value = 11;

        ComponentClipboard.Copy(src);

        // Swap to an unrelated scene, as entering/leaving prefab mode does.
        var other = new Scene();
        var host = new GameObject("Host");
        other.Add(host);
        Scene.Load(other);

        var pasted = ComponentClipboard.PasteAsNew(host) as ClipRefComp;

        Assert.NotNull(pasted);
        Assert.Equal(11, pasted!.Value);     // plain data still comes through
        Assert.Null(pasted.TargetGO);        // dangling refs drop rather than resurrect dead objects
        Assert.Null(pasted.TargetComp);
        Assert.Null(pasted.TargetTransform);
    }

    [Fact]
    public void SceneReferences_SurviveSceneSerializationRoundTrip()
    {
        // Prefab editing mode serializes the scene away and deserializes it back; identifiers are
        // restored by Scene.OnBefore/OnAfterDeserialize, so an identifier captured before the
        // round-trip must still resolve after it.
        var scene = MakeScene(out var a, out _);
        var src = a.AddComponent<ClipRefComp>();
        src.TargetGO = a;

        ComponentClipboard.Copy(src);
        Guid originalId = a.Identifier;

        var echo = Echo.Serializer.Serialize(scene);
        var restored = Echo.Serializer.Deserialize<Scene>(echo)!;
        Scene.Load(restored);

        var restoredA = restored.AllObjects.First(g => g.Identifier == originalId);
        var host = new GameObject("Host");
        restored.Add(host);

        var pasted = ComponentClipboard.PasteAsNew(host) as ClipRefComp;

        Assert.NotNull(pasted);
        Assert.Same(restoredA, pasted!.TargetGO);
    }

    [Fact]
    public void DetachedTransformReference_IsDropped_NotCloned()
    {
        // A Transform with no GameObject can't be identified, so it can't be restored - but it must
        // still be kept out of the payload. Serializing it by value would paste an orphan Transform
        // whose GameObject is null, which NREs the first time anything touches it.
        var scene = MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipRefComp>();
        src.TargetTransform = new Transform(); // never attached to a GameObject

        int rootsBefore = scene.RootObjects.Count();

        ComponentClipboard.Copy(src);
        var pasted = ComponentClipboard.PasteAsNew(b) as ClipRefComp;

        Assert.NotNull(pasted);
        Assert.Null(pasted!.TargetTransform);
        Assert.Equal(rootsBefore, scene.RootObjects.Count());

        // The source keeps its reference - capture must restore what it stashed.
        Assert.NotNull(src.TargetTransform);
    }

    // ---- Malformed / unresolvable payloads ----

    [Fact]
    public void UnknownComponentType_FailsGracefully()
    {
        // A script component copied from a project that has the script, pasted into one that doesn't.
        MakeScene(out _, out var b);
        Input.Clipboard = "ProwlComponent:Some.Missing.Namespace.GhostComponent, GhostAssembly\n{ }";

        Assert.False(ComponentClipboard.CanPasteAsNew());
        Assert.Null(ComponentClipboard.PasteAsNew(b));
        Assert.Empty(b.GetComponents<MonoBehaviour>());
    }

    [Fact]
    public void MalformedPayload_FailsGracefully()
    {
        MakeScene(out var a, out var b);
        string typeName = typeof(ClipComp).AssemblyQualifiedName!;

        // Header resolves, body is garbage.
        Input.Clipboard = $"ProwlComponent:{typeName}\nnot valid echo at all {{{{";
        Assert.Null(ComponentClipboard.PasteAsNew(b));

        // Header with no body at all.
        Input.Clipboard = $"ProwlComponent:{typeName}";
        Assert.Null(ComponentClipboard.PasteAsNew(b));

        // Well-formed body missing the Data compound.
        Input.Clipboard = $"ProwlComponent:{typeName}\n{EchoObject.NewCompound().WriteToString()}";
        Assert.Null(ComponentClipboard.PasteAsNew(b));

        Assert.Empty(b.GetComponents<MonoBehaviour>());

        // A real payload still works afterwards - no sticky failure state.
        ComponentClipboard.Copy(a.AddComponent<ClipComp>());
        Assert.NotNull(ComponentClipboard.PasteAsNew(b));
    }

    [Fact]
    public void PasteValues_OnDetachedComponent_DoesNotThrow()
    {
        MakeScene(out var a, out _);
        var src = a.AddComponent<ClipComp>();
        src.Value = 12;
        ComponentClipboard.Copy(src);

        // Not attached to any GameObject - the lifecycle bracket must handle a null GameObject.
        var orphan = new ClipComp();
        Assert.True(ComponentClipboard.PasteValues(orphan));
        Assert.Equal(12, orphan.Value);
    }

    // ---- Undo ----

    [Fact]
    public void PasteAsNew_Undo_Redo()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 77;

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteAsNew(b);
        Undo.IncrementGroup();

        Assert.Single(b.GetComponents<ClipComp>());

        Undo.PerformUndo();
        Assert.Empty(b.GetComponents<ClipComp>());

        Undo.PerformRedo();
        var again = b.GetComponents<ClipComp>().ToList();
        Assert.Single(again);
        Assert.Equal(77, again[0].Value);
    }

    [Fact]
    public void PasteAsNew_Redo_AfterTargetDeleted_FailsGracefully()
    {
        // The redo lambda looks its GameObject back up by identifier rather than capturing it, so a
        // redo whose target no longer exists must no-op instead of throwing out of the undo stack.
        var scene = MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteAsNew(b);
        Undo.IncrementGroup();

        Undo.PerformUndo();

        scene.Remove(b);
        b.Dispose();

        Undo.PerformRedo(); // must not throw
        Assert.False(Undo.CanRedo);
    }

    [Fact]
    public void PasteAsNew_Undo_AfterTargetDeleted_FailsGracefully()
    {
        var scene = MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteAsNew(b);
        Undo.IncrementGroup();

        scene.Remove(b);
        b.Dispose();

        Undo.PerformUndo(); // must not throw
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void PasteValues_Undo_AfterComponentRemoved_FailsGracefully()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 1;

        var dst = b.AddComponent<ClipComp>();
        dst.Value = 999;

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteValues(dst);
        Undo.IncrementGroup();

        b.RemoveComponent(dst);

        Undo.PerformUndo(); // FindComponent returns null; must no-op rather than throw
        Undo.PerformRedo();
    }

    [Fact]
    public void PasteValues_Undo_RestoresPreviousValues()
    {
        MakeScene(out var a, out var b);
        var src = a.AddComponent<ClipComp>();
        src.Value = 1;
        src.Label = "new";

        var dst = b.AddComponent<ClipComp>();
        dst.Value = 999;
        dst.Label = "old";

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteValues(dst);
        Undo.IncrementGroup();

        Assert.Equal(1, dst.Value);

        Undo.PerformUndo();
        Assert.Equal(999, dst.Value);
        Assert.Equal("old", dst.Label);

        Undo.PerformRedo();
        Assert.Equal(1, dst.Value);
        Assert.Equal("new", dst.Label);
    }

    [Fact]
    public void PasteValues_Undo_RestoresSceneReferences()
    {
        MakeScene(out var a, out var b);
        var dstTarget = b.AddComponent<ClipComp>();

        var src = a.AddComponent<ClipRefComp>();
        src.TargetGO = a;

        var dst = b.AddComponent<ClipRefComp>();
        dst.TargetGO = b;
        dst.TargetComp = dstTarget;

        ComponentClipboard.Copy(src);
        ComponentClipboard.PasteValues(dst);
        Undo.IncrementGroup();

        Assert.Same(a, dst.TargetGO);
        Assert.Null(dst.TargetComp);

        Undo.PerformUndo();

        // The pre-paste snapshot must round-trip references by identifier too, or undo would hand
        // back a deep-cloned orphan instead of the original object.
        Assert.Same(b, dst.TargetGO);
        Assert.Same(dstTarget, dst.TargetComp);
    }
}
