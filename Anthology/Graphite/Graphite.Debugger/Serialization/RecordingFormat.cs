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
            compound.Add("Executions", Serializer.Serialize(typeof(EquatableArray<RecordedExecution>), recording.Executions, context));
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
            return new Recording(Serializer.Deserialize<EquatableArray<RecordedExecution>>(value["Executions"], context));

        return new DeepRecording(
            Serializer.Deserialize<DeepMode>(value["Mode"], context),
            Serializer.Deserialize<GraphicsBackend>(value["Backend"], context),
            Serializer.Deserialize<RecordedFeatures>(value["Features"], context)!,
            (Recording)Deserialize(value["Recording"], typeof(Recording), context)!,
            Serializer.Deserialize<DeepResult>(value["Result"], context)!);
    }
}
