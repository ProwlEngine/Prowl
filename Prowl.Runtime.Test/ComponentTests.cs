// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;


using Xunit;

namespace Prowl.Runtime.Test;

#region Test components

public sealed class PlainComponent : MonoBehaviour { }

public sealed class SecondComponent : MonoBehaviour { }

public class BaseTestComponent : MonoBehaviour { }

public sealed class DerivedTestComponent : BaseTestComponent { }

[RequireComponent(typeof(PlainComponent))]
public sealed class NeedsPlain : MonoBehaviour { }

// Chains through NeedsPlain, which itself requires PlainComponent.
[RequireComponent(typeof(NeedsPlain))]
public sealed class NeedsChain : MonoBehaviour { }

[RequireComponent(typeof(PlainComponent), typeof(SecondComponent))]
public sealed class NeedsTwo : MonoBehaviour { }

[RequireComponent(typeof(PlainComponent))]
[RequireComponent(typeof(SecondComponent))]
public sealed class NeedsStacked : MonoBehaviour { }

[DisallowMultipleComponent]
public sealed class OnlyOne : MonoBehaviour { }

[DisallowMultipleComponent]
public class OnlyOneBase : MonoBehaviour { }

public sealed class OnlyOneDerived : OnlyOneBase { }

[ExecutionOrder(-100)]
public sealed class EarlyComponent : MonoBehaviour { }

[ExecutionOrder(100)]
public sealed class LateComponent : MonoBehaviour { }

// A non-MonoBehaviour type, used to verify AddComponent(Type) rejects it.
public sealed class NotAComponent { }

// Requires itself. The requirement is satisfied by the very component being added.
[RequireComponent(typeof(SelfRequiring))]
public sealed class SelfRequiring : MonoBehaviour { }

// Two components that require each other, which is a cycle no walk can bottom out on.
[RequireComponent(typeof(MutualB))]
public sealed class MutualA : MonoBehaviour { }

[RequireComponent(typeof(MutualA))]
public sealed class MutualB : MonoBehaviour { }

// A three step cycle, to check the guard is not just a one level lookback.
[RequireComponent(typeof(RingB))]
public sealed class RingA : MonoBehaviour { }

[RequireComponent(typeof(RingC))]
public sealed class RingB : MonoBehaviour { }

[RequireComponent(typeof(RingA))]
public sealed class RingC : MonoBehaviour { }

// A component that cannot be constructed without arguments.
public sealed class NoDefaultConstructor : MonoBehaviour
{
    public NoDefaultConstructor(int _) { }
}

// Throws from its constructor, the way a field initializer calling a null delegate does.
public sealed class ThrowsWhenConstructed : MonoBehaviour
{
    public ThrowsWhenConstructed() => throw new InvalidOperationException("no");
}

// Throws only while armed, so an object holding one can be built and then copied.
public sealed class ThrowsWhenArmed : MonoBehaviour
{
    public static bool Armed;

    public ThrowsWhenArmed()
    {
        if (Armed) throw new InvalidOperationException("no");
    }
}

#endregion

/// <summary>
/// Tests for GameObject component management: Add/Get/Remove, the GetComponent family
/// (including assignable/base-type lookups), [RequireComponent] enforcement and [ExecutionOrder] sorting.
/// </summary>
public class ComponentTests : RuntimeTestBase
{
    // ---- Renderer interfaces ----

