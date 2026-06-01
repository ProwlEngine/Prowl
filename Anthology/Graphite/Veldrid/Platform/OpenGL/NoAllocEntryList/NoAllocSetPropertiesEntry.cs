namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocSetPropertiesEntry
{
    public readonly Tracked<PropertySet> Properties;

    public NoAllocSetPropertiesEntry(Tracked<PropertySet> properties)
    {
        Properties = properties;
    }
}
