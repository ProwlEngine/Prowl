// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the <see cref="EngineObject"/> base type: instance identity, disposal semantics,
/// reference equality, the IsValid/IsNotValid extensions, and default field values.
/// </summary>
public class EngineObjectTests
{
    private sealed class TestEngineObject : EngineObject
    {
        public int DisposeCount;
        public TestEngineObject() : base() { }
        public TestEngineObject(string name) : base(name) { }
        public override void OnDispose() => DisposeCount++;
    }

    [Fact]
    public void InstanceID_IsUniqueAndIncreasing()
    {
        var a = new TestEngineObject();
        var b = new TestEngineObject();
        var c = new TestEngineObject();

        Assert.True(a.InstanceID < b.InstanceID);
        Assert.True(b.InstanceID < c.InstanceID);
        Assert.Equal(3, new[] { a.InstanceID, b.InstanceID, c.InstanceID }.Distinct().Count());
    }

    [Fact]
    public void InstanceID_SurvivesDisposal()
    {
        var obj = new TestEngineObject();
        int id = obj.InstanceID;

        obj.Dispose();

        Assert.Equal(id, obj.InstanceID);
    }

    [Fact]
    public void Dispose_SetsIsDisposed()
    {
        var obj = new TestEngineObject();
        Assert.False(obj.IsDisposed);

        obj.Dispose();

        Assert.True(obj.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_CallsOnDisposeOnce()
    {
        var obj = new TestEngineObject();

        obj.Dispose();
        obj.Dispose();
        obj.Dispose();

        Assert.Equal(1, obj.DisposeCount);
    }

    [Fact]
    public void Equality_IsReferenceBased()
    {
        var a = new TestEngineObject("same");
        var b = new TestEngineObject("same");

        Assert.True(a == a);
        Assert.False(a == b);     // identical names, different instances
        Assert.True(a != b);
        Assert.True(a.Equals(a));
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equality_WithNull()
    {
        var a = new TestEngineObject();
        EngineObject? n = null;

        Assert.False(a == n);
        Assert.True(a != n);
        Assert.True(n == null);
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void GetHashCode_IsInstanceID()
    {
        var obj = new TestEngineObject();
        Assert.Equal(obj.InstanceID, obj.GetHashCode());
    }

    [Fact]
    public void IsValid_And_IsNotValid()
    {
        EngineObject? nil = null;
        Assert.False(nil.IsValid());
        Assert.True(nil.IsNotValid());

        var obj = new TestEngineObject();
        Assert.True(obj.IsValid());
        Assert.False(obj.IsNotValid());

        obj.Dispose();
        Assert.False(obj.IsValid());
        Assert.True(obj.IsNotValid());
    }

    [Fact]
    public void DefaultName_IsNewPlusTypeName()
    {
        var obj = new TestEngineObject();
        Assert.Equal("New" + nameof(TestEngineObject), obj.Name);
    }

    [Fact]
    public void Constructor_SetsProvidedName()
    {
        var obj = new TestEngineObject("Custom");
        Assert.Equal("Custom", obj.Name);
    }

    [Fact]
    public void Defaults_AssetIdEmpty_AssetPathEmpty()
    {
        var obj = new TestEngineObject();
        Assert.Equal(Guid.Empty, obj.AssetID);
        Assert.Equal(string.Empty, obj.AssetPath);
    }
}
