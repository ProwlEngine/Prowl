using System.IO;

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

    /// <summary>Writes the recording as compressed Echo binary. Copies are mostly zeros, so this shrinks them a lot.</summary>
    public static void Save(DeepRecording recording, string path)
    {
        Register();
        Serializer.Serialize(recording).WriteToBinary(new FileInfo(path), new BinarySerializationOptions { Compressed = true });
    }

    /// <summary>Reads a recording written by <see cref="Save"/>, or an uncompressed one from before files were compressed.</summary>
    public static DeepRecording Load(string path)
    {
        Register();
        return Serializer.Deserialize<DeepRecording>(EchoObject.ReadFromBinary(new FileInfo(path)))
            ?? throw new InvalidDataException($"{path} is not a Graphite debugger recording.");
    }
}
