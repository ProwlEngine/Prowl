#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

internal sealed class HookRecorder : ICaptureProfiler
{
    public readonly List<string> Log = new();
    public ViewCaptureInfo View;
    public readonly Dictionary<string, ResourceUse[]> Inputs = new();
    public readonly Dictionary<string, ResourceUse[]> Outputs = new();
    public readonly Dictionary<string, ResourceUse[]> Loaded = new();
    public readonly Dictionary<string, ExternalResourceInfo[]> Externals = new();
    public ulong Submitted;

    public void OnViewBegin(in ViewCaptureInfo view, ICaptureContext capture)
    {
        View = view;
        Log.Add($"ViewBegin:{view.Resources.Length}:{view.Passes.Length}");
    }

    public void OnPassBegin(in PassInfo pass, ReadOnlySpan<ResourceUse> inputs, ICaptureContext capture)
    {
        Inputs[pass.Name] = inputs.ToArray();
        Log.Add("PassBegin:" + pass.Name);
    }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<ResourceUse> outputs, ReadOnlySpan<ResourceUse> loadedAttachments, ReadOnlySpan<ExternalResourceInfo> externals, ICaptureContext capture)
    {
        Outputs[pass.Name] = outputs.ToArray();
        Loaded[pass.Name] = loadedAttachments.ToArray();
        Externals[pass.Name] = externals.ToArray();
        Log.Add("PassEnd:" + pass.Name);
    }

    public void OnViewEnd(ICaptureContext capture) => Log.Add("ViewEnd");

    public void OnExecutionSubmitted(ulong executionId)
    {
        Submitted = executionId;
        Log.Add("Submitted");
    }
}

file readonly struct HookView : IRenderView
{
    public uint PixelWidth => 32;
    public uint PixelHeight => 32;
    public int ViewId => 0;
    public string Name => "HookView";
}

file sealed class ProducePass : RasterPass
{
    private readonly RenderResourceID _target;
    private readonly DeviceBuffer _external;

    public ProducePass(RenderResourceID target, DeviceBuffer external)
    {
        _target = target;
        _external = external;
    }

    public override string Name => "Produce";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _target, GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

    public override void Render(RenderContext context, CommandBuffer cmd)
        => cmd.UpdateBuffer(_external, 0, new byte[] { 1, 2, 3, 4 });
}

file sealed class ConsumePass : IPass
{
    private readonly RenderResourceID _input;
    private readonly RenderResourceID _imported;
    private readonly RenderTexture _texture;

    public ConsumePass(RenderResourceID input, RenderResourceID imported, RenderTexture texture)
    {
        _input = input;
        _imported = imported;
        _texture = texture;
    }

    public string Name => "Consume";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareInputTexture(_input, TextureState.Sampled);
        builder.DeclareImportedTexture(_imported, _texture);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

file sealed class FinishPass : RasterPass
{
    private readonly RenderResourceID _input;
    private readonly RenderResourceID _target;

    public FinishPass(RenderResourceID input, RenderResourceID target)
    {
        _input = input;
        _target = target;
    }

    public override string Name => "Finish";

    public override void Setup(RenderContextBuilder builder)
    {
        builder.DeclareInputTexture(_input, TextureState.Sampled);
        SetTarget(builder, _target, GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));
    }

    public override void Render(RenderContext context, CommandBuffer cmd) { }
}

