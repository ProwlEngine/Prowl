using Prowl.Runtime.Networking;

namespace Prowl.Runtime.Test;

/// <summary>Protocol fixtures may serialize a scene before starting an engine session.</summary>
internal static class NetworkTestScene
{
    private static NetworkSystem? s_serialization;
    internal static NetworkSystem Current => Networking.Networking.System
        ?? (s_serialization ??= new(Networking.Networking.Configuration));

    internal static void Reset()
    {
        s_serialization?.Dispose();
        s_serialization = null;
    }
}
