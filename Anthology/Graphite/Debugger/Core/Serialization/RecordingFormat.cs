using System;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class RecordingFormat : ISerializationFormat
{
    private sealed record RecordingData(
        GraphicsBackend Backend,
        string DeviceName,
        RecordedFeatures Features,
        ulong ExecutionId,
        string GraphName,
        EquatableArray<RecordedView> Views,
        EquatableArray<RecordedCommandBuffer> CommandBuffers);

    private sealed record DeepRecordingData(
        DeepMode Mode,
        RecordingData Recording,
        DeepResult Result);

    public bool CanHandle(Type type) => type == typeof(Recording) || type == typeof(DeepRecording);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        => value is Recording recording
            ? Serializer.Serialize(typeof(RecordingData), Pack(recording), context)
            : Serializer.Serialize(typeof(DeepRecordingData), Pack((DeepRecording)value), context);

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (targetType == typeof(Recording))
            return Unpack(Serializer.Deserialize<RecordingData>(value, context)!);

        DeepRecordingData data = Serializer.Deserialize<DeepRecordingData>(value, context)!;
        return new DeepRecording(data.Mode, Unpack(data.Recording), data.Result);
    }

    private static RecordingData Pack(Recording recording)
        => new(recording.Backend, recording.DeviceName, recording.Features, recording.ExecutionId, recording.GraphName, recording.Views, recording.CommandBuffers);

    private static DeepRecordingData Pack(DeepRecording deep)
        => new(deep.Mode, Pack(deep.Recording), deep.Result);

    private static Recording Unpack(RecordingData data)
        => new(data.Backend, data.DeviceName ?? "", data.Features, data.ExecutionId, data.GraphName ?? "", data.Views, data.CommandBuffers);
}
