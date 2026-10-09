using System;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Serialization;

internal sealed class RecordingFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(Recording) || type == typeof(DeepRecording);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        EchoObject compound = EchoObject.NewCompound();
        if (value is Recording recording)
        {
            compound.Add("ExecutionId", Serializer.Serialize(typeof(ulong), recording.ExecutionId, context));
            compound.Add("GraphName", Serializer.Serialize(typeof(string), recording.GraphName, context));
            compound.Add("Views", Serializer.Serialize(typeof(EquatableArray<RecordedView>), recording.Views, context));
            compound.Add("CommandBuffers", Serializer.Serialize(typeof(EquatableArray<RecordedCommandBuffer>), recording.CommandBuffers, context));
            return compound;
        }

        DeepRecording deep = (DeepRecording)value;
        compound.Add("Mode", Serializer.Serialize(typeof(DeepMode), deep.Mode, context));
        compound.Add("Backend", Serializer.Serialize(typeof(GraphicsBackend), deep.Backend, context));
        compound.Add("Features", Serializer.Serialize(typeof(RecordedFeatures), deep.Features, context));
        compound.Add("Recording", Serialize(typeof(Recording), deep.Recording, context));
        compound.Add("Result", Serializer.Serialize(typeof(DeepResult), deep.Result, context));
        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (targetType == typeof(Recording))
            return new Recording(
                Serializer.Deserialize<ulong>(value["ExecutionId"], context),
                Serializer.Deserialize<string>(value["GraphName"], context) ?? "",
                Serializer.Deserialize<EquatableArray<RecordedView>>(value["Views"], context),
                Serializer.Deserialize<EquatableArray<RecordedCommandBuffer>>(value["CommandBuffers"], context));

        return new DeepRecording(
            Serializer.Deserialize<DeepMode>(value["Mode"], context),
            Serializer.Deserialize<GraphicsBackend>(value["Backend"], context),
            Serializer.Deserialize<RecordedFeatures>(value["Features"], context)!,
            (Recording)Deserialize(value["Recording"], typeof(Recording), context)!,
            Serializer.Deserialize<DeepResult>(value["Result"], context)!);
    }
}
