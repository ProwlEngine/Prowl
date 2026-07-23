using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Vector;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Round-trips a Snapshot (including the paired ProfiledFrame and every SnapshotResource byte blob)
/// to/from an EchoObject, for .prowlsnap files (Echo binary format).
///
/// ProfiledPipelineState's BlendState/DepthStencilState/RasterizerState are not round-tripped yet -
/// only a debug summary string is persisted (ThreadGroupSize is, since it's flat scalars). A live,
/// in-memory ProfiledFrame still carries the full structured state; only the on-disk .prowlsnap loses
/// it. Follow-up: give BlendStateDescription/DepthStencilStateDescription/RasterizerStateDescription
/// their own Echo mapping.
/// </summary>
public static class SnapshotSerializer
{
    public static EchoObject ToEcho(Snapshot s)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", s.Name == null ? new EchoObject(EchoType.Null, null) : new EchoObject(s.Name));
        e.Add("FrameIndex", new EchoObject(s.FrameIndex));
        e.Add("Frame", FrameToEcho(s.Frame));
        e.Add("Resources", ListToEcho(s.Resources, ResourceToEcho));
        return e;
    }

    public static Snapshot FromEcho(EchoObject e)
    {
        string? name = e["Name"].TagType == EchoType.Null ? null : e["Name"].StringValue;
        return new Snapshot(
            name,
            e["FrameIndex"].LongValue,
            FrameFromEcho(e["Frame"]),
            ListFromEcho(e["Resources"], ResourceFromEcho));
    }

    // ProfiledFrame

    private static EchoObject FrameToEcho(ProfiledFrame f)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("FrameIndex", new EchoObject(f.FrameIndex));
        e.Add("FrameMilliseconds", new EchoObject(f.FrameMilliseconds));
        e.Add("Fps", new EchoObject(f.Fps));
        e.Add("Counters", CountersToEcho(f.Counters));
        e.Add("Views", ListToEcho(f.Views, ViewToEcho));
        e.Add("CpuRoot", f.CpuRoot is { } cpuRoot ? TimeSampleToEcho(cpuRoot) : new EchoObject(EchoType.Null, null));
        e.Add("GpuRoot", f.GpuRoot is { } gpuRoot ? TimeSampleToEcho(gpuRoot) : new EchoObject(EchoType.Null, null));
        e.Add("Submits", ListToEcho(f.Submits, SubmitToEcho));
        e.Add("HasCaptureDepth", new EchoObject(f.HasCaptureDepth));
        return e;
    }

    private static ProfiledFrame FrameFromEcho(EchoObject e)
    {
        var frame = new ProfiledFrame
        {
            FrameIndex = e["FrameIndex"].LongValue,
            FrameMilliseconds = e["FrameMilliseconds"].DoubleValue,
            Fps = e["Fps"].DoubleValue,
            HasCaptureDepth = e["HasCaptureDepth"].BoolValue,
            CpuRoot = e["CpuRoot"].TagType == EchoType.Null ? null : TimeSampleFromEcho(e["CpuRoot"]),
            GpuRoot = e["GpuRoot"].TagType == EchoType.Null ? null : TimeSampleFromEcho(e["GpuRoot"]),
        };
        frame.SetCounters(CountersFromEcho(e["Counters"]));
        foreach (SubmitRecord s in ListFromEcho(e["Submits"], SubmitFromEcho))
            frame.AddSubmit(s);
        foreach (EchoObject viewEcho in e["Views"].List)
            ViewFromEcho(frame, viewEcho);
        return frame;
    }

    private static EchoObject CountersToEcho(IReadOnlyList<CounterValue> c)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Values", ListToEcho(c, CounterValueToEcho));
        return e;
    }

    private static IReadOnlyList<CounterValue> CountersFromEcho(EchoObject e)
        => ListFromEcho(e["Values"], CounterValueFromEcho);

    private static EchoObject CounterValueToEcho(CounterValue v)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(v.Name));
        e.Add("Category", new EchoObject((int)v.Category));
        e.Add("Unit", new EchoObject((int)v.Unit));
        e.Add("Value", new EchoObject(v.Value));
        return e;
    }

    private static CounterValue CounterValueFromEcho(EchoObject e)
        => new(e["Name"].StringValue, (CounterCategory)e["Category"].IntValue, (CounterUnit)e["Unit"].IntValue, e["Value"].DoubleValue);

    // View / Pass / CommandBuffer / PipelineSwitch / CallingObject / DrawCall

    private static EchoObject ViewToEcho(ProfiledView v)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(v.Name));
        e.Add("CpuMilliseconds", new EchoObject(v.CpuMilliseconds));
        // GpuMilliseconds is a computed rollup of the Passes below - not serialized, it's re-derived on load.
        e.Add("RegisteredObjects", new EchoObject(v.RegisteredObjects));
        e.Add("CulledObjects", new EchoObject(v.CulledObjects));
        e.Add("TotalObjects", new EchoObject(v.TotalObjects));
        e.Add("DrawCallCount", new EchoObject(v.DrawCallCount));
        e.Add("Passes", ListToEcho(v.Passes, PassToEcho));
        e.Add("Edges", ListToEcho(v.Edges, PassEdgeToEcho));
        return e;
    }

    private static void ViewFromEcho(ProfiledFrame frame, EchoObject e)
    {
        ProfiledView view = frame.View(e["Name"].StringValue);
        view.SetCpuMilliseconds(e["CpuMilliseconds"].DoubleValue);
        view.SetObjectCounts(
            e["RegisteredObjects"].IntValue,
            e["CulledObjects"].IntValue,
            e["TotalObjects"].IntValue,
            e["DrawCallCount"].IntValue);

        foreach (EchoObject passEcho in e["Passes"].List)
            PassFromEcho(view, passEcho);
        foreach (PassEdge edge in ListFromEcho(e["Edges"], PassEdgeFromEcho))
            view.AddEdge(edge);
    }

    private static EchoObject PassToEcho(ProfiledPass p)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Index", new EchoObject(p.Index));
        e.Add("Name", new EchoObject(p.Name));
        e.Add("CpuMilliseconds", new EchoObject(p.CpuMilliseconds));
        // GpuMilliseconds is a computed rollup of the CommandBuffers below - not serialized.
        e.Add("Inputs", ListToEcho(p.Inputs, ResourceRefToEcho));
        e.Add("Outputs", ListToEcho(p.Outputs, ResourceRefToEcho));
        e.Add("CommandBuffers", ListToEcho(p.CommandBuffers, CommandBufferToEcho));
        e.Add("CpuSamples", ListToEcho(p.CpuSamples, TimeSampleToEcho));
        return e;
    }

    private static void PassFromEcho(ProfiledView view, EchoObject e)
    {
        ProfiledPass pass = view.Pass(e["Index"].IntValue, e["Name"].StringValue);
        pass.SetCpuTiming(e["CpuMilliseconds"].DoubleValue, ListFromEcho(e["CpuSamples"], TimeSampleFromEcho));
        pass.SetResources(ListFromEcho(e["Inputs"], ResourceRefFromEcho), ListFromEcho(e["Outputs"], ResourceRefFromEcho));

        foreach (EchoObject cbEcho in e["CommandBuffers"].List)
            CommandBufferFromEcho(pass, cbEcho);
    }

    private static EchoObject PassEdgeToEcho(PassEdge p)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("FromPass", new EchoObject(p.FromPass));
        e.Add("ToPass", new EchoObject(p.ToPass));
        e.Add("Resource", ResourceRefToEcho(p.Resource));
        return e;
    }

    private static PassEdge PassEdgeFromEcho(EchoObject e)
        => new(e["FromPass"].IntValue, e["ToPass"].IntValue, ResourceRefFromEcho(e["Resource"]));

    private static EchoObject CommandBufferToEcho(ProfiledCommandBuffer c)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Id", new EchoObject(c.Id));
        e.Add("Name", new EchoObject(c.Name));
        e.Add("GpuMilliseconds", new EchoObject(c.GpuMilliseconds));
        e.Add("Switches", ListToEcho(c.Switches, SwitchToEcho));
        return e;
    }

    private static void CommandBufferFromEcho(ProfiledPass pass, EchoObject e)
    {
        ProfiledCommandBuffer cb = pass.CommandBuffer(e["Id"].ULongValue, e["Name"].StringValue);
        cb.SetGpuMs(e["GpuMilliseconds"].DoubleValue);

        foreach (ProfiledPipelineSwitch sw in ListFromEcho(e["Switches"], SwitchFromEcho))
            cb.AddSwitchInstance(sw);
    }

    private static EchoObject SwitchToEcho(ProfiledPipelineSwitch s)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("ShaderName", new EchoObject(s.ShaderName));
        e.Add("IsCompute", new EchoObject(s.IsCompute));
        e.Add("Stages", new EchoObject((int)s.Stages));
        e.Add("PassName", new EchoObject(s.PassName));
        e.Add("Variant", new EchoObject(s.Variant));
        e.Add("Tags", TagsToEcho(s.Tags));
        e.Add("MaterialName", new EchoObject(s.MaterialName));
        e.Add("State", s.State is { } state ? PipelineStateToEcho(state) : new EchoObject(EchoType.Null, null));
        e.Add("Objects", ListToEcho(s.Objects, CallingObjectToEcho));
        // Draws not tied to any calling object - post-process blits, fullscreen passes, user-invoked
        // immediate draws. See DrawHierarchyCollector.FlushLooseDraws.
        e.Add("Draws", ListToEcho(s.Draws, DrawCallToEcho));
        return e;
    }

    private static ProfiledPipelineSwitch SwitchFromEcho(EchoObject e)
    {
        var sw = new ProfiledPipelineSwitch(
            e["ShaderName"].StringValue,
            e["IsCompute"].BoolValue,
            (ShaderStages)e["Stages"].IntValue,
            e["PassName"].StringValue,
            e["Variant"].StringValue,
            TagsFromEcho(e["Tags"]),
            e["MaterialName"].StringValue,
            e["State"].TagType == EchoType.Null ? null : PipelineStateFromEcho(e["State"]));

        foreach (ProfiledCallingObject obj in ListFromEcho(e["Objects"], CallingObjectFromEcho))
            sw.AddObjectInstance(obj);
        foreach (ProfiledDrawCall draw in ListFromEcho(e["Draws"], DrawCallFromEcho))
            sw.AddDraw(draw);

        return sw;
    }

    private static EchoObject PipelineStateToEcho(ProfiledPipelineState s)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Debug", new EchoObject(DescribePipelineState(s)));
        e.Add("ThreadGroupSizeX", NullableUIntToEcho(s.ThreadGroupSizeX));
        e.Add("ThreadGroupSizeY", NullableUIntToEcho(s.ThreadGroupSizeY));
        e.Add("ThreadGroupSizeZ", NullableUIntToEcho(s.ThreadGroupSizeZ));
        return e;
    }

    private static ProfiledPipelineState PipelineStateFromEcho(EchoObject e)
    {
        return new ProfiledPipelineState(
            null, null, null,
            NullableUIntFromEcho(e["ThreadGroupSizeX"]),
            NullableUIntFromEcho(e["ThreadGroupSizeY"]),
            NullableUIntFromEcho(e["ThreadGroupSizeZ"]));
    }

    private static string DescribePipelineState(ProfiledPipelineState s)
    {
        if (s.BlendState is { } blend && s.DepthStencilState is { } depth && s.RasterizerState is { } raster)
        {
            return $"Blend={blend.AttachmentStates?.Length ?? 0} attachments, AlphaToCoverage={blend.AlphaToCoverageEnabled}, "
                 + $"DepthTest={depth.DepthTestEnabled}, DepthWrite={depth.DepthWriteEnabled}, DepthCmp={depth.DepthComparison}, "
                 + $"Cull={raster.CullMode}, FrontFace={raster.FrontFace}";
        }
        return "";
    }

    private static EchoObject NullableUIntToEcho(uint? v) => v.HasValue ? new EchoObject(v.Value) : new EchoObject(EchoType.Null, null);
    private static uint? NullableUIntFromEcho(EchoObject e) => e.TagType == EchoType.Null ? null : e.UIntValue;

    private static EchoObject CallingObjectToEcho(ProfiledCallingObject o)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Label", new EchoObject(o.Label));
        e.Add("MaterialName", new EchoObject(o.MaterialName));
        e.Add("MeshName", new EchoObject(o.MeshName));
        e.Add("Layer", new EchoObject(o.Layer));
        e.Add("Position", Float3ToEcho(o.Position));
        e.Add("Registered", new EchoObject(o.Registered));
        e.Add("Culled", new EchoObject(o.Culled));
        e.Add("Draws", ListToEcho(o.Draws, DrawCallToEcho));
        return e;
    }

    private static ProfiledCallingObject CallingObjectFromEcho(EchoObject e)
    {
        var obj = new ProfiledCallingObject(
            e["Label"].StringValue,
            e["MaterialName"].StringValue,
            e["MeshName"].StringValue,
            e["Layer"].IntValue,
            Float3FromEcho(e["Position"]),
            e["Registered"].BoolValue,
            e["Culled"].BoolValue);

        foreach (ProfiledDrawCall draw in ListFromEcho(e["Draws"], DrawCallFromEcho))
            obj.AddDraw(draw);

        return obj;
    }

    private static EchoObject DrawCallToEcho(ProfiledDrawCall d)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Draw", d.Draw is { } draw ? DrawCallInfoToEcho(draw) : new EchoObject(EchoType.Null, null));
        e.Add("Dispatch", d.Dispatch is { } dispatch ? DispatchCallInfoToEcho(dispatch) : new EchoObject(EchoType.Null, null));
        e.Add("Culled", new EchoObject(d.Culled));
        e.Add("ReferenceBuffers", ListToEcho(d.ReferenceBuffers, ReferenceBufferToEcho));
        return e;
    }

    private static ProfiledDrawCall DrawCallFromEcho(EchoObject e)
    {
        return new ProfiledDrawCall(
            e["Draw"].TagType == EchoType.Null ? null : DrawCallInfoFromEcho(e["Draw"]),
            e["Dispatch"].TagType == EchoType.Null ? null : DispatchCallInfoFromEcho(e["Dispatch"]),
            e["Culled"].BoolValue,
            ListFromEcho(e["ReferenceBuffers"], ReferenceBufferFromEcho).ToArray());
    }

    private static EchoObject DrawCallInfoToEcho(DrawCallInfo d)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Kind", new EchoObject((int)d.Kind));
        e.Add("VertexOrIndexCount", new EchoObject(d.VertexOrIndexCount));
        e.Add("InstanceCount", new EchoObject(d.InstanceCount));
        e.Add("DrawCount", new EchoObject(d.DrawCount));
        e.Add("IsIndirect", new EchoObject(d.IsIndirect));
        return e;
    }

    private static DrawCallInfo DrawCallInfoFromEcho(EchoObject e)
        => new((DrawKind)e["Kind"].IntValue, e["VertexOrIndexCount"].UIntValue, e["InstanceCount"].UIntValue, e["DrawCount"].UIntValue, e["IsIndirect"].BoolValue);

    private static EchoObject DispatchCallInfoToEcho(DispatchCallInfo d)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("GroupCountX", new EchoObject(d.GroupCountX));
        e.Add("GroupCountY", new EchoObject(d.GroupCountY));
        e.Add("GroupCountZ", new EchoObject(d.GroupCountZ));
        e.Add("IsIndirect", new EchoObject(d.IsIndirect));
        return e;
    }

    private static DispatchCallInfo DispatchCallInfoFromEcho(EchoObject e)
        => new(e["GroupCountX"].UIntValue, e["GroupCountY"].UIntValue, e["GroupCountZ"].UIntValue, e["IsIndirect"].BoolValue);

    private static EchoObject ReferenceBufferToEcho(ReferenceBuffer b)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(b.Name));
        e.Add("SizeInBytes", new EchoObject(b.SizeInBytes));
        e.Add("ContentVersion", new EchoObject(b.ContentVersion));
        e.Add("ReadOnly", new EchoObject(b.ReadOnly));
        e.Add("Resource", ResourceIdToEcho(b.Resource));
        return e;
    }

    private static ReferenceBuffer ReferenceBufferFromEcho(EchoObject e)
    {
        return new ReferenceBuffer(
            e["Name"].StringValue, e["SizeInBytes"].UIntValue, e["ContentVersion"].UIntValue, e["ReadOnly"].BoolValue,
            ResourceIdFromEcho(e["Resource"]));
    }

    private static EchoObject TagsToEcho(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags == null)
            return new EchoObject(EchoType.Null, null);

        EchoObject e = EchoObject.NewCompound();
        foreach (KeyValuePair<string, string> kvp in tags)
            e.Add(kvp.Key, new EchoObject(kvp.Value));
        return e;
    }

    private static IReadOnlyDictionary<string, string>? TagsFromEcho(EchoObject e)
    {
        if (e.TagType == EchoType.Null)
            return null;

        var dict = new Dictionary<string, string>();
        foreach (string key in e.GetNames())
            dict[key] = e[key].StringValue;
        return dict;
    }

    private static EchoObject ResourceRefToEcho(ResourceRef r)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Id", new EchoObject(r.Id));
        e.Add("Name", new EchoObject(r.Name));
        e.Add("Kind", new EchoObject((int)r.Kind));
        e.Add("Resource", ResourceIdToEcho(r.Resource));
        return e;
    }

    private static ResourceRef ResourceRefFromEcho(EchoObject e)
        => new(e["Id"].UIntValue, e["Name"].StringValue, (ResourceRefKind)e["Kind"].IntValue, ResourceIdFromEcho(e["Resource"]));

    private static EchoObject ResourceIdToEcho(SnapshotResourceID r)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("ResourceId", new EchoObject(r.ResourceId));
        e.Add("Version", new EchoObject(r.Version));
        e.Add("IsValid", new EchoObject(r.IsValid));
        return e;
    }

    private static SnapshotResourceID ResourceIdFromEcho(EchoObject e)
        => new(e["ResourceId"].UIntValue, e["Version"].UIntValue, e["IsValid"].BoolValue);

    private static EchoObject Float3ToEcho(Float3 v)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("X", new EchoObject(v.X));
        e.Add("Y", new EchoObject(v.Y));
        e.Add("Z", new EchoObject(v.Z));
        return e;
    }

    private static Float3 Float3FromEcho(EchoObject e)
        => new(e["X"].FloatValue, e["Y"].FloatValue, e["Z"].FloatValue);

    private static EchoObject SubmitToEcho(SubmitRecord s)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Kind", new EchoObject((int)s.Kind));
        e.Add("Name", new EchoObject(s.Name));
        e.Add("CommandBufferCount", new EchoObject(s.CommandBufferCount));
        return e;
    }

    private static SubmitRecord SubmitFromEcho(EchoObject e)
        => new((SubmitKind)e["Kind"].IntValue, e["Name"].StringValue, e["CommandBufferCount"].UIntValue);

    private static EchoObject TimeSampleToEcho(TimeSample t)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(t.Name));
        e.Add("InclusiveMilliseconds", new EchoObject(t.InclusiveMilliseconds));
        e.Add("IsTransfer", new EchoObject(t.IsTransfer));
        e.Add("Children", ListToEcho(t.Children, TimeSampleToEcho));
        return e;
    }

    private static TimeSample TimeSampleFromEcho(EchoObject e)
        => new(e["Name"].StringValue, e["InclusiveMilliseconds"].DoubleValue, e["IsTransfer"].BoolValue, ListFromEcho(e["Children"], TimeSampleFromEcho).ToArray());

    // Snapshot resources

    private static EchoObject ResourceToEcho(SnapshotResource r)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("ResourceId", new EchoObject(r.ResourceId));
        e.Add("Name", new EchoObject(r.Name));
        e.Add("Kind", new EchoObject((int)r.Kind));
        e.Add("Versions", ListToEcho(r.Versions, ResourceVersionToEcho));
        return e;
    }

    private static SnapshotResource ResourceFromEcho(EchoObject e)
    {
        return new SnapshotResource(
            e["ResourceId"].UIntValue,
            e["Name"].StringValue,
            (SnapshotResourceKind)e["Kind"].IntValue,
            ListFromEcho(e["Versions"], ResourceVersionFromEcho));
    }

    private static EchoObject ResourceVersionToEcho(SnapshotResourceVersion v)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Version", new EchoObject(v.Version));
        e.Add("Subtextures", ListToEcho(v.Subtextures, SubTextureToEcho));
        e.Add("BufferData", new EchoObject(v.BufferData));
        e.Add("BufferMeta", v.BufferMeta is { } meta ? BufferMetaToEcho(meta) : new EchoObject(EchoType.Null, null));
        return e;
    }

    private static SnapshotResourceVersion ResourceVersionFromEcho(EchoObject e)
    {
        return new SnapshotResourceVersion(
            e["Version"].UIntValue,
            ListFromEcho(e["Subtextures"], SubTextureFromEcho),
            e["BufferData"].ByteArrayValue,
            e["BufferMeta"].TagType == EchoType.Null ? null : BufferMetaFromEcho(e["BufferMeta"]));
    }

    private static EchoObject SubTextureToEcho(SnapshotSubTexture t)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(t.Name));
        e.Add("Format", new EchoObject((int)t.Format));
        e.Add("Width", new EchoObject(t.Width));
        e.Add("Height", new EchoObject(t.Height));
        e.Add("Depth", new EchoObject(t.Depth));
        e.Add("MipLevels", new EchoObject(t.MipLevels));
        e.Add("Pixels", new EchoObject(t.Pixels));
        return e;
    }

    private static SnapshotSubTexture SubTextureFromEcho(EchoObject e)
    {
        return new SnapshotSubTexture(
            e["Name"].StringValue,
            (PixelFormat)e["Format"].IntValue,
            e["Width"].UIntValue,
            e["Height"].UIntValue,
            e["Depth"].UIntValue,
            e["MipLevels"].UIntValue,
            e["Pixels"].ByteArrayValue);
    }

    private static EchoObject BufferMetaToEcho(SnapshotBufferMeta m)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Kind", new EchoObject((int)m.Kind));
        e.Add("SizeBytes", new EchoObject(m.SizeBytes));
        e.Add("Stride", new EchoObject(m.Stride));
        e.Add("Layout", ListToEcho(m.Layout, BufferFieldToEcho));
        return e;
    }

    private static SnapshotBufferMeta BufferMetaFromEcho(EchoObject e)
    {
        return new SnapshotBufferMeta(
            (BufferUsage)e["Kind"].IntValue,
            e["SizeBytes"].UIntValue,
            e["Stride"].UIntValue,
            ListFromEcho(e["Layout"], BufferFieldFromEcho));
    }

    private static EchoObject BufferFieldToEcho(BufferField f)
    {
        EchoObject e = EchoObject.NewCompound();
        e.Add("Name", new EchoObject(f.Name));
        e.Add("Type", new EchoObject(f.Type));
        e.Add("Offset", new EchoObject(f.Offset));
        e.Add("SizeBytes", new EchoObject(f.SizeBytes));
        return e;
    }

    private static BufferField BufferFieldFromEcho(EchoObject e)
        => new(e["Name"].StringValue, e["Type"].StringValue, e["Offset"].UIntValue, e["SizeBytes"].UIntValue);

    // Generic list helpers

    private static EchoObject ListToEcho<T>(IReadOnlyList<T> items, System.Func<T, EchoObject> toEcho)
    {
        EchoObject list = EchoObject.NewList();
        foreach (T item in items)
            list.ListAdd(toEcho(item));
        return list;
    }

    private static IReadOnlyList<T> ListFromEcho<T>(EchoObject e, System.Func<EchoObject, T> fromEcho)
        => e.List.Select(fromEcho).ToList();
}
