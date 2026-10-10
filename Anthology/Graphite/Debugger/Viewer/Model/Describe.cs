using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger.Viewer;

internal static class Describe
{
    public static string Name(VertexAttributeID id) => VertexAttributeID.ToString(id) ?? id.ToString();

    public static string Name(PropertyID id) => PropertyID.ToString(id) ?? id.ToString();

    public static IEnumerable<VertexElementDescription> Elements(EquatableArray<VertexLayoutDescription> layouts)
        => layouts.SelectMany(l => l.Elements ?? []);

    public static IEnumerable<(uint Set, ResourceLayoutElementDescription Element)> Elements(EquatableArray<ResourceLayoutDescription> layouts)
        => layouts.SelectMany(l => (l.Elements ?? []).Select(e => (l.Set, e)));

    public static int Components(VertexElementFormat format)
    {
        string name = format.ToString().Replace("_Norm", "");
        return name.EndsWith('4') ? 4 : name.EndsWith('3') ? 3 : name.EndsWith('2') ? 2 : 1;
    }

    public static string Color(BlendAttachmentDescription b) => $"{b.SourceColorFactor} / {b.ColorFunction} / {b.DestinationColorFactor}";

    public static string Alpha(BlendAttachmentDescription b) => $"{b.SourceAlphaFactor} / {b.AlphaFunction} / {b.DestinationAlphaFactor}";

    public static string Stencil(StencilBehaviorDescription s) => $"{s.Fail} / {s.Pass} / {s.DepthFail} / {s.Comparison}";

    public static string Outputs(OutputDescription o)
    {
        string colors = o.ColorFormats is { Length: > 0 } c ? string.Join(", ", c) : "none";
        return o.DepthFormat is { } depth ? $"{colors} + {depth}" : colors;
    }

    public static string Ops(AttachmentOps ops) => $"{ops.Load} / {ops.Store}";

    public static int IndexSize(IndexFormat format) => format == IndexFormat.UInt32 ? 4 : 2;
}