public abstract class CaptureHookTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static readonly RenderResourceID ColorA = RenderResourceID.Intern("capture_hook_a");
    private static readonly RenderResourceID ColorB = RenderResourceID.Intern("capture_hook_b");
    private static readonly RenderResourceID Imported = RenderResourceID.Intern("capture_hook_imported");

    private sealed record Run(HookRecorder Hook, DeviceBuffer External, ResourceVersion ExternalBefore, RenderTexture ImportedTexture, ResourceVersion ImportedBefore, ulong ExecutionId);

    private Run Execute()
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        external.Name = "ExternalMesh";
        RenderTexture imported = RF.CreateRenderTexture(new RenderTextureDescription(32, 32, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        ResourceVersion externalBefore = external.CurrentVersion;
        ResourceVersion importedBefore = imported.ColorTextures[0].CurrentVersion;
        HookRecorder hook = new();

        GD.Debug.Attach(hook);
        ExecutionTask task;
        try
        {
            using RenderPipeline pipeline = new([
                new ProducePass(ColorA, external),
                new ConsumePass(ColorA, Imported, imported),
                new FinishPass(ColorA, ColorB)]);
            task = GD.DispatchGraph(pipeline, new HookView[] { new() });
            GD.WaitForIdle();
        }
        finally
        {
            GD.Debug.Detach(hook);
        }

        return new Run(hook, external, externalBefore, imported, importedBefore, task.Id);
    }

    [SkippableFact]
    public void ThreePassGraph_CallbackOrder()
    {
        Run run = Execute();

        Assert.Equal(
            ["ViewBegin:3:3", "PassBegin:Produce", "PassEnd:Produce", "PassBegin:Consume", "PassEnd:Consume", "PassBegin:Finish", "PassEnd:Finish", "ViewEnd", "Submitted"],
            run.Hook.Log);
        Assert.Equal(run.ExecutionId, run.Hook.Submitted);
        Assert.Equal("HookView", run.Hook.View.ViewName);
        Assert.Equal(0, run.Hook.View.ViewIndex);
        Assert.Equal(32u, run.Hook.View.PixelWidth);
        Assert.Equal(32u, run.Hook.View.PixelHeight);
        Assert.Equal(run.ExecutionId, run.Hook.View.ExecutionId);
        Assert.Equal(new[] { "Produce", "Consume", "Finish" }, run.Hook.View.Passes.ToArray().Select(p => p.Pass.Name));
    }

    [SkippableFact]
    public void ResourceTable_ListsBackingsAndImportedFlag()
    {
        Run run = Execute();
        GraphResourceInfo[] resources = run.Hook.View.Resources.ToArray();

        GraphResourceInfo a = Assert.Single(resources, r => r.Id == ColorA);
        Assert.False(a.Imported);
        Assert.Equal(GraphResourceKind.Texture, a.Kind);
        GraphBacking aBacking = Assert.Single(a.Backings.ToArray());
        Assert.Equal(BackingRole.Color, aBacking.Role);
        Assert.NotNull(a.Texture);

        GraphResourceInfo imported = Assert.Single(resources, r => r.Id == Imported);
        Assert.True(imported.Imported);
        GraphBacking importedBacking = Assert.Single(imported.Backings.ToArray());
        Assert.Equal(run.ImportedTexture.ColorTextures[0].ResourceId, importedBacking.Id);
        Assert.Equal(run.ImportedBefore, importedBacking.EntryVersion);
        Assert.NotEqual(aBacking.Id, importedBacking.Id);
    }

    [SkippableFact]
    public void Uses_CarryVersionsAcrossPasses()
    {
        Run run = Execute();
        GraphBacking aBacking = Assert.Single(Assert.Single(run.Hook.View.Resources.ToArray(), r => r.Id == ColorA).Backings.ToArray());

        ResourceUse produced = Assert.Single(run.Hook.Outputs["Produce"]);
        Assert.Equal(ColorA, produced.Resource);
        Assert.Equal(aBacking.Id, produced.Version.Resource);
        Assert.Equal(aBacking.EntryVersion.Version + 1, produced.Version.Version);
        Assert.Equal(ResourceUsage.Attachment, produced.Usage);

        ResourceUse consumed = Assert.Single(run.Hook.Inputs["Consume"]);
        Assert.Equal(produced.Version, consumed.Version);
        Assert.Equal(ResourceUsage.Sampled, consumed.Usage);
        Assert.Empty(run.Hook.Inputs["Produce"]);
    }

    [SkippableFact]
    public void ExternalResources_AreReportedOnceAtFirstReference()
    {
        Run run = Execute();

        ExternalResourceInfo external = Assert.Single(run.Hook.Externals["Produce"]);
        Assert.Equal(run.External.ResourceId, external.Id);
        Assert.Equal("ExternalMesh", external.Name);
        Assert.Equal(run.ExternalBefore, external.EntryVersion);
        Assert.Equal(64u, external.Buffer!.Value.SizeInBytes);
        Assert.Null(external.Texture);
        Assert.Empty(run.Hook.Externals["Consume"]);
        Assert.Empty(run.Hook.Externals["Finish"]);
    }

    [SkippableFact]
    public void LoadedAttachments_ReportImportedTargetWithPreVersion()
    {
        Run run = Execute();

        ResourceUse loaded = Assert.Single(run.Hook.Loaded["Consume"]);
        Assert.Equal(Imported, loaded.Resource);
        Assert.Equal(run.ImportedBefore, loaded.Version);
        Assert.Empty(run.Hook.Loaded["Produce"]);
        Assert.Empty(run.Hook.Loaded["Finish"]);
    }

    [SkippableFact]
    public void NoCaptureAssigned_RunsNormally()
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        using RenderPipeline pipeline = new([new ProducePass(ColorA, external), new FinishPass(ColorA, ColorB)]);

        GD.DispatchGraph(pipeline, new HookView[] { new() });
        GD.WaitForIdle();

        Assert.Equal(1u, external.ContentVersion);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCaptureHookTests : CaptureHookTests<VulkanDeviceCreator> { }
#endif
