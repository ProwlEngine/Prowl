#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

internal sealed class HookRecorder : ICaptureProfiler
{
    public readonly List<string> Log = new();
    public GroupCaptureInfo Group;
    public readonly Dictionary<string, PassReference[]> References = new();
    public ulong ExecutionId;
    public string GraphName = "";

    public void BeginExecution(ulong executionId, string graphName)
    {
        ExecutionId = executionId;
        GraphName = graphName;
        Log.Add("Begin");
    }

    public void EndExecution() => Log.Add("End");

    public void DescribeGroup(in GroupCaptureInfo group)
    {
        Group = group;
        Log.Add($"Describe:{group.Resources.Length}:{group.Passes.Length}");
    }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        References[pass.Name] = references.ToArray();
        Log.Add("PassEnd:" + pass.Name);
    }
}

public abstract class CaptureHookTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static readonly RenderResourceID ColorA = RenderResourceID.Intern("capture_hook_a");
    private static readonly RenderResourceID ColorB = RenderResourceID.Intern("capture_hook_b");
    private static readonly TargetLoadStoreOps ClearBlack = TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1));

    private sealed record Run(HookRecorder Hook, DeviceBuffer External, ResourceVersion ExternalBefore, RenderTexture ImportedTexture, ResourceVersion ImportedBefore, ulong ExecutionId);

    private static void AddPasses(FrameGraph graph, DeviceBuffer external, RenderTexture? imported)
    {
        using (graph.Group("View"))
        {
            TextureHandle created = graph.Create(ColorA, GraphTextureDesc.Sized(32, 32, PixelFormat.R8_G8_B8_A8_UNorm));
            TextureHandle a = created;
            graph.AddPass("Produce", pass => a = pass.Write(created, firstWrite: ClearBlack), context => context.Command.UpdateBuffer(external, 0, new byte[] { 1, 2, 3, 4 }));
            if (imported != null)
            {
                TextureHandle import = graph.Import(imported);
                graph.AddPass("Consume", pass =>
                {
                    pass.Read(a);
                    pass.Write(import);
                }, _ => { });
            }

            TextureHandle b = graph.Create(ColorB, GraphTextureDesc.Sized(32, 32, PixelFormat.R8_G8_B8_A8_UNorm));
            graph.AddPass("Finish", pass =>
            {
                pass.Read(a);
                pass.Write(b, firstWrite: ClearBlack);
            }, _ => { });
        }
    }

    private Run Execute()
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        external.Name = "ExternalMesh";
        RenderTexture imported = RF.CreateRenderTexture(new RenderTextureDescription(32, 32, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        ResourceVersion externalBefore = external.CurrentVersion;
        ResourceVersion importedBefore = imported.ColorTextures[0].CurrentVersion;
        HookRecorder hook = new();

        using FrameGraph graph = new(GD, "CaptureHook");
        AddPasses(graph, external, imported);
        ExecutionTask task = graph.Execute(hook);
        GD.WaitForIdle();

        return new Run(hook, external, externalBefore, imported, importedBefore, task.Id);
    }

    [SkippableFact]
    public void ThreePassGraph_CallbackOrder()
    {
        Run run = Execute();

        Assert.Equal(
            ["Begin", "Describe:3:3", "PassEnd:Produce", "PassEnd:Consume", "PassEnd:Finish", "End"],
            run.Hook.Log);
        Assert.Equal(run.ExecutionId, run.Hook.ExecutionId);
        Assert.Equal("CaptureHook", run.Hook.GraphName);
        Assert.Equal(0, run.Hook.Group.GroupIndex);
        Assert.Equal(new[] { "Produce", "Consume", "Finish" }, run.Hook.Group.Passes.ToArray().Select(p => p.Name));
    }

    [SkippableFact]
    public void ResourceTable_ListsBackingsAndOrigin()
    {
        Run run = Execute();
        GraphResourceInfo[] resources = run.Hook.Group.Resources.ToArray();

        GraphResourceInfo a = Assert.Single(resources, r => r.Id == ColorA);
        Assert.Equal(GraphResourceOrigin.Transient, a.Origin);
        Assert.Single(a.Backings.ToArray());

        GraphResourceInfo imported = Assert.Single(resources, r => r.Origin == GraphResourceOrigin.Imported);
        Assert.Equal(GraphResourceOrigin.Imported, imported.Origin);
        GraphBacking importedBacking = Assert.Single(imported.Backings.ToArray());
        Assert.Equal(run.ImportedTexture.ColorTextures[0].ResourceId, importedBacking.Id);
        Assert.Equal(run.ImportedBefore, importedBacking.EntryVersion);
    }

    [SkippableFact]
    public void PassReferences_CoverDeclaredCommandAndAttachmentVersions()
    {
        Run run = Execute();
        GraphBacking aBacking = Assert.Single(Assert.Single(run.Hook.Group.Resources.ToArray(), r => r.Id == ColorA).Backings.ToArray());
        uint aEntry = aBacking.EntryVersion.Version;

        PassReference[] produce = run.Hook.References["Produce"];
        PassReference produced = Assert.Single(produce, r => r.First.Resource == aBacking.Id);
        Assert.Equal(aEntry, produced.First.Version);
        Assert.Equal(aEntry + 1, produced.LastVersion);
        Assert.NotNull(produced.Texture);
        Assert.False(produced.NeedsContents);
        PassReference external = Assert.Single(produce, r => r.First.Resource == run.External.ResourceId);
        Assert.True(external.NeedsContents);
        Assert.Equal(run.ExternalBefore, external.First);
        Assert.Equal(run.ExternalBefore.Version + 1, external.LastVersion);
        Assert.Equal("ExternalMesh", external.Name);
        Assert.Equal(64u, external.Buffer!.Value.SizeInBytes);

        PassReference[] consume = run.Hook.References["Consume"];
        PassReference sampled = Assert.Single(consume, r => r.First.Resource == aBacking.Id);
        Assert.Equal(aEntry + 1, sampled.First.Version);
        Assert.Equal(sampled.First.Version, sampled.LastVersion);
        Assert.False(sampled.NeedsContents);
        PassReference attachment = Assert.Single(consume, r => r.First.Resource == run.ImportedTexture.ColorTextures[0].ResourceId);
        Assert.Equal(run.ImportedBefore, attachment.First);
        Assert.Equal(run.ImportedBefore.Version + 1, attachment.LastVersion);
        Assert.DoesNotContain(run.Hook.References["Finish"], r => r.First.Resource == run.External.ResourceId);
    }

    [SkippableFact]
    public void NoCaptureAssigned_RunsNormally()
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        using FrameGraph graph = new(GD, "Test");
        AddPasses(graph, external, null);
        graph.Execute();
        GD.WaitForIdle();

        Assert.Equal(1u, external.ContentVersion);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCaptureHookTests : CaptureHookTests<VulkanDeviceCreator> { }
#endif
