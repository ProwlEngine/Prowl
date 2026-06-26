// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

[ExecutionOrder(-100)]
public sealed class EarlyComponent : MonoBehaviour { }

[ExecutionOrder(100)]
public sealed class LateComponent : MonoBehaviour { }

// A non-MonoBehaviour type, used to verify AddComponent(Type) rejects it.
public sealed class NotAComponent { }

#endregion

/// <summary>
/// Tests for GameObject component management: Add/Get/Remove, the GetComponent family
/// (including assignable/base-type lookups), [RequireComponent] enforcement and [ExecutionOrder] sorting.
/// </summary>
public class ComponentTests : RuntimeTestBase
{
    // ---- Add ----

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

    // ---- ExecutionOrder ----

    [Fact]
    public void ExecutionOrder_SortsComponentsAscending()
    {
        var go = CreateGameObject();
        // Add out of order to prove sorting, not insertion order, drives the result.
        go.AddComponent<LateComponent>();
        go.AddComponent<EarlyComponent>();
        go.AddComponent<PlainComponent>(); // default order 0

        var types = go.GetComponents().Select(c => c.GetType()).ToList();

        Assert.Equal(
            [typeof(EarlyComponent), typeof(PlainComponent), typeof(LateComponent)],
            types);
    }
}
