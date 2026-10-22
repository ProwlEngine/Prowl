using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public sealed partial class FrameGraph
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private readonly PassContext _context = new();

    /// <summary>Runs every pass in the order added, ends the frame and presents swapchain targets written this frame.</summary>
    public ExecutionTask Execute(params IProfiler[] profilers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Building != null || _groupStack.Count != 0)
            throw new InvalidOperationException("Execute was called with a pass or a group still open.");

        try
        {
            GraphLayout layout = BuildLayout();
            return Device.ExecuteFrame(this, layout, _context, profilers);
        }
        finally
        {
            ResetFrame();
        }
    }

    private GraphLayout BuildLayout()
    {
        ulong hash = FnvOffset;
        foreach (GroupInfo group in _groups)
        {
            hash = Mix(hash, group.Name);
            hash = Mix(hash, group.ParentIndex);
        }

        for (int i = 0; i < _passCount; i++)
        {
            FramePass pass = _passes[i];
            hash = Mix(hash, pass.Name);
            hash = Mix(hash, pass.Group);
            foreach (ResourceAccess access in pass.Accesses)
            {
                hash = Mix(hash, access.Id.Value);
                hash = Mix(hash, access.IsTexture ? 1 : 0);
                hash = Mix(hash, access.IsOutput ? 1 : 0);
                hash = Mix(hash, (int)access.TextureUsage);
                hash = Mix(hash, access.DepthUsage is TextureState depth ? (int)depth + 1 : 0);
                hash = Mix(hash, (int)access.BufferUsage);
            }
        }

        if (_layout != null && _layout.Hash == hash)
            return _layout;

        GroupInfo[] groups = _groups.ToArray();
        PassLayout[] passes = new PassLayout[_passCount];
        for (int i = 0; i < _passCount; i++)
        {
            FramePass pass = _passes[i];
            PassResourceAccess[] accesses = new PassResourceAccess[pass.Accesses.Count];
            for (int a = 0; a < accesses.Length; a++)
            {
                ResourceAccess access = pass.Accesses[a];
                accesses[a] = new PassResourceAccess(
                    access.Id,
                    access.IsTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer,
                    access.IsOutput,
                    access.TextureUsage,
                    access.DepthUsage,
                    access.BufferUsage);
            }
            passes[i] = new PassLayout(pass.Name, pass.Group, accesses);
        }

        ResourceLayout[] resources = new ResourceLayout[_resourceCount];
        for (int i = 0; i < _resourceCount; i++)
        {
            FrameResource resource = _resources[i];
            resources[i] = new ResourceLayout(resource.Id, resource.Kind, resource.Lifetime, resource.Imported, resource.Scope);
        }

        return _layout = new GraphLayout(hash, groups, passes, resources);
    }

    private static ulong Mix(ulong hash, int value)
    {
        hash = (hash ^ (uint)value) * FnvPrime;
        return (hash ^ (hash >> 29)) * FnvPrime;
    }

    private static ulong Mix(ulong hash, string text)
    {
        foreach (char c in text)
            hash = (hash ^ c) * FnvPrime;
        return Mix(hash, text.Length);
    }
}