    [Fact]
    public void MaterialRenderer_ListSlots_GrowByOneAndRejectGaps()
    {
        IMaterialRenderer renderer = CreateGameObject().AddComponent<MeshRenderer>();

        Assert.Equal(0, renderer.MaterialSlotCount);
        renderer.SetMaterial(0, null);
        renderer.SetMaterial(1, null);
        Assert.Equal(2, renderer.MaterialSlotCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetMaterial(5, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.GetMaterial(2));
    }

    [Fact]
    public void MaterialRenderer_SingleSlot_OnlyAcceptsSlotZero()
    {
        IMaterialRenderer renderer = CreateGameObject().AddComponent<SpriteRenderer>();

        Assert.Equal(1, renderer.MaterialSlotCount);
        Assert.Null(renderer.GetMaterial(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetMaterial(1, null));
    }

    [Fact]
    public void ColorTint_WritesTheRendererColor()
    {
        var sprite = CreateGameObject().AddComponent<SpriteRenderer>();
        var skinned = CreateGameObject().AddComponent<SkinnedMeshRenderer>();
        var text = CreateGameObject().AddComponent<TextMeshComponent>();

        ((IColorTint)sprite).Tint = Color.Red;
        ((IColorTint)skinned).Tint = Color.Green;
        ((IColorTint)text).Tint = Color.Blue;

        Assert.Equal(Color.Red, sprite.Color);
        Assert.Equal(Color.Green, skinned.MainColor);
        Assert.Equal(Color.Blue, text.TextColor);
    }

    // ---- Add ----

    /// <summary>
    /// A component whose requirement is itself has to stop, not recurse. The requirement walk runs
    /// before the component is on the object, so nothing it can look at will ever satisfy it.
    /// </summary>
    [Fact]
    public void AddComponent_SelfRequirement_AddsOnceAndTerminates()
    {
        var go = CreateGameObject();

        var comp = go.AddComponent<SelfRequiring>();

        Assert.NotNull(comp);
        Assert.Single(go.GetComponents<SelfRequiring>());
    }

    [Fact]
    public void AddComponent_MutualRequirement_AddsBothAndTerminates()
    {
        var go = CreateGameObject();

        var comp = go.AddComponent<MutualA>();

        Assert.NotNull(comp);
        Assert.Single(go.GetComponents<MutualA>());
        Assert.Single(go.GetComponents<MutualB>());
    }

    [Fact]
    public void AddComponent_RequirementRing_AddsEachOnceAndTerminates()
    {
        var go = CreateGameObject();

        go.AddComponent<RingA>();

        Assert.Single(go.GetComponents<RingA>());
        Assert.Single(go.GetComponents<RingB>());
        Assert.Single(go.GetComponents<RingC>());
    }

    /// <summary>
    /// Every path that adds a component by type reaches this, including dragging a script onto the
    /// inspector, so a type that cannot be constructed has to be refused rather than thrown from.
    /// </summary>
    /// <summary>
    /// A constructor is user code, and a field initializer is compiled into one, so it can throw
    /// anything. That has to be contained rather than escaping into whatever asked for the component.
    /// </summary>
    [Fact]
    public void AddComponent_WhoseConstructorThrows_ReturnsNullInsteadOfPropagating()
    {
        var go = CreateGameObject();

        var comp = go.AddComponent(typeof(ThrowsWhenConstructed));

        Assert.Null(comp);
        Assert.Empty(go.GetComponents<ThrowsWhenConstructed>());
    }

    /// <summary>The object it failed on stays usable, rather than being left half built.</summary>
    [Fact]
    public void AddComponent_AfterAConstructorThrows_TheObjectStillWorks()
    {
        var go = CreateGameObject();

        go.AddComponent(typeof(ThrowsWhenConstructed));
        var plain = go.AddComponent<PlainComponent>();

        Assert.NotNull(plain);
        Assert.Single(go.GetComponents<PlainComponent>());
    }

    /// <summary>
    /// A constructor that only fails later, which is what a delegate that was assigned and then
    /// cleared looks like, must not take the copy down with it.
    /// </summary>
    [Fact]
    public void Cloning_WhenAComponentsConstructorThrows_SkipsItRatherThanFailing()
    {
        var go = CreateGameObject();
        Assert.NotNull(go.AddComponent(typeof(ThrowsWhenArmed)));
        go.AddComponent<PlainComponent>();

        GameObject copy;
        ThrowsWhenArmed.Armed = true;
        try
        {
            copy = ObjectCopy.Clone(go);
        }
        finally
        {
            ThrowsWhenArmed.Armed = false;
        }

        Assert.NotNull(copy);
        Assert.Empty(copy.GetComponents<ThrowsWhenArmed>());
        Assert.Single(copy.GetComponents<PlainComponent>());
    }

    [Fact]
    public void AddComponent_WithoutAParameterlessConstructor_ReturnsNull()
    {
        var go = CreateGameObject();

        var comp = go.AddComponent(typeof(NoDefaultConstructor));

        Assert.Null(comp);
        Assert.Empty(go.GetComponents<NoDefaultConstructor>());
    }


    [Fact]
    public void AddComponent_ReturnsInstance_WiredToGameObject()
    {
        var go = CreateGameObject();

        var comp = go.AddComponent<PlainComponent>();

        Assert.NotNull(comp);
        Assert.Same(go, comp.GameObject);
        Assert.Same(go.Transform, comp.Transform);
        Assert.Same(comp, go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void AddComponent_NonMonoBehaviourType_ReturnsNull()
    {
        var go = CreateGameObject();

        MonoBehaviour? result = go.AddComponent(typeof(NotAComponent));

        Assert.Null(result);
        Assert.Empty(go.GetComponents());
    }

    [Fact]
    public void AddComponent_SameTypeTwice_CreatesTwoInstances()
    {
        var go = CreateGameObject();

        var a = go.AddComponent<PlainComponent>();
        var b = go.AddComponent<PlainComponent>();

        Assert.NotSame(a, b);
        Assert.Equal(2, go.GetComponents<PlainComponent>().Count());
    }

    [Fact]
    public void AddComponent_DoesNotLeakToOtherGameObjects()
    {
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");

        a.AddComponent<PlainComponent>();

        Assert.NotNull(a.GetComponent<PlainComponent>());
        Assert.Null(b.GetComponent<PlainComponent>());
    }

    // ---- Get ----

    [Fact]
    public void GetComponent_ReturnsNull_WhenAbsent()
    {
        var go = CreateGameObject();
        Assert.Null(go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void GetComponent_ByBaseType_ReturnsDerivedInstance()
    {
        var go = CreateGameObject();
        var derived = go.AddComponent<DerivedTestComponent>();

        // Lookup by base type isn't an exact cache key, so this exercises the assignable fallback.
        Assert.Same(derived, go.GetComponent<BaseTestComponent>());
    }

    [Fact]
    public void GetComponents_ByBaseType_IncludesDerived()
    {
        var go = CreateGameObject();
        var derived = go.AddComponent<DerivedTestComponent>();

        var found = go.GetComponents<BaseTestComponent>().ToList();

        Assert.Single(found);
        Assert.Same(derived, found[0]);
    }

    [Fact]
    public void GetComponent_MultipleSameType_ReturnsFirstAdded()
    {
        var go = CreateGameObject();
        var first = go.AddComponent<PlainComponent>();
        go.AddComponent<PlainComponent>();

        Assert.Same(first, go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void TryGetComponent_ReflectsPresence()
    {
        var go = CreateGameObject();

        Assert.False(go.TryGetComponent<PlainComponent>(out _));

        var comp = go.AddComponent<PlainComponent>();

        Assert.True(go.TryGetComponent<PlainComponent>(out var found));
        Assert.Same(comp, found);
    }

    [Fact]
    public void GetComponentByIdentifier_FindsComponent_AndRejectsEmpty()
    {
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();

        Assert.Same(comp, go.GetComponentByIdentifier(comp.Identifier));
        Assert.Null(go.GetComponentByIdentifier(Guid.Empty));
    }

    [Fact]
    public void GetComponents_NoArgs_ReturnsAllComponents()
    {
        var go = CreateGameObject();
        go.AddComponent<PlainComponent>();
        go.AddComponent<SecondComponent>();

        Assert.Equal(2, go.GetComponents().Count());
    }

    // ---- Remove ----

    [Fact]
    public void RemoveComponent_RemovesInstance()
    {
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();

        go.RemoveComponent(comp);

        Assert.Null(go.GetComponent<PlainComponent>());
        Assert.Empty(go.GetComponents());
    }

    [Fact]
    public void RemoveComponent_Generic_RemovesOnlyThatInstance()
    {
        var go = CreateGameObject();
        var a = go.AddComponent<PlainComponent>();
        var b = go.AddComponent<PlainComponent>();

        go.RemoveComponent<PlainComponent>(a);

        var remaining = go.GetComponents<PlainComponent>().ToList();
        Assert.Single(remaining);
        Assert.Same(b, remaining[0]);
    }

    [Fact]
    public void RemoveComponent_ByGuid_RemovesInstance()
    {
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();

        go.RemoveComponent(comp.Identifier);

        Assert.Null(go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void RemoveAll_RemovesEveryInstanceOfType()
    {
        var go = CreateGameObject();
        go.AddComponent<PlainComponent>();
        go.AddComponent<PlainComponent>();
        go.AddComponent<SecondComponent>();

        go.RemoveAll<PlainComponent>();

        Assert.Empty(go.GetComponents<PlainComponent>());
        Assert.NotNull(go.GetComponent<SecondComponent>());
    }

    [Fact]
    public void RemoveComponent_LeavesOthersIntact()
    {
        var go = CreateGameObject();
        var plain = go.AddComponent<PlainComponent>();
        var second = go.AddComponent<SecondComponent>();

        go.RemoveComponent(plain);

        Assert.Null(go.GetComponent<PlainComponent>());
        Assert.Same(second, go.GetComponent<SecondComponent>());
    }

    // ---- RequireComponent ----

    [Fact]
    public void RequireComponent_AddsDependency()
    {
        var go = CreateGameObject();

        go.AddComponent<NeedsPlain>();

        Assert.NotNull(go.GetComponent<PlainComponent>());
        Assert.NotNull(go.GetComponent<NeedsPlain>());
    }

    [Fact]
    public void RequireComponent_DoesNotDuplicate_WhenDependencyAlreadyPresent()
    {
        var go = CreateGameObject();
        var existing = go.AddComponent<PlainComponent>();

        go.AddComponent<NeedsPlain>();

        var plains = go.GetComponents<PlainComponent>().ToList();
        Assert.Single(plains);
        Assert.Same(existing, plains[0]);
    }

    [Fact]
    public void RequireComponent_Chain_AddsTransitiveDependencies()
    {
        var go = CreateGameObject();

        go.AddComponent<NeedsChain>();

        Assert.NotNull(go.GetComponent<NeedsChain>());
        Assert.NotNull(go.GetComponent<NeedsPlain>());
        Assert.NotNull(go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void RequireComponent_Multiple_AddsAllDependencies()
    {
        var go = CreateGameObject();

        go.AddComponent<NeedsTwo>();

        Assert.NotNull(go.GetComponent<PlainComponent>());
        Assert.NotNull(go.GetComponent<SecondComponent>());
        Assert.NotNull(go.GetComponent<NeedsTwo>());
    }

    [Fact]
    public void RemoveComponent_RequiredByAnother_IsBlocked()
    {
        var go = CreateGameObject();
        go.AddComponent<NeedsPlain>(); // also adds PlainComponent
        var plain = go.GetComponent<PlainComponent>();

        // PlainComponent is required by NeedsPlain, so removal must be refused.
        go.RemoveComponent(plain!);

        Assert.NotNull(go.GetComponent<PlainComponent>());
    }

    [Fact]
    public void RequireComponent_Stacked_AddsEveryDependency()
    {
        var go = CreateGameObject();

        go.AddComponent<NeedsStacked>();

        Assert.NotNull(go.GetComponent<PlainComponent>());
        Assert.NotNull(go.GetComponent<SecondComponent>());
    }

    [Fact]
    public void RemoveComponent_RequiredByStackedAttribute_IsBlocked()
    {
        var go = CreateGameObject();
        go.AddComponent<NeedsStacked>();

        go.RemoveComponent(go.GetComponent<SecondComponent>()!);

        Assert.NotNull(go.GetComponent<SecondComponent>());
    }

    // ---- DisallowMultipleComponent ----

    [Fact]
    public void DisallowMultiple_SecondAdd_IsRefused()
    {
        var go = CreateGameObject();
        var first = go.AddComponent<OnlyOne>();

        var second = go.AddComponent<OnlyOne>();

        Assert.Null(second);
        Assert.Same(first, Assert.Single(go.GetComponents<OnlyOne>()));
    }

    [Fact]
    public void DisallowMultiple_CoversSubclassesOfTheMarkedType()
    {
        var go = CreateGameObject();
        go.AddComponent<OnlyOneBase>();

        Assert.Null(go.AddComponent<OnlyOneDerived>());
        Assert.Single(go.GetComponents<OnlyOneBase>());
    }

    [Fact]
    public void DisallowMultiple_AttachingAnInstance_IsRefused()
    {
        var go = CreateGameObject();
        go.AddComponent<OnlyOne>();
        var loose = CreateGameObject().AddComponent<OnlyOne>();

        Assert.False(go.AddComponent(loose));
        Assert.Single(go.GetComponents<OnlyOne>());
    }

    [Fact]
    public void DisallowMultiple_OnAnotherObject_IsAllowed()
    {
        Assert.NotNull(CreateGameObject().AddComponent<OnlyOne>());
        Assert.NotNull(CreateGameObject().AddComponent<OnlyOne>());
    }

    // ---- Enumeration safety ----

    // GetComponents used to yield straight off the live list, so adding one while walking the
    // result threw "Collection was modified".
    [Fact]
    public void GetComponents_TolerartesAddDuringEnumeration()
    {
        var go = CreateGameObject();
        go.AddComponent<PlainComponent>();
        go.AddComponent<PlainComponent>();

        int seen = 0;
        foreach (var _ in go.GetComponents<PlainComponent>())
        {
            seen++;
            go.AddComponent<PlainComponent>();
        }

        Assert.Equal(2, seen); // the snapshot taken when enumeration started
        Assert.Equal(4, go.GetComponents<PlainComponent>().Count());
    }

    [Fact]
    public void GetComponents_TolerartesRemoveDuringEnumeration()
    {
        var go = CreateGameObject();
        var a = go.AddComponent<PlainComponent>();
        go.AddComponent<PlainComponent>();

        foreach (var _ in go.GetComponents<PlainComponent>())
            go.RemoveComponent(a);

        Assert.Single(go.GetComponents<PlainComponent>());
    }

    // ---- Ownership ----

    [Fact]
    public void RemoveComponent_FromWrongGameObject_DoesNothing()
    {
        var scene = CreateScene(enable: true);
        var owner = CreateGameObject("Owner");
        var other = CreateGameObject("Other");
        var comp = owner.AddComponent<PlainComponent>();
        scene.Add(owner); scene.Add(other);

        other.RemoveComponent(comp);       // non-generic overload
        other.RemoveComponent<PlainComponent>(comp); // generic overload
        EngineObject.ProcessDestroyed();

        Assert.True(comp.IsValid(), "A GameObject that does not own the component must not destroy it.");
        Assert.Same(comp, owner.GetComponent<PlainComponent>());
        Assert.Same(owner, comp.GameObject);
    }

    [Fact]
    public void AddComponent_Instance_MovesItOffItsPreviousGameObject()
    {
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var comp = a.AddComponent<PlainComponent>();

        b.AddComponent(comp);

        Assert.Same(b, comp.GameObject);
        Assert.Empty(a.GetComponents<PlainComponent>());
        Assert.Same(comp, b.GetComponent<PlainComponent>());
    }

    [Fact]
    public void AddComponent_Instance_SurvivesDisposalOfThePreviousGameObject()
    {
        var scene = CreateScene(enable: true);
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var comp = a.AddComponent<PlainComponent>();
        scene.Add(a); scene.Add(b);

        b.AddComponent(comp);
        a.Dispose();
        EngineObject.ProcessDestroyed();

        Assert.True(comp.IsValid(), "Disposing the old GameObject must not destroy a component that moved away.");
        Assert.Same(comp, b.GetComponent<PlainComponent>());
    }

    // ---- ExecutionOrder ----

    [Fact]
    public void GetComponents_ReturnsInsertionOrder()
    {
        // Component storage is insertion-ordered; execution order is applied by the scene update
        // loop, not by GetComponents() (see UpdateLoopTests for the execution-order behavior).
        var go = CreateGameObject();
        go.AddComponent<LateComponent>();   // [ExecutionOrder(100)]
        go.AddComponent<EarlyComponent>();  // [ExecutionOrder(-100)]
        go.AddComponent<PlainComponent>();  // default order 0

        var types = go.GetComponents().Select(c => c.GetType()).ToList();

        Assert.Equal(
            [typeof(LateComponent), typeof(EarlyComponent), typeof(PlainComponent)],
            types);
    }
}
