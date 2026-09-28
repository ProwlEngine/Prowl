using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A clip's events in a model's import settings, which is where the inspector edits them.</summary>
public class ModelClipEventSettingsTests
{
    [Fact]
    public void EventsWrittenToTheSettingsReadBackAndReachTheImport()
    {
        EchoObject settings = EchoObject.NewCompound();
        ModelImportOverrides.WriteEvents(settings, "mixamo.com", new[]
        {
            new ClipEvent { Kind = ClipEventKind.Foot, Time = 0.4f, Option = 1 },
            new ClipEvent { Kind = ClipEventKind.RootMotion, Time = 1f, Length = 0.3f, BlendTime = 0.25f },
            new ClipEvent { Kind = ClipEventKind.TransitionWindow, Time = 2f, Length = 0.5f, Name = "Exit", Option = 2 },
        });

        // Through text, as the .meta file holds it.
        EchoObject reloaded = EchoObject.ReadFromString(settings.WriteToString());
        List<ClipEvent> events = ModelImportOverrides.ReadEvents(ModelImportOverrides.ReadClipBlock(reloaded, "mixamo.com"));

        Assert.Equal(3, events.Count);
        Assert.Equal(ClipEventKind.Foot, events[0].Kind);
        Assert.Equal(1, events[0].Option);
        Assert.Equal(0.25f, events[1].BlendTime, 3);
        Assert.Equal("Exit", events[2].Name);
        Assert.Equal(0.5f, events[2].Length, 3);

        Dictionary<string, ModelClipSettings> clips = ModelImportOverrides.ReadClips(reloaded)!;
        Assert.Equal(3, clips["mixamo.com"].Events!.Count);
    }

    /// <summary>The kind is stored by name, so reordering the kinds cannot turn one into another in saved files.</summary>
    [Fact]
    public void TheKindIsStoredByName()
    {
        EchoObject settings = EchoObject.NewCompound();
        ModelImportOverrides.WriteEvents(settings, "clip", new[] { new ClipEvent { Kind = ClipEventKind.TargetWarp } });

        EchoObject entry = ModelImportOverrides.ReadClipBlock(settings, "clip")![ModelImportKeys.ClipEvents].List[0];
        Assert.Equal("TargetWarp", entry["kind"].StringValue);
    }
}
