using Prowl.Echo;

namespace Prowl.Graphite.Debugger.Serialization;

/// <summary>Registers the Echo formats for recordings. Call once before serializing a recording.</summary>
public static class DebuggerSerialization
{
    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Registers the formats. Calling it again does nothing.</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
                return;

            Serializer.RegisterFormat(new EquatableArrayFormat());
            Serializer.RegisterFormat(new TraceFormat());
            Serializer.RegisterFormat(new RecordFormat());
            Serializer.RegisterFormat(new RecordingFormat());
            _registered = true;
        }
    }
}
