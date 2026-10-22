using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal static class RenderKinds
{
    public static readonly GraphKind World = GraphKind.Intern("Graphite.World");
    public static readonly GraphKind CaptureFill = GraphKind.Intern("Graphite.CaptureFill");
    public static readonly GraphKind Readback = GraphKind.Intern("Graphite.Readback");

    public static readonly RenderResourceID FillSource = RenderResourceID.Intern("Graphite.CaptureFill.Source");
    public static readonly RenderResourceID FillTarget = RenderResourceID.Intern("Graphite.CaptureFill.Target");
    public static readonly RenderResourceID FillAmbient = RenderResourceID.Intern("Graphite.CaptureFill.Ambient");
    public static readonly RenderResourceID ReadbackTexture = RenderResourceID.Intern("Graphite.Readback.Texture");
    public static readonly RenderResourceID ReadbackBuffer = RenderResourceID.Intern("Graphite.Readback.Buffer");

    public static GraphKind Of(ViewKind kind, in CameraShape shape)
    {
        string name = kind switch
        {
            ViewKind.SkyCapture => "Graphite.SkyCapture",
            ViewKind.ReflectionProbe => "Graphite.ProbeCapture",
            _ => "Graphite.Camera",
        };
        return GraphKind.Intern($"{name} {shape}");
    }

    public static GraphScope[] Scopes(IEnumerable<GraphKind> views)
    {
        List<GraphScope> scopes = [new(World, GraphKind.Root), new(CaptureFill, World)];
        foreach (GraphKind view in views)
            scopes.Add(new GraphScope(view, World));
        scopes.Add(new GraphScope(Readback, GraphKind.Root));
        return [.. scopes];
    }
}
