namespace Prowl.Graphite;

/// <summary>One graph pass. Declares its reads and writes in Setup. It is an execution pass or an instance pass.</summary>
public interface IPass
{
    /// <summary>Debug name.</summary>
    string Name { get; }

    /// <summary>The kind of instance the pass runs in, the root kind for an execution pass.</summary>
    GraphKind Kind { get; }
}

/// <summary>A pass that runs once per execution, before every instance.</summary>
public interface IExecutionPass : IPass
{
    GraphKind IPass.Kind => GraphKind.Root;

    /// <summary>False skips the pass for this execution.</summary>
    bool IsEnabled() => true;

    /// <summary>Declare in/out textures and buffers.</summary>
    void Setup(RenderContextBuilder builder);

    /// <summary>Record rendering into cmd, already begun. The graph submits it after Render returns. Get textures via context.GetRenderTexture.</summary>
    void Render(RenderContext context, CommandBuffer cmd);
}

/// <summary>A pass that runs once per instance of its kind.</summary>
public interface IInstancePass : IPass
{
    /// <summary>False skips the pass for this instance.</summary>
    bool IsEnabled(GraphInstance instance) => true;

    /// <summary>Declare in/out textures and buffers.</summary>
    void Setup(InstanceContextBuilder builder);

    /// <summary>Record rendering into cmd, already begun. The graph submits it after Render returns. Get textures via context.GetRenderTexture.</summary>
    void Render(InstanceContext context, CommandBuffer cmd);
}
