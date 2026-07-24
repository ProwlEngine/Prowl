// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

namespace Prowl.Echo.Test;

// Shared round-trip helpers for the serialization test suites.
internal static class RoundtripTestHelpers
{
    public static T Roundtrip<T>(T v) => Serializer.Deserialize<T>(Serializer.Serialize(v))!;

    public static T RoundtripText<T>(T v)
        => Serializer.Deserialize<T>(EchoObject.ReadFromString(Serializer.Serialize(v).WriteToString()))!;

    public static T RoundtripJson<T>(T v)
        => Serializer.Deserialize<T>(EchoObject.ReadFromJson(Serializer.Serialize(v).WriteToJson()))!;

    public static T RoundtripBinary<T>(T v)
    {
        var echo = Serializer.Serialize(v);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            echo.WriteToBinary(w);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        return Serializer.Deserialize<T>(EchoObject.ReadFromBinary(r))!;
    }
}
