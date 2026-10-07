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
    public readonly Dictionary<string, PassReference[]> References = new();
    public ExecutionTask? Submitted;

    public void OnViewBegin(in ViewCaptureInfo view)
    {
        View = view;
        Log.Add($"ViewBegin:{view.Resources.Length}:{view.Passes.Length}");
    }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        References[pass.Name] = references.ToArray();
        Log.Add("PassEnd:" + pass.Name);
    }

    public void OnViewEnd() => Log.Add("ViewEnd");

    public void OnExecutionSubmitted(ExecutionTask task)
    {
        Submitted = task;
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
            ["ViewBegin:3:3", "PassEnd:Produce", "PassEnd:Consume", "PassEnd:Finish", "ViewEnd", "Submitted"],
            run.Hook.Log);
        Assert.Equal(run.ExecutionId, run.Hook.Submitted!.Id);
        Assert.Equal("HookView", run.Hook.View.ViewName);
        Assert.Equal(run.ExecutionId, run.Hook.View.ExecutionId);
        Assert.Equal(new[] { "Produce", "Consume", "Finish" }, run.Hook.View.Passes.ToArray().Select(p => p.Pass.Name));
    }

    [SkippableFact]
    public void ResourceTable_ListsBackingsAndOrigin()
    {
        Run run = Execute();
        GraphResourceInfo[] resources = run.Hook.View.Resources.ToArray();

        GraphResourceInfo a = Assert.Single(resources, r => r.Id == ColorA);
        Assert.Equal(GraphResourceOrigin.Transient, a.Origin);
        Assert.Single(a.Backings.ToArray());

        GraphResourceInfo imported = Assert.Single(resources, r => r.Id == Imported);
        Assert.Equal(GraphResourceOrigin.Imported, imported.Origin);
        GraphBacking importedBacking = Assert.Single(imported.Backings.ToArray());
        Assert.Equal(run.ImportedTexture.ColorTextures[0].ResourceId, importedBacking.Id);
        Assert.Equal(run.ImportedBefore, importedBacking.EntryVersion);
    }

    [SkippableFact]
    public void PassReferences_CoverDeclaredCommandAndAttachmentVersions()
    {
        Run run = Execute();
        GraphBacking aBacking = Assert.Single(Assert.Single(run.Hook.View.Resources.ToArray(), r => r.Id == ColorA).Backings.ToArray());
        uint aEntry = aBacking.EntryVersion.Version;

        PassReference[] produce = run.Hook.References["Produce"];
        PassReference produced = Assert.Single(produce, r => r.First.Resource == aBacking.Id);
        Assert.Equal(aEntry, produced.First.Version);
        Assert.Equal(aEntry + 1, produced.LastVersion);
        Assert.NotNull(produced.Texture);
        PassReference external = Assert.Single(produce, r => r.First.Resource == run.External.ResourceId);
        Assert.Equal(run.ExternalBefore, external.First);
        Assert.Equal(run.ExternalBefore.Version + 1, external.LastVersion);
        Assert.Equal("ExternalMesh", external.Name);
        Assert.Equal(64u, external.Buffer!.Value.SizeInBytes);

        PassReference[] consume = run.Hook.References["Consume"];
        PassReference sampled = Assert.Single(consume, r => r.First.Resource == aBacking.Id);
        Assert.Equal(aEntry + 1, sampled.First.Version);
        Assert.Equal(sampled.First.Version, sampled.LastVersion);
        PassReference attachment = Assert.Single(consume, r => r.First.Resource == run.ImportedTexture.ColorTextures[0].ResourceId);
        Assert.Equal(run.ImportedBefore, attachment.First);
        Assert.Equal(run.ImportedBefore.Version + 1, attachment.LastVersion);
        Assert.DoesNotContain(run.Hook.References["Finish"], r => r.First.Resource == run.External.ResourceId);
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
