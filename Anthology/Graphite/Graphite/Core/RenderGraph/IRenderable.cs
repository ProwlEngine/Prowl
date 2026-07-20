namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Empty marker for something a culler can turn into draw commands. No methods: only the
/// concrete culler that made it knows its real shape.
/// </summary>
public interface IRenderable { }
