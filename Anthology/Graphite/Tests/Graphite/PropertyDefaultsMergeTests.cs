using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;


public class PropertyDefaultsMergeTests
{
    private readonly PropertySet _active = new();
    private readonly HashSet<PropertyID> _defaultKeys = new();
    private readonly List<PropertyID> _changed = new();


    private static PropertySet Set(float value)
    {
        PropertySet set = new();
        set.SetFloat("K", value);
        return set;
    }


    private float Value() => _active.Entries["K"].Uniform.As<float>();


    private void User(float value) => _active.MergeFrom(Set(value), _changed, _defaultKeys);


    private void Defaults(float value) => _active.MergeDefaults(Set(value), _changed, _defaultKeys);


    [Fact]
    public void DefaultsFillMissingKeys()
    {
        Defaults(1);

        Assert.Equal(1f, Value());
    }


    [Fact]
    public void DefaultsNeverReplaceEarlierUserValue()
    {
        User(5);
        _changed.Clear();

        Defaults(1);

        Assert.Equal(5f, Value());
        Assert.Empty(_changed);
    }


    [Fact]
    public void LaterUserValueReplacesDefault()
    {
        Defaults(1);

        User(5);

        Assert.Equal(5f, Value());
        Assert.DoesNotContain((PropertyID)"K", _defaultKeys);
    }


    [Fact]
    public void NewDefaultsReplaceOlderDefaults()
    {
        Defaults(1);

        Defaults(2);

        Assert.Equal(2f, Value());
    }


    [Fact]
    public void DefaultsAfterUserOverrideOfDefaultStillKeepUserValue()
    {
        Defaults(1);
        User(5);

        Defaults(2);

        Assert.Equal(5f, Value());
    }


    [Fact]
    public void AppliedDefaultsAreReportedChanged()
    {
        Defaults(1);

        Assert.Contains((PropertyID)"K", _changed);
    }
}
